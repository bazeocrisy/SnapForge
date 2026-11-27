using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SnapForge.App.Video;

/// <summary>
/// A small diagnostic log for the recording pipeline (stalled threads, deliberately leaked native objects, camera
/// failures). Kept in memory (last 200 entries) and, when <see cref="FilePath"/> is set, appended to a file capped at
/// 1 MB. Never contains recorded content.
/// </summary>
internal static class RecordingLog
{
    private const int MaxEntries = 200;
    private const long MaxFileBytes = 1024 * 1024;
    private static readonly Lock Gate = new();
    private static readonly Queue<string> Entries = new();

    /// <summary>File the entries are appended to (null = memory only).</summary>
    public static string? FilePath { get; set; }

    public static IReadOnlyList<string> Recent
    {
        get
        {
            lock (Gate)
            {
                return [.. Entries];
            }
        }
    }

    public static void Write(string message)
    {
        string line = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + " " + message;
        Trace.WriteLine("SnapForge recording: " + line);
        lock (Gate)
        {
            Entries.Enqueue(line);
            while (Entries.Count > MaxEntries)
            {
                Entries.Dequeue();
            }

            if (FilePath is not { } path)
            {
                return;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
