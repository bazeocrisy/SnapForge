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
    private AppServices(string settingsPath, AppSettings settings, CaptureLibrary library)
    {
        SettingsPath = settingsPath;
        Settings = settings;
        Library = library;
        Tray = RecentTray.Open(library.Root, id => library.Find(id) is { IsDeleted: false });
    }

    public static AppServices? Current { get; set; }

    public event EventHandler? SettingsChanged;

    /// <summary>Raised after the library moved to a new location (Library and Tray are new instances).</summary>
    public event EventHandler? LibraryReplaced;

    public string SettingsPath { get; }

    public AppSettings Settings { get; private set; }

    public CaptureLibrary Library { get; private set; }

    public RecentTray Tray { get; private set; }

    public LibraryWriter Writer { get; } = new();

    public ThumbnailCache Thumbnails { get; } = new();

    /// <summary>Opens settings and the library. <paramref name="libraryOverride"/> is used by tests.</summary>
    public static AppServices Create(string settingsPath, string? libraryOverride = null, Func<AppSettings, AppSettings>? adjust = null)
    {
        AppSettings settings = AppSettings.Load(settingsPath);
        if (adjust is not null)
        {
            settings = adjust(settings);
        }

        string root = libraryOverride ?? settings.EffectiveLibraryPath;
        CaptureLibrary library = CaptureLibrary.Open(root);
        return new AppServices(settingsPath, settings, library);
    }

    public void UpdateSettings(Func<AppSettings, AppSettings> update)
    {
        Settings = update(Settings);
        try
        {
            Settings.Save(SettingsPath);
        }
        catch (IOException)
        {
            // Settings stay in effect for this session even if they cannot be written.
        }
        catch (UnauthorizedAccessException)
        {
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves every capture to <paramref name="newRoot"/> (verified copy, then secure delete of the old copy).</summary>
    public async Task MoveLibraryAsync(string newRoot)
    {
        await Writer.WhenIdleAsync().ConfigureAwait(true);
        CaptureLibrary moved = await Task.Run(() => Library.MoveTo(newRoot)).ConfigureAwait(true);
        string? trayFile = Path.Combine(Library.Root, "tray.json");
        IReadOnlyList<TrayItem> items = Tray.Items;
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

        if (File.Exists(trayFile))
        {
            File.Delete(trayFile);
        }

        Thumbnails.Clear();
        UpdateSettings(s => s with { LibraryPath = moved.Root });
        LibraryReplaced?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Writer.Dispose();

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
