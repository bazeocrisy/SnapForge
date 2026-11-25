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

            File.Move(temp, full, overwrite: true);
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

        File.Delete(path);
    }

    public static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

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
