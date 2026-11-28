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
    /// <summary>Single-instance key: one SnapForge per Windows session and data profile.</summary>
    // Published by the running copy and read by a second launch: one name for both (a mismatch made every second
    // launch of a --profile copy look like a pre-milestone-3 build and wait on a message box; CI run 111).
    private static string InstanceInfoName => $@"Local\{InstanceKey}-info";

    private static string InstanceKey => Handoff.InstanceKey(Role);

    /// <summary>Removes <c>--profile &lt;name&gt;</c> from the arguments and selects that (or SNAPFORGE_PROFILE's) profile.</summary>
    internal static string[] ApplyProfile(string[] raw)
    {
        var rest = new List<string>(raw.Length);
        string? profile = Environment.GetEnvironmentVariable(ProductInfo.ProfileVariable);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == "--profile")
            {
                profile = i + 1 < raw.Length ? raw[++i] : throw new ArgumentException("--profile needs a name.");
                continue;
            }

            rest.Add(raw[i]);
        }

        ProductInfo.UseProfile(string.IsNullOrWhiteSpace(profile) ? null : profile.Trim());
        return [.. rest];
    }
    private static ShellIntegration? _shell;
    private static CaptureWindow? _captureWindow;
    private static CaptureWidget? _widget;
    private static bool _notifiedRunning;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;
    private EventWaitHandle? _exitSignal;
    private EventWaitHandle? _setupExitSignal;
    private static bool _exitInProgress;
    private System.IO.MemoryMappedFiles.MemoryMappedFile? _instanceInfo;

    internal static MainWindow? MainShell => Current?.MainWindow as MainWindow;

    internal static AppServices? Services => AppServices.Current;

    internal static bool HasNotificationIcon => _shell is not null;

    internal static IReadOnlyList<HotkeyRegistration> HotkeyResults { get; private set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Isolated data profile (--profile <name> or SNAPFORGE_PROFILE): separate settings, Library, logs, temp files
        // and single-instance key; no global hotkeys and no Windows start-up entry. Used for testing.
        string[] args;
        try
        {
            args = ApplyProfile(e.Args);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(2);
            return;
        }

        if (args.Length >= 2 && args[0] is "--video-selftest" or "--video-record-until-killed" or "--video-recover" or "--video-benchmark" or "--video-quality")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(Video.VideoSelfTest.Run(args));
            return;
        }

        if (args.Length >= 2 && args[0] == "--feature-selftest")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(FeatureSelfTest.Run(args[1]));
            return;
        }

        if (args.Length >= 2 && args[0] == "--drop-target")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DropTargetMode.Run(this, args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--video-probe")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(Video.VideoProbe.Run(args[1]));
            return;
        }

        if (args.Length >= 3 && args[0] == "--benchmark-startup")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = StartupBenchmark.RunAsync(args[1], args[2]).ContinueWith(t => Dispatcher.Invoke(() => Shutdown(t.Result)), TaskScheduler.Default);
            return;
        }

        if (args.Length >= 2 && args[0] == "--library-check")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(LibraryCheck.Run(args));
            return;
        }

        if (args.Length >= 1 && args[0] == "--migrate-per-user-install")
        {
            // Headless migration from the rc.1 per-user installation (CI and administrators): --yes skips the question.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(MigrateLegacyInstall(interactive: !args.Contains("--yes"), report: true));
            return;
        }

        if (args.Length >= 2 && args[0] == "--version-info")
        {
            // RC4: which program, version and build this executable is (CI checks both installed programs).
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            File.WriteAllText(args[1], $"Program: {ProgramName}\nVersion: {InstanceInfo.ProductVersion}\nBuild: {InstanceInfo.BuildNumber}\nCommit: {InstanceInfo.Commit}\nAbout: {AboutHeadline}\n");
            Shutdown(0);
            return;
        }

        if (args.Length >= 2 && args[0] == "--ui-audit")
        {
            // QA (RC5): every window, dialog, menu, tool and settings section at the CURRENT display scaling.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string label = args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : "current";
            Shutdown(UiAudit.Run(args[1], label, strict: args.Contains("--strict")));
            return;
        }

        if (args.Length >= 1 && args[0] is "--self-test" or "--screenshots")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string output = args.Length >= 2 ? args[1] : Path.Combine(Environment.CurrentDirectory, args[0] == "--self-test" ? "snapforge-self-test.txt" : "snapforge-screenshots");
            int code = args[0] == "--self-test" ? SelfTest.Run(output) : ScreenshotTour.Run(output);
            Shutdown(code);
            return;
        }

        // Single instance: a second launch of the SAME build brings the running SnapForge to the front. A different
        // build (e.g. a newer download while an older copy runs in the notification area) is reported instead of
        // silently handing over to the old copy, and can replace it.
        // FA-003: keep every window's title bar and frame on screen. Installed for the interactive app only — test
        // helper modes above (drop target, self-tests) place their windows deliberately.
        Interop.WorkAreaGuard.Install();
        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\{InstanceKey}", out bool first);
        Interop.RunningMarker.Hold(); // lets the system-wide (un)installer see SnapForge running in any session
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceKey}-activate");
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceKey}-exit");
        if (!first && Role == AppRole.Editor)
        {
            // A second launch of SnapForge Editor: leave the request (or "show yourself") for the running Editor.
            Handoff.Write(AppRole.Editor, Handoff.FromArguments(args) ?? new HandoffRequest(Handoff.ShowEditor));
            Handoff.Signal(AppRole.Editor);
            Shutdown(0);
            return;
        }

        if (!first && !TakeOverFromRunningInstance())
        {
            Shutdown(0);
            return;
        }

        _instanceInfo = InstanceInfo.Publish(InstanceInfoName, InstanceInfo.Current());
        ThreadPool.RegisterWaitForSingleObject(_activateSignal, (_, _) => Dispatcher.BeginInvoke(OnActivated), null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(() => _ = ExitAsync()), null, Timeout.Infinite, executeOnlyOnce: false);
        // RC4: SnapForge Setup asks every SnapForge in this Windows session (both programs, any data profile) to close
        // before it replaces the program files. Manual-reset: Setup sets it while it waits and resets it afterwards.
        _setupExitSignal = new EventWaitHandle(false, EventResetMode.ManualReset, SetupExitEventName);
        WatchSetupExitRequest();
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
        services.WatchOtherPrograms = true;
        bool startedHidden = args.Contains(StartupRegistration.BackgroundArgument);
        ReportSettingsLoad(services, startedHidden);
        services.SettingsSaveFailed += (_, message) => Current.Dispatcher.BeginInvoke(() => ReportSettingsSaveFailure(message));
        ThemeManager.Apply(services.Settings.Theme);
        // RC3: the other SnapForge program may change settings.json; pick that up (hotkeys, QuickDock, theme…).
        services.WatchSettings(() => Dispatcher.BeginInvoke(() => services.ReloadSettingsFromDisk()));

        // Leftover drag-and-drop copies from a previous session (e.g. after a crash) are removed in the background.
        _ = Task.Run(CaptureDragDrop.Sweep);

        if (Role == AppRole.Editor)
        {
            // SnapForge Editor: its window only — no notification-area icon, hotkeys, QuickDock or recording.
            StartEditor(services, args);
            return;
        }

        if (args.Contains("--test-record") && ProductInfo.Profile is not null)
        {
            // CI only (requires an isolated test profile): start a screen-only recording of the primary display
            // without the selection step, so Setup's handling of an ACTIVE recording can be tested.
            Dispatcher.BeginInvoke(() => _ = Video.RecordingController.StartForTestAsync(), DispatcherPriority.ApplicationIdle);
        }

        // SnapForge Capture (RC3): no Editor window in this process — captures are handed to SnapForge Editor.
        // FA-001 / SF-003: Windows startup (--background) shows the notification-area icon and, if enabled, the
        // QuickDock only. A normal launch shows ONE compact window: the capture panel, or on the very first launch
        // the welcome window instead.
        bool background = args.Contains(StartupRegistration.BackgroundArgument);
        StartedInBackground = background;

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
        if (background)
        {
            // Started with Windows: notification-area icon and the enabled dock only — no window, welcome or modal.
            return;
        }

        MigrateLegacyInstall(interactive: true, report: false);

        if (_widget is not null && !services.Settings.WidgetIntroShown)
        {
            // First deliberate start with the dock: open it briefly so the user sees where it lives.
            _widget.Dispatcher.BeginInvoke(() => _widget?.Peek(TimeSpan.FromSeconds(6)), DispatcherPriority.ApplicationIdle);
            services.UpdateSettings(s => s with { WidgetIntroShown = true });
        }

        if (!services.Settings.PrivacyNoticeShown)
        {
            // First launch: the welcome window is the only window; it offers to open the capture panel.
            Dispatcher.BeginInvoke(() =>
            {
                services.UpdateSettings(s => s with { PrivacyNoticeShown = true });
                ShowWelcome(null, afterClose: ShowPrimaryWindow);
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        ShowPrimaryWindow();
    }

    /// <summary>
    /// FA-002: if this copy is installed for all users and the signed-in user still has the older per-user copy
    /// (1.0.0-rc.1), offer once to remove that old copy (captures and settings are kept). Returns a process exit code
    /// for the headless mode: 0 = nothing to do or migrated, 1 = declined, 2 = failed.
    /// </summary>
    internal static int MigrateLegacyInstall(bool interactive, bool report)
    {
        string? exe = Environment.ProcessPath;
        LegacyInstallMigration.LegacyInstall? old = exe is null ? null : LegacyInstallMigration.Find(exe);
        bool installed = exe is not null && InstanceInfo.InstallationKind(exe).StartsWith("installed for all users", StringComparison.Ordinal);
        if (old is null || !installed || (interactive && File.Exists(LegacyInstallMigration.DeclinedMarker)))
        {
            if (report)
            {
                Console.WriteLine(old is null ? "MIGRATION: no older per-user installation for this user" : "MIGRATION: this copy is not the system-wide installation; nothing done");
            }

            return 0;
        }

        if (interactive)
        {
            MessageBoxResult answer = MessageBox.Show(
                $"SnapForge is now installed for all users in Program Files.\n\nAn older copy installed only for you (version {old.Version ?? "unknown"}) is still in:\n{old.Location}\n\n" +
                "Remove the older copy now? Your captures, recordings and settings are kept — only the old program files and its Start menu entry are removed.",
                ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes)
            {
                try
                {
                    Directory.CreateDirectory(ProductInfo.DataRoot);
                    File.WriteAllText(LegacyInstallMigration.DeclinedMarker, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                return 1;
            }
        }

        string? error = LegacyInstallMigration.Remove(old, exe!);
        if (report)
        {
            Console.WriteLine(error is null ? $"MIGRATION: removed the per-user copy in {old.Location}; data kept" : $"MIGRATION FAILED: {error}");
        }
        else if (error is not null)
        {
            MessageBox.Show($"The older copy could not be removed:\n{error}\n\nYou can remove it in Settings → Apps → Installed apps (choose the SnapForge entry installed for you only). Your captures are not affected.",
                ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return error is null ? 0 : 2;
    }

    /// <summary>FA-019: tell the user when settings were damaged, written by a newer version or unreadable.</summary>
    private static void ReportSettingsLoad(AppServices services, bool background)
    {
        if (services.SettingsLoad is not { } load || load.Status is SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing)
        {
            return;
        }

        string text = load.Status switch
        {
            SettingsLoadStatus.NewerVersion => "Your settings were saved by a newer version of SnapForge. This version uses default settings for now and will NOT change that file, so the newer version keeps your settings.",
            SettingsLoadStatus.Damaged => $"Your settings file was damaged, so default settings are used. A copy of the damaged file was kept:\n{load.DamagedCopyPath}",
            _ => $"Your settings file could not be read ({load.Message}). Default settings are used and the file is left unchanged.",
        };
        if (background)
        {
            NotifyBalloon(ProductInfo.Name, text, warning: true);
        }
        else
        {
            MessageBox.Show(text + "\n\nYour captures are not affected.", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool _settingsFailureShown;

    /// <summary>FA-020: settings that could not be saved are reported once per session (they stay in effect until exit).</summary>
    private static void ReportSettingsSaveFailure(string message)
    {
        MainShell?.SetStatus($"Settings could not be saved: {message}");
        if (_settingsFailureShown)
        {
            return;
        }

        _settingsFailureShown = true;
        MessageBox.Show($"Your SnapForge settings could not be saved:\n{message}\n\nThe change applies until you exit SnapForge. Check that there is free disk space and that you can write to:\n{AppSettings.DefaultPath()}",
            ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>True when this process was started by Windows at sign-in (tests and diagnostics).</summary>
    internal static bool StartedInBackground { get; private set; }

    /// <summary>
    /// Opens the one window a deliberate launch shows: the capture panel by default, or the Editor when the user
    /// turned "Open the capture panel when SnapForge starts" off. Used at start-up and when a second launch of the
    /// same build hands over to this one.
    /// </summary>
    internal static void ShowPrimaryWindow()
    {
        if (AppServices.Current is { } services && !services.Settings.ShowCaptureWindowOnStart)
        {
            OpenInEditor(new HandoffRequest(Handoff.ShowEditor));
            return;
        }

        ShowCaptureWindow();
    }

    /// <summary>
    /// Another SnapForge holds the single-instance mutex. Same build: hand over (bring it to the front) and return
    /// false. Different or unknown build: explain and, if the user agrees, ask it to exit and take over (true).
    /// </summary>
    private bool TakeOverFromRunningInstance()
    {
        InstanceInfo? running = InstanceInfo.Read(InstanceInfoName);
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
        _setupExitSignal?.Dispose();
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

        if (DeleteDataOnExit)
        {
            DeleteDataRootNow();
        }

        base.OnExit(e);
    }

    /// <summary>Set by Settings → Delete my SnapForge data: the data folder is deleted after everything is closed.</summary>
    internal static bool DeleteDataOnExit { get; set; }

    private static void DeleteDataRootNow()
    {
        string root = ProductInfo.DataRoot;
        for (int attempt = 0; attempt < 5 && Directory.Exists(root); attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(400); // a file may still be closing
            }
        }

        if (Directory.Exists(root))
        {
            MessageBox.Show($"Some files could not be deleted (they may be open in another program):\n{root}\n\nDelete the folder yourself after closing other programs.",
                ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Runs a capture with the given options and hands the result to the Editor / Library / clipboard.</summary>
    /// <summary>
    /// Scrolling capture: select the area that scrolls, then auto-scroll or add frames manually; the stitched image
    /// opens in the Editor for review. SnapForge's own windows are hidden meanwhile (the control panel is excluded
    /// from capture).
    /// </summary>
    internal static async Task CaptureScrollingAsync()
    {
        if (Role == AppRole.Editor)
        {
            AskCapture(new HandoffRequest(Handoff.Capture, Mode: Handoff.ScrollingMode));
            return;
        }

        MainWindow? main = MainShell;
        if (CaptureController.IsBusy || AppServices.Current is not { } services)
        {
            return;
        }

        CaptureResult? first = await CaptureController.CaptureAsync(services.Settings.Capture with { Mode = Core.Capture.CaptureMode.Region, DelaySeconds = 0, CaptureCursor = false }).ConfigureAwait(true);
        if (first is null || CaptureController.LastSelection is not { } region)
        {
            main?.SetStatus("Scrolling capture cancelled.");
            return;
        }

        var hidden = Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToList();
        hidden.ForEach(w => w.Hide());
        Core.Imaging.PixelBuffer? image;
        try
        {
            image = await Capture.ScrollingCaptureWindow.RunAsync(region).ConfigureAwait(true);
        }
        finally
        {
            hidden.ForEach(w => w.Show());
        }

        if (image is null)
        {
            main?.SetStatus("Scrolling capture cancelled.");
            return;
        }

        CaptureOptions options = services.Settings.Capture with { PreviewInEditor = true, CopyToClipboard = false };
        if (main is null)
        {
            await AcceptCaptureInCaptureProgramAsync(services, image, DateTime.Now, options).ConfigureAwait(true);
            return;
        }

        main.AcceptCapture(image, DateTime.Now, options);
        main.ShowEditorWorkspace();
        main.Activate();
        main.SetStatus($"Scrolling capture: {image.Width} × {image.Height}. Check it for gaps or repeats before sharing.");
    }

    /// <summary>Captures the same screen rectangle as the last capture, with the same options (no selection step).</summary>
    internal static Task RepeatLastCaptureAsync()
    {
        if (Role == AppRole.Editor)
        {
            AskCapture(new HandoffRequest(Handoff.Capture, Mode: Handoff.RepeatMode));
            return Task.CompletedTask;
        }

        if (CaptureController.LastSelection is not { } region || CaptureController.LastOptions is not { } options)
        {
            MainShell?.SetStatus("Nothing to repeat yet — take a capture first.");
            NotifyBalloon(ProgramName, "Nothing to repeat yet — take a capture first.");
            return Task.CompletedTask;
        }

        return CaptureWithOptionsAsync(options with { DelaySeconds = 0 }, region);
    }

    internal static async Task CaptureWithOptionsAsync(CaptureOptions options, Core.Geometry.PixelRect? fixedRegion = null)
    {
        if (Role == AppRole.Editor)
        {
            // Capturing belongs to SnapForge Capture: ask it (it saves the capture and hands it back to the Editor).
            AskCapture(new HandoffRequest(Handoff.Capture, Mode: options.Mode.ToString()));
            return;
        }

        MainWindow? main = MainShell;
        if (CaptureController.IsBusy || AppServices.Current is not { } services)
        {
            return;
        }

        bool mainWasVisible = main?.IsVisible == true;
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
            main?.SetStatus("Capture cancelled.");
            return;
        }

        if (main is null)
        {
            // SnapForge Capture: save, then hand the capture to SnapForge Editor (RC3).
            await AcceptCaptureInCaptureProgramAsync(services, result.Image, result.CapturedAt, options).ConfigureAwait(true);
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
        if (Role == AppRole.Editor)
        {
            AskCapture(new HandoffRequest(Handoff.CapturePanel));
            return;
        }

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

    internal static async Task ExitAsync(bool forSetup = false)
    {
        if (_exitInProgress)
        {
            return; // Setup may send both its own request and the older "-exit" request: ask only once
        }

        _exitInProgress = true;
        try
        {
            if (forSetup)
            {
                MainShell?.Activate(); // any question about unsaved work appears in front of Setup
            }

            if (Video.RecordingController.IsActive && !(forSetup ? ConfirmSetupWhileRecording() : ConfirmExitWhileRecording()))
            {
                return;
            }

            if (MainShell is { } main && !await main.CloseForRealAsync().ConfigureAwait(true))
            {
                return;
            }

            _captureWindow?.Close();
            Current.Shutdown();
        }
        finally
        {
            _exitInProgress = false;
        }
    }

    /// <summary>RC4: name of the event SnapForge Setup sets to ask every SnapForge in this session to close.</summary>
    internal const string SetupExitEventName = @"Local\SnapForge-SetupExit-7c1d";

    /// <summary>Waits for Setup's close request; after the user keeps SnapForge open, waits until Setup gives up.</summary>
    private void WatchSetupExitRequest()
    {
        if (_setupExitSignal is not { } signal)
        {
            return;
        }

        ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            await ExitAsync(forSetup: true).ConfigureAwait(true);
            // Still running (the user kept a recording or work open): listen again once Setup has reset the request.
            _ = Task.Run(async () =>
            {
                while (signal.WaitOne(0))
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                }

                await Dispatcher.BeginInvoke(WatchSetupExitRequest);
            });
        }), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>RC4: Setup wants to update SnapForge while a recording runs — the user decides (shown in front).</summary>
    internal static bool ConfirmSetupWhileRecording() =>
        MessageBox.Show(
            "SnapForge Setup wants to update SnapForge, but a recording is in progress.\n\n" +
            "Yes: stop the recording, save it to the Library and close SnapForge so Setup can continue.\n" +
            "No: keep recording. Setup will wait; you can cancel Setup or click Retry there later.",
            "SnapForge Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly) == MessageBoxResult.Yes;

    /// <summary>A running recording is never stopped without the user's consent (SF-008 / SF-014).</summary>
    internal static bool ConfirmExitWhileRecording() =>
        MessageBox.Show(
            "A recording is in progress.\n\nStop the recording, save it to the Library and exit SnapForge?",
            ProductInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    internal static void NotifyOnce(string title, string text)
    {
        if (!_notifiedRunning)
        {
            _notifiedRunning = true;
            NotifyBalloon(title, text);
        }
    }

    internal static void NotifyBalloon(string title, string text, bool warning = false) => _shell?.ShowBalloon(title, text, warning);

    /// <summary>First-launch welcome / Help → Getting started (offline).</summary>
    internal static void ShowWelcome(Window? owner, Action? afterClose = null)
    {
        AppServices? services = AppServices.Current;
        string root = services?.Library.Root ?? "(not opened)";
        string capture = services?.Settings.CaptureHotkey is { } ch ? Views.CaptureWindow.FriendlyGesture(ch) : "the Capture button";
        string record = services?.Settings.Video.RecordHotkey?.ToString() ?? "the Record button";
        Dialogs.WelcomeWindow.ShowFor(owner, root, capture, record,
            services is null ? null : () => _ = CaptureWithOptionsAsync(services.Settings.Capture), afterClose);
    }

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
            "You can change the location, retention and backups in Settings → Capture Library.";
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

        if (ProductInfo.Profile is not null)
        {
            // Isolated test profiles never take global hotkeys away from the user's normal SnapForge.
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

        if (Video.RecordingController.IsRecording)
        {
            // RC3: the recording can always be stopped from here, even if its controls are hidden or off screen.
            bool paused = Video.RecordingController.Phase == Video.RecordingPhase.Paused;
            Add("■  _Stop recording and save", () => _ = Video.RecordingController.StopAsync(), bold: true);
            Add(paused ? "_Resume recording" : "_Pause recording", Video.RecordingController.TogglePause);
            Add("Show recording _controls", Video.RecordingController.ShowControls);
            if (Video.RecordingController.HasPreview)
            {
                bool hidden = Video.RecordingController.PreviewHidden;
                Add(hidden ? "Show camera pre_view" : "Hide camera pre_view", () => Video.RecordingController.SetPreviewVisible(hidden));
            }

            menu.Items.Add(new Separator());
        }

        Add("_Capture", () => _ = CaptureWithOptionsAsync(AppServices.Current!.Settings.Capture), bold: !Video.RecordingController.IsRecording);
        if (!Video.RecordingController.IsActive)
        {
            Add("_Record…", Video.RecordingController.OpenStudio);
        }

        Add("Capture _panel…", ShowCaptureWindow);
        Add("Show _QuickDock", ShowWidget);
        nint previousForeground = Interop.NativeMethods.GetForegroundWindow();
        Add("Open QuickDock with _keyboard", () => DockActions.OpenQuickDockWithKeyboard(previousForeground));
        if (AppServices.Current?.Settings.Widget.Enabled == true)
        {
            Add("_Hide QuickDock", () => AppServices.Current?.UpdateSettings(s => s with { Widget = s.Widget with { Enabled = false } }));
        }
        menu.Items.Add(new Separator());
        Add("Open SnapForge _Editor", () => OpenInEditor(new HandoffRequest(Handoff.ShowEditor)));
        Add("Open _Library", () => OpenInEditor(new HandoffRequest(Handoff.ShowLibrary)));
        Add("_Settings…", () => ShowSettings(null));
        menu.Items.Add(new Separator());
        Add("_About SnapForge Capture", () => ShowAbout(null));
        Add("E_xit SnapForge Capture", () => _ = ExitAsync());
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
            string dir = Path.Combine(ProductInfo.DataRoot, "logs");
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
