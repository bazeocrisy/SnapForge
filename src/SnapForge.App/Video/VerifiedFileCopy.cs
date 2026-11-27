using System.IO;
using System.Security.Cryptography;

namespace SnapForge.App.Video;

/// <summary>The copy did not match its source (size or SHA-256); nothing was replaced.</summary>
internal sealed class CopyVerificationException : IOException
{
    public CopyVerificationException()
    {
    }

    public CopyVerificationException(string message)
        : base(message)
    {
    }

    public CopyVerificationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// FA-022: copies a file to a destination so that the destination is either the complete, verified copy or untouched.
/// <list type="number">
/// <item>Copy to a unique temporary file in the destination folder (<c>.&lt;name&gt;.snapforge-tmp-&lt;random&gt;</c>),
/// hashing the source as it is read.</item>
/// <item>Flush to the disk (<c>FileStream.Flush(true)</c>).</item>
/// <item>Verify: the temporary file's size and SHA-256 (read back from disk) equal the source's.</item>
/// <item>Only then move it into place in one step (<c>File.Move(…, overwrite: true)</c> = MoveFileEx with
/// MOVEFILE_REPLACE_EXISTING, atomic within a volume). An existing destination is replaced only by that step (the
/// user confirmed the overwrite in the save dialog).</item>
/// </list>
/// On any failure the temporary file is deleted, the source and any existing destination are left as they were, and
/// the exception is rethrown. Deleting the source (the Library copy) is the caller's job, after success.
/// </summary>
internal static class VerifiedFileCopy
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/> as described above.</summary>
    /// <param name="wrapDestination">Tests only: wraps the temporary file's stream (fault injection).</param>
    public static void Copy(string source, string destination, Func<Stream, Stream>? wrapDestination = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        string target = Path.GetFullPath(destination);
        if (string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The recording is already at that location.");
        }

        string folder = Path.GetDirectoryName(target) ?? throw new IOException("The destination folder is not valid.");
        string temp = TempPathFor(target);
        try
        {
            byte[] sourceHash;
            long sourceLength;
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                sourceLength = input.Length;
                using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize);
                Stream output = wrapDestination?.Invoke(file) ?? file;
                try
                {
                    byte[] buffer = new byte[BufferSize];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        hash.AppendData(buffer, 0, read);
                        output.Write(buffer, 0, read);
                    }

                    output.Flush();
                    file.Flush(flushToDisk: true);
                }
                finally
                {
                    if (!ReferenceEquals(output, file))
                    {
                        output.Dispose();
                    }
                }

                sourceHash = hash.GetHashAndReset();
            }

            long copiedLength = new FileInfo(temp).Length;
            if (copiedLength != sourceLength)
            {
                throw new CopyVerificationException($"The copy in {folder} is {copiedLength} bytes instead of {sourceLength}.");
            }

            byte[] copiedHash;
            using (var check = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            {
                copiedHash = SHA256.HashData(check);
            }

            if (!CryptographicOperations.FixedTimeEquals(sourceHash, copiedHash))
            {
                throw new CopyVerificationException($"The copy in {folder} does not match the recording (checksum differs).");
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>The temporary name used next to <paramref name="destination"/> (unique per call).</summary>
    internal static string TempPathFor(string destination)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        string random = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        return Path.Combine(folder, $".{Path.GetFileName(destination)}.snapforge-tmp-{random}");
    }

    /// <summary>True for names this helper creates (so leftovers can be recognised).</summary>
    internal static bool IsTempName(string fileName) => fileName.StartsWith('.') && fileName.Contains(".snapforge-tmp-", StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
