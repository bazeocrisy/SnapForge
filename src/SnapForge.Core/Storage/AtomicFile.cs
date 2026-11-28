using System.Security.Cryptography;

namespace SnapForge.Core.Storage;

/// <summary>
/// Crash-resistant file writes: data goes to a temporary file in the same folder, is flushed to disk, and then
/// atomically renamed over the target. A crash or full disk at any point leaves either the old file or the new
/// file, never a partial one. Stale temporary files are removed by <see cref="CleanupTemporaryFiles"/>.
/// </summary>
public static class AtomicFile
{
    public const string TempSuffix = ".sftmp";

    /// <summary>Test hook: wraps the temporary-file stream (used to simulate disk-full and interrupted writes).</summary>
    internal static AsyncLocal<Func<Stream, Stream>?> WriteDecorator { get; } = new();

    /// <summary>Test hook: skip the flush-to-disk (only for seeding large test libraries quickly).</summary>
    internal static AsyncLocal<bool> SkipDiskFlush { get; } = new();

    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full) ?? throw new IOException($"Invalid path: {path}");
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}{TempSuffix}");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
            {
                Stream target = WriteDecorator.Value?.Invoke(file) ?? file;
                write(target);
                target.Flush();
                file.Flush(flushToDisk: !SkipDiskFlush.Value);
            }

            DurabilityFaults.Checkpoint("atomic.flushed:" + Path.GetFileName(full));
            File.Move(temp, full, overwrite: true);
            DurabilityFaults.Checkpoint("atomic.renamed:" + Path.GetFileName(full));
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        byte[] copy = bytes.ToArray();
        Write(path, s => s.Write(copy));
    }

    public static void WriteAllText(string path, string text) => WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>Removes temporary files left behind by a crash, under <paramref name="directory"/> (recursive).</summary>
    public static int CleanupTemporaryFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*" + TempSuffix, SearchOption.AllDirectories))
        {
            if (TryDelete(file))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Best-effort removal of a file's content before deleting it: overwrites with zeros, flushes, deletes.
    /// On SSDs, copy-on-write file systems, backups or volume shadow copies the old data may survive; see docs.
    /// </summary>
    public static void OverwriteAndDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            long length = new FileInfo(path).Length;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                byte[] zeros = new byte[Math.Min(length, 1 << 20)];
                for (long written = 0; written < length; written += zeros.Length)
                {
                    fs.Write(zeros, 0, (int)Math.Min(zeros.Length, length - written));
                }

                fs.Flush(flushToDisk: true);
            }
        }
        catch (IOException)
        {
            // Fall through to delete.
        }
        catch (UnauthorizedAccessException)
        {
        }

        DurabilityFaults.Checkpoint("delete.overwritten:" + Path.GetFileName(path));
        File.Delete(path);
    }

    public static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>Size of the buffer used by the streaming helpers (no file is ever read whole).</summary>
    public const int StreamBufferSize = 1 << 20;

    /// <summary>Streams a file through SHA-256 (never loads it whole). <paramref name="progress"/> receives bytes read so far.</summary>
    public static (string Sha256, long Length) HashFile(string path, Action<long>? progress = null, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.SequentialScan);
        return HashStream(stream, long.MaxValue, progress, cancellationToken);
    }

    /// <summary>
    /// Streams at most <paramref name="maxBytes"/> + 1 bytes of <paramref name="stream"/> through SHA-256 and returns
    /// the hash and the number of bytes read (callers compare it with the expected length; reading one byte more
    /// detects longer-than-declared data without reading it all).
    /// </summary>
    public static (string Sha256, long Length) HashStream(Stream stream, long maxBytes, Action<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = CopyBounded(stream, null, hash, maxBytes, progress, cancellationToken);
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    /// <summary>
    /// Copies a file to <paramref name="destination"/> atomically (temp + flush + rename), hashing the bytes as they
    /// are copied. Never buffers the whole file.
    /// </summary>
    public static (string Sha256, long Length) CopyFile(string source, string destination, Action<long>? progress = null, CancellationToken cancellationToken = default)
    {
        string sha = string.Empty;
        long length = 0;
        Write(destination, target =>
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            length = CopyBounded(input, target, hash, long.MaxValue, progress, cancellationToken);
            sha = Convert.ToHexStringLower(hash.GetHashAndReset());
        });
        return (sha, length);
    }

    /// <summary>
    /// Copies up to <paramref name="maxBytes"/> + 1 bytes from <paramref name="input"/> to <paramref name="output"/>
    /// (if any), feeding <paramref name="hash"/>; returns the number of bytes read.
    /// </summary>
    internal static long CopyBounded(Stream input, Stream? output, IncrementalHash? hash, long maxBytes, Action<long>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[StreamBufferSize];
        long total = 0;
        long limit = maxBytes == long.MaxValue ? long.MaxValue : maxBytes + 1;
        while (total < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int want = (int)Math.Min(buffer.Length, limit - total);
            int read = input.Read(buffer, 0, want);
            if (read <= 0)
            {
                break;
            }

            hash?.AppendData(buffer, 0, read);
            output?.Write(buffer, 0, read);
            total += read;
            progress?.Invoke(total);
        }

        return total;
    }

    /// <summary>
    /// True if some handle (in this or another process) has the file open, so it may be a write in progress: opening it
    /// for exclusive access fails (a sharing violation on Windows, the advisory lock .NET takes on other systems).
    /// </summary>
    internal static bool IsOpenElsewhere(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Deletes a file if it exists; false if it did not exist or could not be deleted.</summary>
    internal static bool TryDeleteFile(string path) => TryDelete(path);

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }
}
