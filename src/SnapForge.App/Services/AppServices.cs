using System.IO;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;

namespace SnapForge.App.Services;

/// <summary>
/// Application-wide state: settings, the Capture Library, the Recent Captures Tray and the background writer.
/// Tests and the self-test create their own instance over temporary folders, so they never touch the user's library.
/// </summary>
internal sealed class AppServices : IDisposable
{
    private AppServices(string settingsPath, AppSettings settings, CaptureLibrary library, SettingsLoadResult? load = null)
    {
        SettingsPath = settingsPath;
        Settings = settings;
        SettingsLoad = load;
        SettingsReadOnly = load?.IsReadOnly ?? false;
        _settingsStamp = StampOf(settingsPath);
        Library = library;

        // Before the first index finishes, only check that the capture folder exists; deleted/trashed items are
        // pruned once the index is complete.
        Tray = RecentTray.Open(library.Root, id => library.IsIndexed ? library.Find(id) is { IsDeleted: false } : library.ExistsOnDisk(id));
    }

    /// <summary>Completes when the library's first full index (and start-up retention) has finished.</summary>
    public Task Indexing { get; private set; } = Task.CompletedTask;

    public static AppServices? Current { get; set; }

    public event EventHandler? SettingsChanged;

    /// <summary>Raised after the library moved to a new location (Library and Tray are new instances).</summary>
    public event EventHandler? LibraryReplaced;

    public string SettingsPath { get; }

    public AppSettings Settings { get; private set; }

    public CaptureLibrary Library { get; private set; }

    public RecentTray Tray { get; private set; }

    public LibraryWriter Writer { get; } = new();

    /// <summary>How the settings file was loaded (FA-019): damaged, newer version, unreadable… (null in tests).</summary>
    public SettingsLoadResult? SettingsLoad { get; }

    /// <summary>
    /// True when the settings file must not be written this session (it was written by a newer SnapForge or could not
    /// be read): changes apply in memory only, so the newer file is never overwritten (FA-019).
    /// </summary>
    public bool SettingsReadOnly { get; }

    /// <summary>The last settings save error, or null after a successful save (FA-020).</summary>
    public string? LastSettingsSaveError { get; private set; }

    /// <summary>Raised (on the calling thread) when settings could not be saved; the message says why (FA-020).</summary>
    public event EventHandler<string>? SettingsSaveFailed;

    public ThumbnailCache Thumbnails { get; } = new();

    /// <summary>Opens settings and the library. <paramref name="libraryOverride"/> is used by tests.</summary>
    /// <param name="deferIndex">
    /// True for the app: the library opens instantly and is indexed in the background by <see cref="StartIndexingAsync"/>
    /// so start-up never waits for thousands of captures to be read. Tests use false (indexed before returning).
    /// </param>
    public static AppServices Create(string settingsPath, string? libraryOverride = null, Func<AppSettings, AppSettings>? adjust = null, bool deferIndex = false)
    {
        SettingsLoadResult load = AppSettings.LoadWithStatus(settingsPath);
        AppSettings settings = load.Settings;
        if (adjust is not null)
        {
            settings = adjust(settings);
        }

        string root = libraryOverride ?? settings.EffectiveLibraryPath;
        CaptureLibrary library = deferIndex ? CaptureLibrary.OpenDeferred(root) : CaptureLibrary.Open(root);
        return new AppServices(settingsPath, settings, library, load);
    }

    /// <summary>
    /// Applies a settings change and saves it. Returns false (and raises <see cref="SettingsSaveFailed"/>) when the
    /// change could not be written: it stays in effect for this session only (FA-020). In read-only mode (a newer or
    /// unreadable settings file) nothing is written (FA-019).
    /// </summary>
    public bool UpdateSettings(Func<AppSettings, AppSettings> update)
    {
        bool saved;
        using (SettingsGate())
        {
            // RC3: SnapForge Capture and SnapForge Editor share settings.json. Re-read it first if the other program
            // changed it, so this change is applied on top of theirs instead of overwriting it.
            ReloadSettingsFromDisk(raiseChanged: false);
            Settings = update(Settings);
            saved = TrySaveSettings(Settings);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    private (DateTime Written, long Length) _settingsStamp = (DateTime.MinValue, -1);

    private static (DateTime, long) StampOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (DateTime.MinValue, -1);
        }
    }

