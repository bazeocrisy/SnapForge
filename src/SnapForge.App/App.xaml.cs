using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.App.Views;
using SnapForge.Core;
using SnapForge.Core.Settings;

namespace SnapForge.App;

#pragma warning disable CA1001 // The single-instance handles live for the whole process and are disposed in OnExit.
public partial class App : Application
#pragma warning restore CA1001
{
    private const string InstanceName = "SnapForge-SingleInstance-7c1d";
    private static ShellIntegration? _shell;
    private static CaptureWindow? _captureWindow;
    private static bool _notifiedRunning;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;

    internal static MainWindow? MainShell => Current?.MainWindow as MainWindow;

    internal static AppServices? Services => AppServices.Current;

    internal static bool HasNotificationIcon => _shell is not null;

    internal static IReadOnlyList<HotkeyRegistration> HotkeyResults { get; private set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 1 && e.Args[0] is "--self-test" or "--screenshots")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string output = e.Args.Length >= 2 ? e.Args[1] : Path.Combine(Environment.CurrentDirectory, e.Args[0] == "--self-test" ? "snapforge-self-test.txt" : "snapforge-screenshots");
            int code = e.Args[0] == "--self-test" ? SelfTest.Run(output) : ScreenshotTour.Run(output);
            Shutdown(code);
            return;
        }

        // Single instance: a second launch brings the running SnapForge to the front instead.
        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\{InstanceName}", out bool first);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceName}-activate");
        if (!first)
        {
            _activateSignal.Set();
            Shutdown(0);
            return;
        }

        ThreadPool.RegisterWaitForSingleObject(_activateSignal, (_, _) => Dispatcher.BeginInvoke(() => MainShell?.BringToFront()), null, Timeout.Infinite, executeOnlyOnce: false);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppServices services;
        try
        {
            services = AppServices.Create(AppSettings.DefaultPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"The Capture Library could not be opened:\n\n{ex.Message}\n\nSnapForge will use the default location instead.", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            services = AppServices.Create(AppSettings.DefaultPath(), adjust: s => s with { LibraryPath = null });
        }

        AppServices.Current = services;
        ThemeManager.Apply(services.Settings.Theme);
        services.ApplyRetention();

        var window = new MainWindow(services);
        MainWindow = window;
        window.RestoreToolSettings();
        window.Show();

        _shell = new ShellIntegration();
        _shell.ShowIcon(ProductInfo.Name);
        _shell.IconActivated += (_, _) => MainShell?.BringToFront();
        _shell.IconContextMenuRequested += (_, _) => ShowNotificationMenu();
        services.SettingsChanged += (_, _) => RegisterHotkeys(notifyOnFailure: false);
        RegisterHotkeys(notifyOnFailure: true);

        if (services.Settings.ShowCaptureWindowOnStart)
        {
            ShowCaptureWindow();
        }

        if (!services.Settings.PrivacyNoticeShown)
        {
            window.Dispatcher.BeginInvoke(() =>
            {
                ShowPrivacyNotice(window);
                services.UpdateSettings(s => s with { PrivacyNoticeShown = true });
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();
        _shell = null;
        AppServices.Current?.Dispose();
        _activateSignal?.Dispose();
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>Runs a capture with the given options and hands the result to the Editor / Library / clipboard.</summary>
    internal static async Task CaptureWithOptionsAsync(CaptureOptions options)
    {
        MainWindow? main = MainShell;
        if (main is null || CaptureController.IsBusy)
        {
            return;
        }

        bool mainWasVisible = main.IsVisible;
        CaptureResult? result;
        try
        {
            result = await CaptureController.CaptureAsync(options.Normalized()).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Any capture failure must be reported, not crash the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessageBox.Show($"The screen could not be captured.\n\n{ex.Message}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (result is null)
        {
            main.SetStatus("Capture cancelled.");
            return;
        }

        main.AcceptCapture(result.Image, result.CapturedAt, options);
        bool preview = options.PreviewInEditor || (!options.SaveToLibrary && !options.CopyToClipboard);
        if (!preview)
        {
            if (!mainWasVisible)
            {
                main.Hide();
            }

            NotifyBalloon(ProductInfo.Name, options switch
            {
                { SaveToLibrary: true, CopyToClipboard: true } => "Capture saved to the Library and copied to the clipboard.",
                { SaveToLibrary: true } => "Capture saved to the Library.",
                _ => "Capture copied to the clipboard (not saved to the Library).",
            });
        }
    }

    internal static void ShowCaptureWindow()
    {
        if (AppServices.Current is not { } services)
        {
            return;
        }

        if (_captureWindow is null || !_captureWindow.IsLoaded)
        {
            _captureWindow = new CaptureWindow(services);
        }

        _captureWindow.Show();
        _captureWindow.Activate();
    }

    internal static void ShowSettings(Window? owner)
    {
        if (AppServices.Current is not { } services)
        {
            return;
        }

        var settings = new SettingsWindow(services) { Owner = owner is { IsVisible: true } ? owner : null };
        settings.ShowDialog();
    }

    internal static async Task ExitAsync()
    {
        if (MainShell is { } main && !await main.CloseForRealAsync().ConfigureAwait(true))
        {
            return;
        }

        _captureWindow?.Close();
        Current.Shutdown();
    }

    internal static void NotifyOnce(string title, string text)
    {
        if (!_notifiedRunning)
        {
            _notifiedRunning = true;
            NotifyBalloon(title, text);
        }
    }

    internal static void NotifyBalloon(string title, string text, bool warning = false) => _shell?.ShowBalloon(title, text, warning);

    internal static void ShowPrivacyNotice(Window? owner)
    {
        string root = AppServices.Current?.Library.Root ?? "(not opened)";
        string text =
            "Where your captures are stored\n\n" +
            $"SnapForge saves every capture automatically to your local Capture Library:\n{root}\n\n" +
            "• Nothing is uploaded or synchronized by SnapForge. It has no network features. (If you move the Library into a OneDrive/Dropbox folder, that service will sync it.)\n" +
            "• To capture without keeping a copy, turn off \"Save to Library\" in the Capture window, or use the preset \"Full screen to Clipboard (no history)\".\n" +
            "• Redaction: exported images (PNG/JPEG, clipboard) contain only the black box. The editable Library copy keeps the original pixels so you can undo; use File → Sanitize to remove them permanently.\n" +
            "• Deleted captures go to the Library's Trash first. Backups you make, exported files and Windows file-recovery or backup tools can keep copies that SnapForge cannot remove.\n\n" +
            "You can change the location, retention and backups in Settings → Library.";
        if (owner is { IsVisible: true })
        {
            MessageBox.Show(owner, text, $"{ProductInfo.Name} — privacy and storage", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(text, $"{ProductInfo.Name} — privacy and storage", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Registers the capture hotkey and preset hotkeys; reports conflicts.</summary>
    internal static void RegisterHotkeys(bool notifyOnFailure)
    {
        if (_shell is null || AppServices.Current is not { } services)
        {
            return;
        }

        AppSettings s = services.Settings;
        var list = new List<(string, HotkeyGesture?, Action)>
        {
            ("Capture", s.CaptureHotkey, () => _ = CaptureWithOptionsAsync(AppServices.Current!.Settings.Capture)),
        };
        foreach (CapturePreset preset in s.Presets)
        {
            CaptureOptions options = preset.Options;
            list.Add(($"Preset: {preset.Name}", preset.Hotkey, () => _ = CaptureWithOptionsAsync(options)));
        }

        HotkeyResults = _shell.RegisterHotkeys(list);
        List<HotkeyRegistration> failed = [.. HotkeyResults.Where(r => !r.Succeeded)];
        if (failed.Count > 0)
        {
            string message = string.Join("\n", failed.Select(f => $"{f.Gesture} ({f.Action}): {f.Error}"));
            MainShell?.SetStatus($"Hotkey not available: {failed[0].Gesture} — {failed[0].Error} Change it in Settings → Hotkeys.");
            if (notifyOnFailure)
            {
                NotifyBalloon("SnapForge hotkey not available", message + "\nChange it in Settings → Hotkeys.", warning: true);
            }
        }
    }

    private static void ShowNotificationMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, bool bold = false)
        {
            var item = new MenuItem { Header = header, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("_Capture", () => _ = CaptureWithOptionsAsync(AppServices.Current!.Settings.Capture), bold: true);
        Add("Capture _window…", ShowCaptureWindow);
        menu.Items.Add(new Separator());
        Add("Open _Editor", () => { MainShell?.BringToFront(); MainShell?.ShowEditorWorkspace(); });
        Add("Open _Library", () => { MainShell?.BringToFront(); MainShell?.ShowLibraryWorkspace(); });
        Add("_Settings…", () => ShowSettings(null));
        menu.Items.Add(new Separator());
        Add("E_xit SnapForge", () => _ = ExitAsync());
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string? logPath = TryWriteCrashLog(e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}" + (logPath is null ? string.Empty : $"\n\nDetails were saved to:\n{logPath}"),
            ProductInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keep running: open captures are still in memory and already-saved work is in the Library.
        e.Handled = true;
    }

    /// <summary>Local-only diagnostics (exception text and OS version; never image data). Nothing is sent anywhere.</summary>
    private static string? TryWriteCrashLog(Exception exception)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"error-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, $"{DateTime.Now:O}\n{Environment.OSVersion}\n{exception}");
            return path;
        }
#pragma warning disable CA1031 // Logging must never throw from the crash handler.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
