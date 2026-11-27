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
    private static CaptureWidget? _widget;
    private static bool _notifiedRunning;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;
    private EventWaitHandle? _exitSignal;
    private System.IO.MemoryMappedFiles.MemoryMappedFile? _instanceInfo;

    internal static MainWindow? MainShell => Current?.MainWindow as MainWindow;

    internal static AppServices? Services => AppServices.Current;

    internal static bool HasNotificationIcon => _shell is not null;

    internal static IReadOnlyList<HotkeyRegistration> HotkeyResults { get; private set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0] is "--video-selftest" or "--video-record-until-killed" or "--video-recover" or "--video-benchmark")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(Video.VideoSelfTest.Run(e.Args));
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--feature-selftest")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(FeatureSelfTest.Run(e.Args[1]));
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--drop-target")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DropTargetMode.Run(this, e.Args[1]);
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--video-probe")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(Video.VideoProbe.Run(e.Args[1]));
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0] == "--benchmark-startup")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = StartupBenchmark.RunAsync(e.Args[1], e.Args[2]).ContinueWith(t => Dispatcher.Invoke(() => Shutdown(t.Result)), TaskScheduler.Default);
            return;
        }

        if (e.Args.Length >= 1 && e.Args[0] is "--self-test" or "--screenshots")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string output = e.Args.Length >= 2 ? e.Args[1] : Path.Combine(Environment.CurrentDirectory, e.Args[0] == "--self-test" ? "snapforge-self-test.txt" : "snapforge-screenshots");
            int code = e.Args[0] == "--self-test" ? SelfTest.Run(output) : ScreenshotTour.Run(output);
            Shutdown(code);
            return;
        }

        // Single instance: a second launch of the SAME build brings the running SnapForge to the front. A different
        // build (e.g. a newer download while an older copy runs in the notification area) is reported instead of
        // silently handing over to the old copy, and can replace it.
        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\{InstanceName}", out bool first);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceName}-activate");
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceName}-exit");
        if (!first && !TakeOverFromRunningInstance())
        {
            Shutdown(0);
            return;
        }

        _instanceInfo = InstanceInfo.Publish($@"Local\{InstanceName}-info", InstanceInfo.Current());
        ThreadPool.RegisterWaitForSingleObject(_activateSignal, (_, _) => Dispatcher.BeginInvoke(() => MainShell?.BringToFront()), null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(() => _ = ExitAsync()), null, Timeout.Infinite, executeOnlyOnce: true);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppServices services;
        try
        {
            services = AppServices.Create(AppSettings.DefaultPath(), deferIndex: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"The Capture Library could not be opened:\n\n{ex.Message}\n\nSnapForge will use the default location instead.", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            services = AppServices.Create(AppSettings.DefaultPath(), adjust: s => s with { LibraryPath = null }, deferIndex: true);
        }

        AppServices.Current = services;
        ThemeManager.Apply(services.Settings.Theme);

        // Leftover drag-and-drop copies from a previous session (e.g. after a crash) are removed in the background.
        _ = Task.Run(CaptureDragDrop.Sweep);

        var window = new MainWindow(services);
        MainWindow = window;
        window.RestoreToolSettings();
        bool background = e.Args.Contains(StartupRegistration.BackgroundArgument);
        if (!background)
        {
            window.Show();
        }

        // The library is indexed after the window is up, so start-up time does not grow with the library size.
        _ = services.StartIndexingAsync().ContinueWith(_ => RecoverInterruptedRecordings(services), TaskScheduler.Default);

        _shell = new ShellIntegration();
        _shell.ShowIcon(ProductInfo.Name);
        // Capture-first: clicking the notification-area icon opens the capture panel (the Editor is in its menu).
        _shell.IconActivated += (_, _) => ShowCaptureWindow();
        _shell.IconContextMenuRequested += (_, _) => ShowNotificationMenu();
        services.SettingsChanged += (_, _) => RegisterHotkeys(notifyOnFailure: false);
        RegisterHotkeys(notifyOnFailure: true);
        services.SettingsChanged += (_, _) => Current.Dispatcher.BeginInvoke(() => ApplyWidget(services));
        ApplyWidget(services);
        Video.RecordingController.ActiveChanged += (_, _) => _widget?.UpdateRecordButton();
        if (_widget is not null && !services.Settings.WidgetIntroShown)
        {
            // First start with the widget: open it briefly so the user sees where it lives.
            _widget.Dispatcher.BeginInvoke(() => _widget?.Peek(TimeSpan.FromSeconds(6)), DispatcherPriority.ApplicationIdle);
            services.UpdateSettings(s => s with { WidgetIntroShown = true });
        }

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

    /// <summary>
    /// Another SnapForge holds the single-instance mutex. Same build: hand over (bring it to the front) and return
    /// false. Different or unknown build: explain and, if the user agrees, ask it to exit and take over (true).
    /// </summary>
    private bool TakeOverFromRunningInstance()
    {
        InstanceInfo? running = InstanceInfo.Read($@"Local\{InstanceName}-info");
        if (running is not null && running.Build == InstanceInfo.CurrentBuild)
        {
            _activateSignal!.Set();
            return false;
        }

        string runningText = running is null
            ? "an older SnapForge build (from before milestone 3)"
            : $"SnapForge build {InstanceInfo.ShortBuild(running.Build)}\n{running.ExePath}";
        string thisText = $"build {InstanceInfo.ShortBuild(InstanceInfo.CurrentBuild)}\n{Environment.ProcessPath}";
        if (running is null)
        {
            // Builds before milestone 3 cannot be asked to exit; tell the user how.
            MessageBox.Show(
                $"Another copy of SnapForge is already running: {runningText}.\n\nThis copy is {thisText}.\n\n" +
                "Only one SnapForge can run at a time. Right-click the SnapForge icon in the notification area (next to the clock) " +
                "→ Exit SnapForge, then start this copy again.",
                ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            _activateSignal!.Set();
            return false;
        }

        MessageBoxResult answer = MessageBox.Show(
            $"Another copy of SnapForge is already running:\n{runningText}\n\nThis copy is {thisText}.\n\n" +
            "Close the running copy and start this one? (Its captures are saved first; your Library is shared by both.)",
            ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes)
        {
            _activateSignal!.Set();
            return false;
        }

        _exitSignal!.Set();
        try
        {
            if (_instanceMutex!.WaitOne(TimeSpan.FromSeconds(20)))
            {
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            return true; // the other copy ended without releasing it; we own it now
        }

        MessageBox.Show("The running copy did not close (it may be waiting for an answer in one of its windows). Close it from the notification area and try again.",
            ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Video.RecordingController.StopForExit();
        _widget?.Close();
        _widget = null;
        try
        {
            // Drag copies are kept across exit (a receiving app may still read them); apply retention only.
            CaptureDragDrop.Store.Enforce(DateTime.UtcNow, Core.Export.TempExportStore.MaxTotalBytes);
        }
        catch (IOException)
        {
        }

        _shell?.Dispose();
        _shell = null;
        AppServices.Current?.Dispose();
        _activateSignal?.Dispose();
        _exitSignal?.Dispose();
        _instanceInfo?.Dispose();
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
    /// <summary>Captures the same screen rectangle as the last capture, with the same options (no selection step).</summary>
    internal static Task RepeatLastCaptureAsync()
    {
        if (CaptureController.LastSelection is not { } region || CaptureController.LastOptions is not { } options)
        {
            MainShell?.SetStatus("Nothing to repeat yet — take a capture first.");
            return Task.CompletedTask;
        }

        return CaptureWithOptionsAsync(options with { DelaySeconds = 0 }, region);
    }

    internal static async Task CaptureWithOptionsAsync(CaptureOptions options, Core.Geometry.PixelRect? fixedRegion = null)
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
            result = await CaptureController.CaptureAsync(options.Normalized(), fixedRegion).ConfigureAwait(true);
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

    /// <summary>The desktop capture widget, if enabled.</summary>
    internal static CaptureWidget? Widget => _widget;

    /// <summary>Shows, hides and updates the desktop widget to match the settings.</summary>
    internal static void ApplyWidget(AppServices services)
    {
        if (services.Settings.Widget.Enabled)
        {
            if (_widget is null)
            {
                _widget = new CaptureWidget(services);
                _widget.Show();
            }

            _widget.ApplySettings();
        }
        else if (_widget is not null)
        {
            _widget.Close();
            _widget = null;
        }
    }

    /// <summary>
    /// View → Show capture widget: turns the widget on, moves it to the top centre of the display the main window
    /// is on, and opens it for a few seconds so it is easy to find.
    /// </summary>
    internal static void ShowWidget()
    {
        if (AppServices.Current is not { } services)
        {
            return;
        }

        string? device = null;
        if (MainShell is { IsVisible: true } main && Interop.PhysicalPlacement.GetBounds(main) is { } b)
        {
            device = Interop.PhysicalPlacement.MonitorFor(Interop.PhysicalPlacement.Layout(), b).DeviceName;
        }

        services.UpdateSettings(s => s with
        {
            Widget = s.Widget with
            {
                Enabled = true,
                Placement = device is null ? null : new WidgetPlacement(device, DockEdge.Top, 0.5),
            },
        });
        ApplyWidget(services);
        _widget?.Peek(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Start-up recovery of recordings interrupted by a crash, forced close or power loss (VID-R04). Runs after the
    /// Library is indexed, on the library writer; results are reported in a notification.
    /// </summary>
    private static async Task RecoverInterruptedRecordings(AppServices services)
    {
        if (services.Library.InterruptedRecordings().Count == 0)
        {
            return;
        }

        _ = Video.Interop.Mf.MFStartup(Video.Interop.Mf.MF_VERSION, 0);
        IReadOnlyList<Video.RecoveryOutcome> outcomes = await services.Writer.RunAsync(() => Video.RecordingRecovery.RecoverAll(services.Library)).ConfigureAwait(false);
        if (outcomes.Count > 0)
        {
            string text = string.Join("\n", outcomes.Select(o => o.Message));
            await Current.Dispatcher.BeginInvoke(() => NotifyBalloon("SnapForge: interrupted recording", text, warning: outcomes.Any(o => !o.Recovered)));
        }
    }

    /// <summary>Opens a recording from the Library in the player (one window per video).</summary>
    internal static void ShowVideo(Guid id)
    {
        if (AppServices.Current is not { } services || services.Library.Find(id) is not { IsVideo: true } record)
        {
            return;
        }

        if (record.Damage is { } damage)
        {
            MessageBox.Show($"This recording cannot be played: {damage}", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Video.VideoPlayerWindow? open = Current.Windows.OfType<Video.VideoPlayerWindow>().FirstOrDefault(w => w.Record.Id == id);
        if (open is not null)
        {
            open.Activate();
            return;
        }

        new Video.VideoPlayerWindow(record, services.Library.VideoPath(id)).Show();
        services.Library.MarkOpened(id, DateTime.UtcNow);
    }

    internal static void ShowCaptureWindow()
    {
        if (AppServices.Current is not { } services)
        {
            return;
        }

        if (_captureWindow is null || !_captureWindow.IsLoaded || !ReferenceEquals(_captureWindow.Services, services))
        {
            _captureWindow?.Close();
            _captureWindow = new CaptureWindow(services);
        }

        _captureWindow.Topmost = services.Settings.CapturePanelTopmost;
        if (_captureWindow.WindowState == WindowState.Minimized)
        {
            _captureWindow.WindowState = WindowState.Normal;
        }

        _captureWindow.Show();
        _captureWindow.Activate();
    }

    /// <summary>The capture panel window, if it has been created (tests).</summary>
    internal static CaptureWindow? CapturePanel => _captureWindow;

    /// <summary>Show/hide capture panel hotkey: hides the panel if it is visible, otherwise shows it.</summary>
    internal static void ToggleCaptureWindow()
    {
        if (_captureWindow is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            _captureWindow.Close(); // closing only hides it while SnapForge runs in the notification area
        }
        else
        {
            ShowCaptureWindow();
        }
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
        list.Add(("Show capture panel", s.ShowPanelHotkey, ToggleCaptureWindow));
        list.Add(("Record / pause / resume", s.Video.RecordHotkey, Video.RecordingController.RecordOrPause));
        list.Add(("Stop recording", s.Video.StopHotkey, () => _ = Video.RecordingController.StopAsync()));
        list.Add(("Cancel recording", s.Video.CancelHotkey, Video.RecordingController.Cancel));
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
        Add("Capture _panel…", ShowCaptureWindow);
        Add("Show capture _widget", ShowWidget);
        if (AppServices.Current?.Settings.Widget.Enabled == true)
        {
            Add("_Hide capture widget", () => AppServices.Current?.UpdateSettings(s => s with { Widget = s.Widget with { Enabled = false } }));
        }
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