    /// <summary>A named mutex per settings file, so the two SnapForge programs never interleave read-modify-write.</summary>
    private GateRelease SettingsGate()
    {
        string name = "Local\\SnapForge-Settings-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(SettingsPath).ToUpperInvariant())))[..32];
        var mutex = new Mutex(false, name);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
            {
                mutex.Dispose();
                return new GateRelease(null); // the other program is stuck: save anyway rather than lose the change
            }
        }
        catch (AbandonedMutexException)
        {
            // The other program ended while saving; the file itself is written atomically.
        }

        return new GateRelease(mutex);
    }

    private sealed class GateRelease(Mutex? mutex) : IDisposable
    {
        public void Dispose()
        {
            if (mutex is not null)
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }
    }

    /// <summary>
    /// Re-reads settings.json when another SnapForge program changed it since this one last read or wrote it. Returns
    /// true when the settings changed. Never reads in read-only mode (FA-019). When the Library location changed, the
    /// Library is reopened at the new location (<see cref="LibraryReplaced"/>).
    /// </summary>
    public bool ReloadSettingsFromDisk(bool raiseChanged = true)
    {
        if (SettingsReadOnly)
        {
            return false;
        }

        (DateTime, long) stamp = StampOf(SettingsPath);
        if (stamp == _settingsStamp || stamp.Item2 < 0)
        {
            return false;
        }

        SettingsLoadResult load = AppSettings.LoadWithStatus(SettingsPath);
        if (load.Status != SettingsLoadStatus.Loaded)
        {
            return false;
        }

        _settingsStamp = stamp;
        string oldRoot = Library.Root;
        Settings = load.Settings;
        string newRoot = Path.GetFullPath(Settings.EffectiveLibraryPath);
        if (!string.Equals(newRoot, oldRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(newRoot))
        {
            CaptureLibrary moved = CaptureLibrary.OpenDeferred(newRoot);
            Library = moved;
            Tray = RecentTray.Open(moved.Root, id => moved.IsIndexed ? moved.Find(id) is { IsDeleted: false } : moved.ExistsOnDisk(id));
            Thumbnails.Clear();
            _ = StartIndexingAsync();
            LibraryReplaced?.Invoke(this, EventArgs.Empty);
        }

        if (raiseChanged)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    private FileSystemWatcher? _settingsWatcher;
    private Timer? _settingsDebounce;

    /// <summary>
    /// Watches settings.json for changes made by the other SnapForge program and calls <paramref name="changed"/>
    /// (on a thread-pool thread, debounced) — the caller marshals to the UI thread and calls
    /// <see cref="ReloadSettingsFromDisk"/>.
    /// </summary>
    public void WatchSettings(Action changed)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(SettingsPath));
        if (dir is null || !Directory.Exists(dir))
        {
            return;
        }

        _settingsDebounce = new Timer(_ => changed(), null, Timeout.Infinite, Timeout.Infinite);
        _settingsWatcher = new FileSystemWatcher(dir, Path.GetFileName(SettingsPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        void Kick(object? sender, FileSystemEventArgs e) => _settingsDebounce?.Change(300, Timeout.Infinite);
        _settingsWatcher.Changed += Kick;
        _settingsWatcher.Created += Kick;
        _settingsWatcher.Renamed += (o, e) => Kick(o, e);
        _settingsWatcher.EnableRaisingEvents = true;
    }

    private bool TrySaveSettings(AppSettings settings)
    {
        if (SettingsReadOnly)
        {
            LastSettingsSaveError = "Settings are not saved in this session because the settings file was written by a newer SnapForge or could not be read; it is left unchanged.";
            return false;
        }

        SettingsSaveResult result = settings.TrySave(SettingsPath);
        if (result.Succeeded)
        {
            _settingsStamp = StampOf(SettingsPath); // our own write: not a change by the other program
            LastSettingsSaveError = null;
            return true;
        }

        LastSettingsSaveError = result.Error ?? "The settings file could not be written.";
        SettingsSaveFailed?.Invoke(this, LastSettingsSaveError);
        return false;
    }

    /// <summary>
    /// FA-006 / FA-007: two-phase Library relocation. (1) Copies EVERY file of the Library (captures, videos, damaged,
    /// interrupted recordings, unknown files) to <paramref name="newRoot"/> and verifies each byte-for-byte; (2) saves
    /// the new location durably — if that fails, SnapForge keeps using the old Library and nothing is deleted;
    /// (3) switches to the new Library; (4) only if <paramref name="confirmCleanup"/> agrees, deletes the source files
    /// that were verified copied and are unchanged (anything else is left in place and reported).
    /// The caller must make sure no recording is running.
    /// </summary>
    public async Task<LibraryRelocationOutcome> RelocateLibraryAsync(string newRoot, Func<RelocationResult, bool> confirmCleanup, IProgress<BackupProgress>? progress = null)
    {
        await Writer.WhenIdleAsync().ConfigureAwait(true);
        CaptureLibrary old = Library;
        RelocationResult copy = await Writer.RunAsync(() => old.CopyTo(newRoot, progress)).ConfigureAwait(true);
        if (string.Equals(copy.TargetRoot, old.Root, StringComparison.OrdinalIgnoreCase))
        {
            return new LibraryRelocationOutcome(copy, null, null);
        }

        if (!TrySaveSettings(Settings with { LibraryPath = copy.TargetRoot }))
        {
            // The pointer could not be saved: keep the old Library in use; the verified copy stays at the target.
            return new LibraryRelocationOutcome(copy, null, LastSettingsSaveError);
        }

        CaptureLibrary moved = await Task.Run(() => CaptureLibrary.Open(copy.TargetRoot)).ConfigureAwait(true);
        IReadOnlyList<TrayItem> items = Tray.Items;
        Settings = Settings with { LibraryPath = moved.Root };
        Library = moved;
        Tray = RecentTray.Open(moved.Root, id => moved.Find(id) is { IsDeleted: false });
        foreach (TrayItem item in items.Reverse())
        {
            Tray.Add(item.Id);
            if (item.Pinned)
            {
                Tray.SetPinned(item.Id, true);
            }
        }

        Thumbnails.Clear();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        LibraryReplaced?.Invoke(this, EventArgs.Empty);

        SourceCleanupResult? cleanup = null;
        if (confirmCleanup(copy))
        {
            cleanup = await Task.Run(() => old.DeleteSourceAfterVerifiedMove(copy)).ConfigureAwait(true);
        }

        return new LibraryRelocationOutcome(copy, cleanup, null);
    }

    public void Dispose()
    {
        _settingsWatcher?.Dispose();
        _settingsDebounce?.Dispose();
        Writer.Dispose();
    }

    /// <summary>
    /// Indexes the library on the background writer (later saves queue behind it, so nothing races the index),
    /// applies retention, then prunes the tray on the calling (UI) thread.
    /// </summary>
    public Task StartIndexingAsync()
    {
        if (Library.IsIndexed)
        {
            return Indexing;
        }

        CaptureLibrary library = Library;
        RetentionPolicy retention = Settings.Retention;
        Indexing = IndexAsync();
        return Indexing;

        async Task IndexAsync()
        {
            await Writer.RunAsync(() =>
            {
                library.CompleteOpen();
                library.ApplyRetention(retention, DateTime.UtcNow);
            }).ConfigureAwait(true);
            if (ReferenceEquals(library, Library))
            {
                Tray.Prune(id => Library.Find(id) is { IsDeleted: false });
            }
        }
    }

    public void ApplyRetention()
    {
        Library.ApplyRetention(Settings.Retention, DateTime.UtcNow);
        Tray.Prune(id => Library.Find(id) is { IsDeleted: false });
    }
}

/// <summary>Runs library writes one at a time on a background thread, so saves never overlap or block the UI.</summary>
internal sealed class LibraryWriter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<T> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RunAsync(Action work) => RunAsync<bool>(() =>
    {
        work();
        return true;
    });

    public async Task WhenIdleAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>Outcome of <see cref="AppServices.RelocateLibraryAsync"/>.</summary>
/// <param name="Copy">The verified copy (always made, unless an exception was thrown).</param>
/// <param name="Cleanup">What was deleted at the old location, or null if the user kept it.</param>
/// <param name="SettingsError">Non-null if the new location could not be saved: the old Library is still in use.</param>
internal sealed record LibraryRelocationOutcome(RelocationResult Copy, SourceCleanupResult? Cleanup, string? SettingsError);
