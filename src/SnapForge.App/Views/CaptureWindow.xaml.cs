using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SnapForge.App.Dialogs;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Settings;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Views;

/// <summary>
/// The compact capture panel (SF-005): segmented Image / Video tabs, option cards ("What to capture" chips, "After
/// capture", "Options"), a full-width primary action with its hotkey on a key cap, presets and links to the Editor
/// and Library. All-in-One, Effects and Share destinations are visibly disabled ("Not available yet") until they exist.
/// </summary>
public partial class CaptureWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _resetNoticeTimer;
    private CaptureOptions? _beforeReset;
    private int _lastDelay = 3;
    private bool _loading;
    private bool _scrolling;

    internal CaptureWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        _resetNoticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _resetNoticeTimer.Tick += (_, _) => HideResetNotice();
        SourceInitialized += (_, _) =>
        {
            WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
            RestorePosition();
        };
        ThemeManager.ThemeChanged += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        services.SettingsChanged += (_, _) => Dispatcher.BeginInvoke(LoadFromSettings);
        PreviewKeyDown += OnPanelKeyDown;
        LoadFromSettings();
    }

    internal static string ModeLabel(CaptureMode mode) => mode switch
    {
        CaptureMode.Window => "Window",
        CaptureMode.ActiveWindow => "Active window",
        CaptureMode.SingleScreen => "Display",
        CaptureMode.AllScreens => "Full screen",
        _ => "Region",
    };

    /// <summary>"Print Screen" rather than the key name.</summary>
    internal static string FriendlyGesture(HotkeyGesture gesture) =>
        gesture.ToString().Replace("PrintScreen", "Print Screen", StringComparison.Ordinal);

    internal void LoadFromSettings()
    {
        _loading = true;
        CaptureOptions o = _services.Settings.Capture;
        PreviewBox.IsChecked = o.PreviewInEditor;
        CopyBox.IsChecked = o.CopyToClipboard;
        SaveBox.IsChecked = o.SaveToLibrary;
        CursorBox.IsChecked = o.CaptureCursor;
        if (o.DelaySeconds > 0)
        {
            _lastDelay = o.DelaySeconds;
        }

        TopmostMenu.IsChecked = _services.Settings.CapturePanelTopmost;
        Topmost = _services.Settings.CapturePanelTopmost;
        DelayBox.IsChecked = o.DelaySeconds > 0;
        DelayBox.Content = o.DelaySeconds > 0 ? $"Time Delay ({o.DelaySeconds} s)" : "Time Delay";
        LoadVideoOptions();

        foreach ((RadioButton chip, CaptureMode mode) in ImageTargets)
        {
            chip.IsChecked = !_scrolling && o.Mode == mode;
        }

        ScrollingChip.IsChecked = _scrolling;
        UpdatePrimaryAction();

        string action = IsVideoMode ? "Record / pause / resume" : "Capture";
        HotkeyRegistration? reg = App.HotkeyResults.FirstOrDefault(r => r.Action == action);
        string tip;
        if ((IsVideoMode ? _services.Settings.Video.RecordHotkey : _services.Settings.CaptureHotkey) is not { } hk)
        {
            HotkeyText.Text = "No hotkey";
            tip = IsVideoMode ? "Set a global record hotkey in Settings → Hotkeys." : "Set a global capture hotkey in Settings → Hotkeys.";
            HotkeyWarningIcon.Visibility = Visibility.Collapsed;
        }
        else if (reg is { Succeeded: false })
        {
            HotkeyText.Text = $"{FriendlyGesture(hk)} unavailable";
            tip = HotkeyConflictHelp(hk, reg.Error);
            HotkeyWarningIcon.Visibility = Visibility.Visible;
        }
        else
        {
            HotkeyText.Text = FriendlyGesture(hk);
            tip = IsVideoMode
                ? "Global record hotkey: starts a recording directly (works while SnapForge runs in the notification area)"
                : "Global capture hotkey (works while SnapForge runs in the notification area)";
            HotkeyWarningIcon.Visibility = Visibility.Collapsed;
        }

        HotkeyText.ToolTip = tip;
        HotkeyCap.ToolTip = tip;
        HotkeyCap.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"Hotkey: {HotkeyText.Text}");
        _loading = false;
    }

    /// <summary>Explains a failed hotkey, naming the Windows 11 Snipping Tool setting for Print Screen.</summary>
    internal static string HotkeyConflictHelp(HotkeyGesture gesture, string? error)
    {
        string text = $"{FriendlyGesture(gesture)} could not be registered: {error ?? "another program is using it"}.";
        if (gesture.Key == "PrintScreen")
        {
            text += "\nOn Windows 11 the Print Screen key usually opens the Snipping Tool: Settings → Accessibility → Keyboard → " +
                    "turn off “Use the Print screen key to open screen capture”, then restart SnapForge — or choose another hotkey in SnapForge Settings → Hotkeys.";
        }

        return text;
    }

    private void Update(Func<CaptureOptions, CaptureOptions> change) =>
        _services.UpdateSettings(s => s with { Capture = change(s.Capture).Normalized() });

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Update(o => o with
        {
            PreviewInEditor = PreviewBox.IsChecked == true,
            CopyToClipboard = CopyBox.IsChecked == true,
            SaveToLibrary = SaveBox.IsChecked == true,
            CaptureCursor = CursorBox.IsChecked == true,
        });
    }

    private void OnDelayToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            int seconds = DelayBox.IsChecked == true ? Math.Max(1, _lastDelay) : 0;
            Update(o => o with { DelaySeconds = seconds });
        }
    }

    internal void SetDelay(int seconds)
    {
        if (seconds > 0)
        {
            _lastDelay = seconds;
        }

        Update(o => o with { DelaySeconds = seconds });
    }

    internal void SetMode(CaptureMode mode) => Update(o => o with { Mode = mode });

    /// <summary>The "What to capture" chips that map to a capture mode (Scrolling is a separate action).</summary>
    internal (RadioButton Chip, CaptureMode Mode)[] ImageTargets =>
    [
        (RegionChip, CaptureMode.Region),
        (WindowChip, CaptureMode.Window),
        (ActiveWindowChip, CaptureMode.ActiveWindow),
        (DisplayChip, CaptureMode.SingleScreen),
        (FullScreenChip, CaptureMode.AllScreens),
    ];

    /// <summary>True while the Scrolling chip is chosen: the primary action starts a scrolling capture.</summary>
    internal bool IsScrollingSelected => _scrolling;

    private void OnTargetChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton chip || RegionChip is null)
        {
            return;
        }

        if (ReferenceEquals(chip, ScrollingChip))
        {
            _scrolling = true;
            UpdatePrimaryAction();
            return;
        }

        _scrolling = false;
        foreach ((RadioButton c, CaptureMode mode) in ImageTargets)
        {
            if (ReferenceEquals(c, chip))
            {
                SetMode(mode);
            }
        }

        UpdatePrimaryAction();
    }

    /// <summary>Primary button text and accessible name for the current tab and choice.</summary>
    private void UpdatePrimaryAction()
    {
        (string text, string name, string help) = (IsVideoMode, _scrolling) switch
        {
            (true, _) => ("Open Studio…", "Open Studio", "Opens the recording studio with the video options above"),
            (false, true) => ("Start scrolling capture", "Start scrolling capture", "Select the area that scrolls; SnapForge stitches one tall image"),
            _ => ("Capture", "Capture", "Captures with the options above (Enter)"),
        };
        CaptureButtonText.Text = text;
        CaptureButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, name);
        CaptureButton.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, help);
        CaptureButtonIcon.Data = (System.Windows.Media.Geometry)FindResource(IsVideoMode ? "IconVideoMode" : "BrandApertureMark");
    }

    private void OnDelayMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = DelayMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
        int current = _services.Settings.Capture.DelaySeconds;
        foreach (int s in CaptureOptions.AllowedDelays)
        {
            var item = new MenuItem { Header = s == 0 ? "No delay" : $"{s} seconds", IsCheckable = true, IsChecked = s == current };
            int seconds = s;
            item.Click += (_, _) => SetDelay(seconds);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var custom = new MenuItem { Header = "_Custom…" };
        custom.Click += (_, _) =>
        {
            string? text = InputDialog.Ask(this, "Time delay", $"Delay in seconds (1–{CaptureOptions.MaxDelaySeconds}):", Math.Max(1, current).ToString(CultureInfo.CurrentCulture));
            if (text is not null && int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) && value is >= 1 and <= CaptureOptions.MaxDelaySeconds)
            {
                SetDelay(value);
            }
            else if (text is not null)
            {
                MessageBox.Show(this, $"Enter a whole number from 1 to {CaptureOptions.MaxDelaySeconds}.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        menu.Items.Add(custom);
        DelayMenuButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    internal void ResetOptions()
    {
        _beforeReset = _services.Settings.Capture;
        Update(_ => new CaptureOptions());
        UndoResetMenu.IsEnabled = true;
        ResetNotice.Visibility = Visibility.Visible;
        _resetNoticeTimer.Stop();
        _resetNoticeTimer.Start();
    }

    internal void UndoReset()
    {
        if (_beforeReset is { } previous)
        {
            Update(_ => previous);
        }

        HideResetNotice();
    }

    private void HideResetNotice()
    {
        _resetNoticeTimer.Stop();
        _beforeReset = null;
        UndoResetMenu.IsEnabled = false;
        ResetNotice.Visibility = Visibility.Collapsed;
    }

    private void OnReset(object sender, RoutedEventArgs e) => ResetOptions();

    private void OnUndoReset(object sender, RoutedEventArgs e) => UndoReset();

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if (IsVideoMode)
        {
            DockActions.OpenStudio();
        }
        else if (_scrolling)
        {
            _ = App.CaptureScrollingAsync();
        }
        else
        {
            _ = App.CaptureWithOptionsAsync(_services.Settings.Capture);
        }
    }

    // ---------------------------------------------------------------- video mode (milestone V1)

    internal bool IsVideoMode => VideoMode.IsChecked == true;

    internal static string SourceLabel(RecordingSource source) => source switch
    {
        RecordingSource.Window => "Window",
        RecordingSource.Display => "Display",
        _ => "Region",
    };

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (ImageOptions is null || VideoOptions is null)
        {
            return; // during InitializeComponent
        }

        bool video = IsVideoMode;
        ImageOptions.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        VideoOptions.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        LoadFromSettings();
    }

    private void LoadVideoOptions()
    {
        VideoSettings v = _services.Settings.Video;
        VideoRegionChip.IsChecked = v.Source == RecordingSource.Region;
        VideoWindowChip.IsChecked = v.Source == RecordingSource.Window;
        VideoDisplayChip.IsChecked = v.Source == RecordingSource.Display;
        FrameRateText.Text = $"{v.FramesPerSecond} FPS";
        QualityText.Text = v.Quality == VideoQuality.High ? "High" : "Standard";
        SystemAudioBox.IsChecked = v.RecordSystemAudio;
        MicBox.IsChecked = v.RecordMicrophone;
        VideoCursorBox.IsChecked = v.CaptureCursor;
        CountdownBox.IsChecked = v.Countdown;
        CountdownBox.Content = v.Countdown && v.CountdownSeconds > 0 ? $"Countdown ({v.CountdownSeconds} s)" : "Countdown";
    }

    private void UpdateVideo(Func<VideoSettings, VideoSettings> change) =>
        _services.UpdateSettings(s => s with { Video = change(s.Video).Normalized() });

    private void OnVideoOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateVideo(v => v with
        {
            RecordSystemAudio = SystemAudioBox.IsChecked == true,
            RecordMicrophone = MicBox.IsChecked == true,
            CaptureCursor = VideoCursorBox.IsChecked == true,
            Countdown = CountdownBox.IsChecked == true,
        });
    }

    private static void ShowChoiceMenu<T>(FrameworkElement target, IEnumerable<(T Value, string Label)> choices, T current, Action<T> choose, System.Windows.Controls.Primitives.PlacementMode placement = System.Windows.Controls.Primitives.PlacementMode.Bottom)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = placement };
        foreach ((T value, string label) in choices)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            item.Click += (_, _) => choose(value);
            menu.Items.Add(item);
        }

        target.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void OnSourceChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || VideoRegionChip is null)
        {
            return;
        }

        RecordingSource source = ReferenceEquals(sender, VideoWindowChip) ? RecordingSource.Window
            : ReferenceEquals(sender, VideoDisplayChip) ? RecordingSource.Display
            : RecordingSource.Region;
        UpdateVideo(v => v with { Source = source });
    }

    private void OnFrameRateMenu(object sender, RoutedEventArgs e) => ShowChoiceMenu(
        FrameRateButton,
        VideoSettings.SupportedFrameRates.Select(f => (f, $"{f} FPS")),
        _services.Settings.Video.FramesPerSecond,
        fps => UpdateVideo(v => v with { FramesPerSecond = fps }));

    private void OnQualityMenu(object sender, RoutedEventArgs e) => ShowChoiceMenu(
        QualityButton,
        [(VideoQuality.Standard, "Standard (smaller files)"), (VideoQuality.High, "High (sharper, larger files)")],
        _services.Settings.Video.Quality,
        q => UpdateVideo(v => v with { Quality = q }));

    private void OnCountdownMenu(object sender, RoutedEventArgs e) => ShowChoiceMenu(
        CountdownMenuButton,
        VideoSettings.CountdownChoices.Select(c => (c, c == 0 ? "No countdown" : $"{c} seconds")),
        _services.Settings.Video.Countdown ? _services.Settings.Video.CountdownSeconds : 0,
        c => UpdateVideo(v => v with { Countdown = c > 0, CountdownSeconds = c > 0 ? c : v.CountdownSeconds }),
        System.Windows.Controls.Primitives.PlacementMode.Right);

    private void OnPresetsMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PresetsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (CapturePreset preset in _services.Settings.Presets)
        {
            CapturePreset p = preset;
            var item = new MenuItem { Header = p.Name, InputGestureText = p.Hotkey is { } h ? FriendlyGesture(h) : string.Empty };
            AddSub(item, "_Capture now", () => _ = App.CaptureWithOptionsAsync(p.Options), bold: true);
            AddSub(item, "_Use these settings", () => Update(_ => p.Options));
            AddSub(item, "Set _hotkey…", () => SetPresetHotkey(p));
            AddSub(item, "_Rename…", () => RenamePreset(p));
            AddSub(item, "_Delete…", () => DeletePreset(p));
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var save = new MenuItem { Header = "Save current settings as _preset…" };
        save.Click += OnSavePreset;
        menu.Items.Add(save);
        PresetsButton.ContextMenu = menu;
        menu.IsOpen = true;

        static void AddSub(MenuItem parent, string header, Action action, bool bold = false)
        {
            var sub = new MenuItem { Header = header, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            sub.Click += (_, _) => action();
            parent.Items.Add(sub);
        }
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        string? name = InputDialog.Ask(this, "Save preset", "Preset name:", "My preset");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var preset = new CapturePreset { Name = name.Trim(), Options = _services.Settings.Capture };
            _services.UpdateSettings(s => s with { Presets = [.. s.Presets, preset] });
        }
    }

    private void RenamePreset(CapturePreset p)
    {
        string? name = InputDialog.Ask(this, "Rename preset", "Preset name:", p.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _services.UpdateSettings(s => s with { Presets = [.. s.Presets.Select(x => x.Id == p.Id ? x with { Name = name.Trim() } : x)] });
        }
    }

    private void DeletePreset(CapturePreset p)
    {
        if (MessageBox.Show(this, $"Delete the preset \"{p.Name}\"?", "SnapForge", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _services.UpdateSettings(s => s with { Presets = [.. s.Presets.Where(x => x.Id != p.Id)] });
        }
    }

    private void SetPresetHotkey(CapturePreset p)
    {
        string? text = InputDialog.Ask(this, "Preset hotkey", $"Hotkey for \"{p.Name}\" (e.g. Ctrl+Shift+F9; leave empty for none):", p.Hotkey?.ToString() ?? string.Empty);
        if (text is null)
        {
            return;
        }

        try
        {
            HotkeyGesture? gesture = HotkeyGesture.Parse(text);
            if (gesture is { IsValid: false })
            {
                MessageBox.Show(this, gesture.Problem, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _services.UpdateSettings(s => s with { Presets = [.. s.Presets.Select(x => x.Id == p.Id ? x with { Hotkey = gesture } : x)] });
            HotkeyRegistration? result = App.HotkeyResults.FirstOrDefault(r => r.Action == $"Preset: {p.Name}");
            if (result is { Succeeded: false } && gesture is not null)
            {
                MessageBox.Show(this, HotkeyConflictHelp(gesture, result.Error), "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOpenEditor(object sender, RoutedEventArgs e)
    {
        App.MainShell?.BringToFront();
        App.MainShell?.ShowEditorWorkspace();
    }

    private void OnOpenLibrary(object sender, RoutedEventArgs e)
    {
        App.MainShell?.BringToFront();
        App.MainShell?.ShowLibraryWorkspace();
    }

    private void OnSettings(object sender, RoutedEventArgs e) => App.ShowSettings(this);

    private void OnToggleWidget(object sender, RoutedEventArgs e) => App.ShowWidget();

    /// <summary>View → Open QuickDock with keyboard (FA-004): Esc in QuickDock returns focus to this panel.</summary>
    private void OnDockKeyboard(object sender, RoutedEventArgs e) => DockActions.OpenQuickDockWithKeyboard();

    /// <summary>The services (settings, library) this panel was created with.</summary>
    internal AppServices Services => _services;

    /// <summary>Menu ticks always reflect the current settings when the menu opens.</summary>
    internal void OnViewMenuOpened(object sender, RoutedEventArgs e) => TopmostMenu.IsChecked = _services.Settings.CapturePanelTopmost;

    private void OnToggleTopmost(object sender, RoutedEventArgs e) =>
        _services.UpdateSettings(s => s with { CapturePanelTopmost = TopmostMenu.IsChecked });

    private void OnShortcuts(object sender, RoutedEventArgs e) => App.MainShell?.ShowShortcuts(this);

    private void OnPrivacy(object sender, RoutedEventArgs e) => App.ShowPrivacyNotice(this);

    private void OnClosePanel(object sender, RoutedEventArgs e) => Close();

    private void OnPanelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !PanelMenu.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            Close();
        }
    }

    // ------------------------------------------------------------------ position memory

    private void RestorePosition()
    {
        try
        {
            ScreenLayout layout = PhysicalPlacement.Layout();
            PixelRect? current = PhysicalPlacement.GetBounds(this);
            int w = current?.Width ?? (int)Width;
            int h = current?.Height ?? 300;
            PixelRect target = WindowPosition.Clamp(layout, _services.Settings.CapturePanelPosition, w, h);
            PhysicalPlacement.SetBounds(this, target, resize: false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void RememberPosition()
    {
        if (PhysicalPlacement.GetBounds(this) is { } b && WindowState == WindowState.Normal)
        {
            MonitorInfo monitor = PhysicalPlacement.MonitorFor(PhysicalPlacement.Layout(), b);
            var pos = new WindowPosition(monitor.DeviceName, b.X, b.Y);
            if (pos != _services.Settings.CapturePanelPosition)
            {
                _services.UpdateSettings(s => s with { CapturePanelPosition = pos });
            }
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        RememberPosition();

        // Closing only hides the panel; it stays available from the notification area and QuickDock.
        base.OnClosing(e);
        if (Application.Current.MainWindow is { } main && main.IsLoaded && !e.Cancel && App.HasNotificationIcon)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
