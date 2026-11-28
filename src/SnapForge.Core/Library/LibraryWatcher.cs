using SnapForge.Core.Storage;

namespace SnapForge.Core.Library;

/// <summary>
/// Keeps a <see cref="CaptureLibrary"/>'s index up to date with changes made by another process sharing the same
/// Library folder (SnapForge Capture and SnapForge Editor). Watches the captures and videos folders for item folders
/// created, renamed or deleted and for manifest and metadata files written; after a quiet period
/// (<see cref="DefaultDebounce"/>) it calls <see cref="CaptureLibrary.RefreshFromDisk"/> for each affected item, which
/// raises <see cref="CaptureLibrary.Changed"/>. Staging folders (<c>.creating-</c>) and temporary files are ignored.
/// If the watcher's buffer overflows (many changes at once), it falls back to <see cref="CaptureLibrary.Reindex"/>.
/// Opt-in: the app creates one per opened library and disposes it when done. Changes made by this process are
/// refreshed again too (harmless: the record is re-read and Changed raised once more).
/// </summary>
public sealed class LibraryWatcher : IDisposable
{
    /// <summary>The default quiet period before refreshing.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly CaptureLibrary _library;
    private readonly TimeSpan _debounce;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private readonly object _sync = new();
    private readonly HashSet<Guid> _pending = [];
    private bool _reindex;
    private bool _running;
    private bool _disposed;

    /// <summary>Starts watching <paramref name="library"/>'s folders.</summary>
    /// <param name="library">The library to keep up to date.</param>
    /// <param name="debounce">Quiet period before refreshing (default <see cref="DefaultDebounce"/>).</param>
    public LibraryWatcher(CaptureLibrary library, TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        _debounce = debounce ?? DefaultDebounce;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            Watch(library.CapturesDirectory);
            Watch(library.VideosDirectory);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Raised (on a thread-pool thread) when refreshing failed; the next change or overflow retries.</summary>
    public event EventHandler<ErrorEventArgs>? RefreshFailed;

    /// <summary>Stops watching. Refreshes already running finish; none start afterwards.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
        }

        foreach (FileSystemWatcher w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }

        _timer.Dispose();
    }

    /// <summary>
    /// The item id an event path refers to, or null: <c>&lt;dir&gt;/&lt;id&gt;</c> (the item folder) or
    /// <c>&lt;dir&gt;/&lt;id&gt;/(manifest.json|meta.json|video.json|recording.inprogress)</c>. Staging folders, temporary files and other
    /// content files are ignored (the manifest is always written last).
    /// </summary>
    internal static Guid? ItemIdOf(string watchedDirectory, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string relative = Path.GetRelativePath(watchedDirectory, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return null;
        }

        string[] parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2 || parts[0].StartsWith('.') || !Guid.TryParseExact(parts[0], "D", out Guid id))
        {
            return null; // ".creating-…", ".import-…" and other names that are not item folders
        }

        if (parts.Length == 2)
        {
            string name = parts[1];
            if (name.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase)
                || !(name.Equals(CaptureFormat.ManifestFile, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(CaptureFormat.MetaFile, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(CaptureLibrary.VideoManifestFile, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(CaptureLibrary.InProgressMarker, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
        }

        return id;
    }

    private void Watch(string directory)
    {
        Directory.CreateDirectory(directory);
        var w = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        w.Created += (_, e) => Note(directory, e.FullPath);
        w.Changed += (_, e) => Note(directory, e.FullPath);
        w.Deleted += (_, e) => Note(directory, e.FullPath);
        w.Renamed += (_, e) =>
        {
            Note(directory, e.OldFullPath);
            Note(directory, e.FullPath);
        };
        w.Error += (_, _) => Overflow();
        _watchers.Add(w);
        w.EnableRaisingEvents = true;
    }

    private void Note(string directory, string? path)
    {
        if (ItemIdOf(directory, path) is not { } id)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pending.Add(id);
            Arm();
        }
    }

    /// <summary>The watcher lost events (buffer overflow or a watcher error): re-read everything.</summary>
    internal void Overflow()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _reindex = true;
            Arm();
        }
    }

    private void Arm()
    {
        // Restarted on every event: refreshes run once the folder has been quiet for the debounce period.
        if (!_running)
        {
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        Guid[] ids;
        bool reindex;
        lock (_sync)
        {
            if (_disposed || _running)
            {
                return;
            }

            _running = true;
            ids = [.. _pending];
            _pending.Clear();
            reindex = _reindex;
            _reindex = false;
        }

        try
        {
            // Synchronous on this thread: the write gate is entered and left on the same thread.
            if (reindex)
            {
                _library.Reindex();
            }
            else
            {
                foreach (Guid id in ids)
                {
                    _library.RefreshFromDisk(id);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshFailed?.Invoke(this, new ErrorEventArgs(ex));
        }
        finally
        {
            lock (_sync)
            {
                _running = false;
                if (!_disposed && (_pending.Count > 0 || _reindex))
                {
                    _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }
}
