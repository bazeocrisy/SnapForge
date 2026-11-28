using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Settings;

namespace SnapForge.App.Video;

/// <summary>
/// RC3 Record panel (replaces the long SnapForge Studio form of rc.2): what to record (Screen / Window / Area), three
/// clear switches — Camera, Mic, Computer audio — and Start recording. Everything else (camera only, devices, frame
/// rate, quality, countdown, camera overlay look and position, pointer, Library, opening the Editor afterwards) is under
/// "More options". The target type and the options are remembered; Camera and Mic start OFF in every new SnapForge
/// Capture session and must be turned on deliberately (<see cref="RecordingController.SessionCamera"/>,
/// <see cref="RecordingController.SessionMicrophone"/>).
/// <para>Camera on opens the camera and shows the floating <see cref="CameraPreviewWindow"/> at once, so you can see
/// yourself before recording; the preview stays on screen for the whole recording. Mic on shows a live level meter, so
/// you can check the microphone picks up sound. The panel and the preview are excluded from screen capture. Closing
/// the panel, locking the PC or starting the recording releases the camera and the microphone (the recorder opens its
/// own); the preview window is handed to the recording (<see cref="TakePreview"/>).</para>
/// </summary>
#pragma warning disable CA1001 // The microphone meter is released when the window closes (StopMeter in Closed).
internal sealed class StudioWindow : Window
#pragma warning restore CA1001
{
    private static readonly Geometry CameraIcon = Geometry.Parse("M2,6 h12 a2,2 0 0 1 2,2 v1.5 l5,-3 v11 l-5,-3 V16 a2,2 0 0 1 -2,2 H2 a2,2 0 0 1 -2,-2 V8 a2,2 0 0 1 2,-2 z");
    private static readonly Geometry MicIcon = Geometry.Parse("M12,2 a3,3 0 0 1 3,3 v6 a3,3 0 0 1 -6,0 V5 a3,3 0 0 1 3,-3 z M5,10 h2 a5,5 0 0 0 10,0 h2 a7,7 0 0 1 -6,6.9 V20 h3 v2 H8 v-2 h3 v-3.1 A7,7 0 0 1 5,10 z");
    private static readonly Geometry SpeakerIcon = Geometry.Parse("M3,9 h4 l5,-5 v16 l-5,-5 H3 z M15,8 a5,5 0 0 1 0,8 l-1,-1.5 a3.2,3.2 0 0 0 0,-5 z M17,4.5 a9,9 0 0 1 0,15 l-1,-1.6 a7,7 0 0 0 0,-11.8 z");

    private readonly AppServices _services;
    private readonly Func<string?, string, ICameraSource> _cameraFactory;
    private readonly Func<IReadOnlyList<Mf.VideoDevice>> _listCameras;
    private readonly Border _targetRow;
    private readonly TextBlock _targetHeader;
    private readonly ToggleButton _cameraToggle;
    private readonly ToggleButton _micToggle;
    private readonly ToggleButton _systemToggle;
    private readonly StackPanel _cameraInfo;
    private readonly TextBlock _cameraStatus;
    private readonly Button _previewToggle;
    private readonly TextBlock _cameraError;
    private readonly Button _cameraPrivacy;
    private readonly StackPanel _micInfo;
    private readonly ProgressBar _micLevel;
    private readonly TextBlock _micStatus;
    private readonly TextBlock _micError;
    private readonly Button _micPrivacy;
    private readonly TextBlock _cameraText;
    private readonly Button _cameraButton;
    private readonly Button _micButton;
    private readonly CheckBox _cameraOnly;
    private readonly StackPanel _cursorRow;
    private readonly Button _start;
    private readonly DispatcherTimer _meterTimer;
    private IReadOnlyList<Mf.VideoDevice> _cameras = [];
    private ICameraSource? _camera;
    private CameraPreviewWindow? _preview;
    private MicrophoneMeter? _meter;
    private int _generation;
    private Task _cameraReleased = Task.CompletedTask;
    private bool _starting;

    /// <param name="cameraFactory">Creates the preview camera (symbolic link, name); tests pass the synthetic camera.</param>
    /// <param name="listCameras">Lists cameras without opening them; tests pass a fixed list.</param>
    public StudioWindow(AppServices services, Func<string?, string, ICameraSource>? cameraFactory = null, Func<IReadOnlyList<Mf.VideoDevice>>? listCameras = null)
    {
        _services = services;
        _cameraFactory = cameraFactory ?? ((id, name) => new MediaFoundationCameraSource(id, name));
        _listCameras = listCameras ?? MediaFoundationCameraSource.Enumerate;
        Title = "SnapForge — Record";
        Width = 392;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)FindResource("UiFont");
        AutomationProperties.SetName(this, "SnapForge Record panel");

        VideoSettings v = Settings;
        var root = new StackPanel { Margin = new Thickness(18, 12, 18, 14) };

        // ---- What to record
        _targetHeader = Header("WHAT TO RECORD");
        _targetHeader.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(_targetHeader);
        _targetRow = Segments("What to record", "target",
            [("Screen", "A whole screen", RecordingSource.Display), ("Window", "One window", RecordingSource.Window), ("Area", "An area you draw", RecordingSource.Region)],
            v.Source, t => Save(s => s with { Source = t }), stretch: true);
        root.Children.Add(_targetRow);

        // ---- Camera, Mic, Computer audio
        var switches = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        _cameraToggle = Switch("Camera", CameraIcon, RecordingController.SessionCamera, "Camera: show yourself in the recording");
        _micToggle = Switch("Mic", MicIcon, RecordingController.SessionMicrophone, "Microphone: record your voice");
        _systemToggle = Switch("Computer audio", SpeakerIcon, v.RecordSystemAudio, "Computer audio: record what you hear");
        _cameraToggle.Click += (_, _) => SetCamera(_cameraToggle.IsChecked == true);
        _micToggle.Click += (_, _) => SetMicrophone(_micToggle.IsChecked == true);
        _systemToggle.Click += (_, _) =>
        {
            Save(s => s with { RecordSystemAudio = _systemToggle.IsChecked == true });
            UpdateSwitch(_systemToggle);
        };
        switches.Children.Add(_cameraToggle);
        switches.Children.Add(_micToggle);
        switches.Children.Add(_systemToggle);
        root.Children.Add(switches);

        // ---- Camera status (where it will appear) and problems
        _cameraInfo = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        var cameraLine = new DockPanel();
        _previewToggle = new Button { Content = "Hide preview", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
        _previewToggle.SetResourceReference(StyleProperty, "ToolbarButton");
        _previewToggle.Click += (_, _) => TogglePreviewVisible();
        DockPanel.SetDock(_previewToggle, Dock.Right);
        cameraLine.Children.Add(_previewToggle);
        _cameraStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_cameraStatus, "Camera status");
        cameraLine.Children.Add(_cameraStatus);
        _cameraInfo.Children.Add(cameraLine);
        _cameraError = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        _cameraError.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        _cameraPrivacy = Link("Open camera privacy settings", () => PrivacySettings.Open(PrivacySettings.CameraUri));
        _cameraInfo.Children.Add(_cameraError);
        _cameraInfo.Children.Add(_cameraPrivacy);
        root.Children.Add(_cameraInfo);

        // ---- Microphone level and problems
        _micInfo = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var micLine = new DockPanel();
        var micLabel = new TextBlock { Text = "Mic level", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        micLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        DockPanel.SetDock(micLabel, Dock.Left);
        micLine.Children.Add(micLabel);
        _micLevel = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        _micLevel.SetResourceReference(StyleProperty, "LevelMeter");
        AutomationProperties.SetName(_micLevel, "Microphone level");
        micLine.Children.Add(_micLevel);
        _micInfo.Children.Add(micLine);
        _micStatus = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Text = "Say something — the bar moves when the microphone hears you." };
        _micStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        _micInfo.Children.Add(_micStatus);
        _micError = new TextBlock { Text = PrivacySettings.MicrophoneBlockedMessage, TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
        _micError.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        _micPrivacy = Link("Open microphone privacy settings", () => PrivacySettings.Open(PrivacySettings.MicrophoneUri));
        _micInfo.Children.Add(_micError);
        _micInfo.Children.Add(_micPrivacy);
        root.Children.Add(_micInfo);

        // ---- More options
        var more = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _cameraOnly = new CheckBox { Content = "Record the camera _only (no screen)", IsChecked = v.StudioMode == StudioMode.Camera, Margin = new Thickness(0, 4, 0, 4) };
        _cameraOnly.Click += (_, _) =>
        {
            bool only = _cameraOnly.IsChecked == true;
            Save(s => s with { StudioMode = only ? StudioMode.Camera : StudioMode.ScreenAndCamera });
            if (only && _cameraToggle.IsChecked != true)
            {
                _cameraToggle.IsChecked = true;
                SetCamera(true);
            }

            UpdateMode();
        };
        more.Children.Add(_cameraOnly);

        more.Children.Add(SubHeader("Camera"));
        _cameraText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _cameraButton = Dropdown(_cameraText, "Camera device");
        _cameraButton.Click += (_, _) => ShowCameraMenu();
        more.Children.Add(_cameraButton);
        more.Children.Add(Labeled("Position", Segments("Camera position in the recording", "corner",
            [("↖", "Top left", CameraCorner.TopLeft), ("↗", "Top right", CameraCorner.TopRight), ("↙", "Bottom left", CameraCorner.BottomLeft), ("↘", "Bottom right", CameraCorner.BottomRight)],
            v.CameraCorner, x => Save(s => s with { CameraCorner = x }))));
        more.Children.Add(Labeled("Shape", Segments("Camera shape", "shape",
            [("Circle", "Circle", CameraOverlayShape.Circle), ("Rounded", "Rounded rectangle", CameraOverlayShape.RoundedRectangle)],
            v.CameraShape, x => Save(s => s with { CameraShape = x }))));
        more.Children.Add(Labeled("Size", Segments("Camera size", "size",
            [("S", "Small", CameraOverlaySize.Small), ("M", "Medium", CameraOverlaySize.Medium), ("L", "Large", CameraOverlaySize.Large)],
            v.CameraSize, x => Save(s => s with { CameraSize = x }))));
        var mirror = new CheckBox { Content = "_Mirror the camera picture", IsChecked = v.CameraMirror, Margin = new Thickness(0, 4, 0, 0) };
        mirror.Click += (_, _) => Save(s => s with { CameraMirror = mirror.IsChecked == true });
        more.Children.Add(mirror);

        more.Children.Add(SubHeader("Microphone"));
        var micText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _micButton = Dropdown(micText, "Microphone device");
        SetUpMicrophoneMenu(micText);
        more.Children.Add(_micButton);

        more.Children.Add(SubHeader("Video"));
        more.Children.Add(Labeled("Frame rate", Segments("Frame rate", "fps",
            [("30 FPS", "30 frames per second", 30), ("60 FPS", "60 frames per second", 60)],
            v.FramesPerSecond, x => Save(s => s with { FramesPerSecond = x }))));
        more.Children.Add(Labeled("Quality", Segments("Quality", "quality",
            [("Standard", "Standard quality", VideoQuality.Standard), ("High", "High quality (larger files)", VideoQuality.High)],
            v.Quality, x => Save(s => s with { Quality = x }))));
        more.Children.Add(Labeled("Countdown", Segments("Countdown", "countdown",
            [("Off", "No countdown", 0), ("3 s", "3 seconds", 3), ("5 s", "5 seconds", 5), ("10 s", "10 seconds", 10)],
            v.Countdown ? v.CountdownSeconds : 0, x => Save(s => s with { Countdown = x > 0, CountdownSeconds = x > 0 ? x : s.CountdownSeconds }))));
        _cursorRow = new StackPanel();
        var cursor = new CheckBox { Content = "Show the mouse _pointer", IsChecked = v.CaptureCursor, Margin = new Thickness(0, 4, 0, 0) };
        cursor.Click += (_, _) => Save(s => s with { CaptureCursor = cursor.IsChecked == true });
        _cursorRow.Children.Add(cursor);
        more.Children.Add(_cursorRow);

        more.Children.Add(SubHeader("After recording"));
        var keep = new CheckBox { Content = "_Keep recordings in the Library", IsChecked = v.SaveToLibrary, Margin = new Thickness(0, 2, 0, 0), ToolTip = "Off = you choose where to save each recording when it stops (no history is kept)." };
        keep.Click += (_, _) => Save(s => s with { SaveToLibrary = keep.IsChecked == true });
        more.Children.Add(keep);
        var open = new CheckBox { Content = "Open in the _Editor after recording", IsChecked = v.OpenAfterRecording, Margin = new Thickness(0, 4, 0, 0), ToolTip = "Off = a small \"Recording saved\" message with Play, Open in Editor and Show in folder." };
        open.Click += (_, _) => Save(s => s with { OpenAfterRecording = open.IsChecked == true });
        more.Children.Add(open);

        var expander = new Expander { Header = "More options", Content = more, Margin = new Thickness(0, 12, 0, 0), IsExpanded = false };
        AutomationProperties.SetName(expander, "More recording options");
        root.Children.Add(expander);

        // ---- Footer
        string hint = string.Join("  ·  ", new[] { v.RecordHotkey is { } r ? $"{r} pause" : null, v.StopHotkey is { } st ? $"{st} stop" : null }.OfType<string>());
        var hintText = new TextBlock { Text = hint, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 76, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        var startContent = new StackPanel { Orientation = Orientation.Horizontal };
        startContent.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x52)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        startContent.Children.Add(new TextBlock { Text = "Start recording", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _start = new Button { Content = startContent, IsDefault = true, MinWidth = 150, Padding = new Thickness(14, 7, 14, 7) };
        _start.SetResourceReference(StyleProperty, "AccentButton");
        AutomationProperties.SetName(_start, "Start recording");
        _start.Click += (_, _) => RequestStart();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_start);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(hintText);
        root.Children.Add(footer);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => UpdateMeter(), Dispatcher);
        SourceInitialized += (_, _) =>
        {
            WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
            Excluded = ExcludeFromCapture && CaptureExclusion.Apply(this); // never part of a recording
        };
        Loaded += (_, _) =>
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            LoadCameras();
            UpdateMode();
            UpdateSwitch(_systemToggle);
            if (RecordingController.SessionCamera)
            {
                StartPreview(); // Camera was turned on earlier in this session
            }

            if (RecordingController.SessionMicrophone)
            {
                StartMeter();
            }

            LoadedWorkMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                StopPreview("The camera was turned off while the Record panel was minimized.");
            }
        };
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closed += (_, _) =>
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            StopMeter();
            StopPreview(null);
            if (!_starting)
            {
                _preview?.Close();
            }

            _preview = null;
        };
    }

    /// <summary>The user pressed Start: settings are saved and the camera and microphone are being released.</summary>
    public event EventHandler? StartRequested;

    /// <summary>True if Windows confirmed the window is excluded from screen capture.</summary>
    public bool Excluded { get; private set; }

    /// <summary>False only for the documentation screenshot tour (so the window can be photographed).</summary>
    internal bool ExcludeFromCapture { get; init; } = true;

    /// <summary>Diagnostics (DEF-RC2-07): how long the Loaded work blocked the UI thread, and the camera listing part of it (ms; −1 = not run yet).</summary>
    internal double LoadedWorkMs { get; private set; } = -1;

    /// <inheritdoc cref="LoadedWorkMs"/>
    internal double CameraListMs { get; private set; } = -1;

    /// <summary>Completes when the preview camera has been released (the recorder may then open it).</summary>
    public Task CameraReleased => _cameraReleased;

    /// <summary>The preview camera, if one is running (tests).</summary>
    internal ICameraSource? PreviewCamera => _camera;

    /// <summary>The floating camera preview, if Camera is on (tests, screenshots).</summary>
    internal CameraPreviewWindow? Preview => _preview;

    private VideoSettings Settings => _services.Settings.Video;

    /// <summary>
    /// At Start: hands the floating preview to the recording (it stays on screen while the recorder opens the camera),
    /// or null if Camera is off. After this the panel no longer closes it.
    /// </summary>
    internal CameraPreviewWindow? TakePreview()
    {
        CameraPreviewWindow? preview = _preview;
        _preview = null;
        preview?.SetSource(null);
        preview?.SetState(CameraPreviewWindow.Placement(RecordingController.Effective(Settings)), inRecording: true, placeholder: "Starting the recording…");
        return preview;
    }

    private void Save(Func<VideoSettings, VideoSettings> change)
    {
        _services.UpdateSettings(s => s with { Video = change(s.Video).Normalized() });
        UpdateCameraInfo();
    }

    // ---------------------------------------------------------------- switches

    private void SetCamera(bool on)
    {
        RecordingController.SessionCamera = on;
        if (on)
        {
            StartPreview(); // turning Camera on is the explicit action that opens the camera
        }
        else
        {
            if (_cameraOnly.IsChecked == true)
            {
                _cameraOnly.IsChecked = false;
                Save(s => s with { StudioMode = StudioMode.ScreenAndCamera });
            }

            StopPreview(null);
            _preview?.Close();
            _preview = null;
            HideCameraError();
        }

        UpdateMode();
    }

    private void SetMicrophone(bool on)
    {
        RecordingController.SessionMicrophone = on;
        if (on)
        {
            StartMeter();
        }
        else
        {
            StopMeter();
        }

        UpdateSwitch(_micToggle);
    }

    private void UpdateMode()
    {
        bool cameraOnly = RecordingController.SessionCamera && Settings.StudioMode == StudioMode.Camera;
        _targetRow.Visibility = cameraOnly ? Visibility.Collapsed : Visibility.Visible;
        _targetHeader.Text = cameraOnly ? "RECORDING THE CAMERA ONLY" : "WHAT TO RECORD";
        _cursorRow.Visibility = cameraOnly ? Visibility.Collapsed : Visibility.Visible;
        UpdateSwitch(_cameraToggle);
        UpdateSwitch(_micToggle);
        UpdateCameraInfo();
        UpdateStart();
    }

    private static void UpdateSwitch(ToggleButton toggle)
    {
        bool on = toggle.IsChecked == true;
        if (toggle.Tag is TextBlock state)
        {
            state.Text = on ? "On" : "Off";
        }

        AutomationProperties.SetItemStatus(toggle, on ? "On" : "Off");
    }

    private void UpdateCameraInfo()
    {
        bool on = RecordingController.SessionCamera;
        _cameraInfo.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            return;
        }

        VideoSettings v = RecordingController.Effective(Settings);
        string where = v.StudioMode == StudioMode.Camera
            ? "The recording will be the camera picture."
            : $"You will appear in the recording at the {CameraPreviewWindow.Corner(v.CameraCorner)} (change it in More options).";
        _cameraStatus.Text = (_camera is { IsRunning: true } ? "Camera is on. " : string.Empty) + where;
        _previewToggle.Content = _preview is { IsVisible: true } ? "Hide preview" : "Show preview";
        _previewToggle.Visibility = _preview is null ? Visibility.Collapsed : Visibility.Visible;
        if (_preview is { } p)
        {
            p.SetShape(v.CameraShape, v.CameraMirror);
            if (_camera is { IsRunning: true })
            {
                p.SetState(CameraPreviewWindow.Placement(v) + " (not recording yet)", inRecording: false);
            }
        }
    }

    private void UpdateStart()
    {
        bool needsCamera = RecordingController.SessionCamera && Settings.StudioMode == StudioMode.Camera;
        _start.IsEnabled = !needsCamera || _cameras.Count > 0;
        _start.ToolTip = _start.IsEnabled ? null : "No camera is connected. Connect one, or turn off \"Record the camera only\".";
    }

    // ---------------------------------------------------------------- camera

    private void LoadCameras()
    {
        try
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            _cameras = _listCameras(); // listing does not open a camera
            CameraListMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        catch (NativeCallException)
        {
            _cameras = [];
        }
        catch (DllNotFoundException)
        {
            _cameras = []; // no Media Foundation (Windows N without the Media Feature Pack)
        }
        catch (EntryPointNotFoundException)
        {
            _cameras = [];
        }

        Mf.VideoDevice? chosen = CurrentCamera();
        _cameraText.Text = chosen?.Name ?? (_cameras.Count == 0 ? "No camera found" : "Camera");
        _cameraButton.IsEnabled = _cameras.Count > 0;
        AutomationProperties.SetName(_cameraButton, "Camera device: " + _cameraText.Text);
    }

    private Mf.VideoDevice? CurrentCamera()
    {
        if (_cameras.Count == 0)
        {
            return null;
        }

        string? id = Settings.CameraDeviceId;
        return _cameras.FirstOrDefault(c => string.Equals(c.SymbolicLink, id, StringComparison.OrdinalIgnoreCase)) is { SymbolicLink.Length: > 0 } match ? match : _cameras[0];
    }

    private void ShowCameraMenu()
    {
        LoadCameras();
        var menu = new ContextMenu { PlacementTarget = _cameraButton, Placement = PlacementMode.Bottom };
        foreach (Mf.VideoDevice device in _cameras)
        {
            var item = new MenuItem { Header = device.Name, IsCheckable = true, IsChecked = device.SymbolicLink == CurrentCamera()?.SymbolicLink };
            item.Click += (_, _) =>
            {
                Save(s => s with { CameraDeviceId = device.SymbolicLink });
                _cameraText.Text = device.Name;
                AutomationProperties.SetName(_cameraButton, "Camera device: " + device.Name);
                if (RecordingController.SessionCamera)
                {
                    StopPreview(null);
                    StartPreview(); // choosing a device while Camera is on switches the preview to it
                }
            };
            menu.Items.Add(item);
        }

        _cameraButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private CameraPreviewWindow EnsurePreviewWindow()
    {
        if (_preview is { } existing)
        {
            return existing;
        }

        var preview = new CameraPreviewWindow(ExcludeFromCapture);
        preview.HideRequested += (_, _) =>
        {
            preview.Hide();
            RecordingController.PreviewHidden = true;
            UpdateCameraInfo();
        };
        _preview = preview;
        VideoSettings v = RecordingController.Effective(Settings);
        preview.SetShape(v.CameraShape, v.CameraMirror);
        if (!RecordingController.PreviewHidden)
        {
            preview.Show();
        }

        return preview;
    }

    private void TogglePreviewVisible()
    {
        if (_preview is not { } p)
        {
            return;
        }

        if (p.IsVisible)
        {
            p.Hide();
            RecordingController.PreviewHidden = true;
        }
        else
        {
            RecordingController.PreviewHidden = false;
            p.Show();
            p.KeepOnScreen();
        }

        UpdateCameraInfo();
    }

    private void StartPreview()
    {
        if (!RecordingController.SessionCamera || _camera is not null)
        {
            return;
        }

        LoadCameras();
        UpdateStart();
        if (CurrentCamera() is not { } device)
        {
            ShowCameraError("No camera was found. Connect a camera, then turn Camera off and on again — or record without it.", privacy: false);
            UpdateCameraInfo();
            return;
        }

        CameraPreviewWindow preview = EnsurePreviewWindow();
        if (PrivacySettings.CameraBlocked())
        {
            ShowCameraError(PrivacySettings.CameraBlockedMessage, privacy: true);
            preview.SetState("Camera blocked by Windows", inRecording: false, placeholder: "Windows is blocking camera access. Use the link in the Record panel to allow it.");
            UpdateCameraInfo();
            return;
        }

        HideCameraError();
        ICameraSource camera = _cameraFactory(device.SymbolicLink, device.Name);
        _camera = camera;
        int generation = ++_generation;
        preview.SetState("Camera preview", inRecording: false, placeholder: "Starting the camera…");
        Task previousRelease = _cameraReleased;
        _ = Task.Run(async () =>
        {
            await previousRelease.ConfigureAwait(false); // the device may still be closing from a previous preview
            camera.Start();
        }).ContinueWith(t => Dispatcher.BeginInvoke(() => OnPreviewStarted(camera, generation, t.Exception?.GetBaseException())), TaskScheduler.Default);
        UpdateCameraInfo();
    }

    private void OnPreviewStarted(ICameraSource camera, int generation, Exception? error)
    {
        if (generation != _generation || !ReferenceEquals(camera, _camera))
        {
            return; // superseded (Camera turned off, panel closed): StopPreview releases it
        }

        if (error is not null)
        {
            _camera = null;
            _cameraReleased = Task.Run(camera.Dispose);
            CameraException ce = error as CameraException ?? new CameraException(CameraFailure.Failed, "The camera could not be started: " + error.Message);
            ShowCameraError(ce.Message, privacy: ce.Kind == CameraFailure.AccessDenied);
            _preview?.SetState(
                ce.Kind == CameraFailure.Busy ? "Camera in use by another app" : "Camera not available",
                inRecording: false,
                placeholder: ce.Kind == CameraFailure.Busy ? "Close the other app that uses the camera, then turn Camera off and on again." : ce.Message);
            UpdateCameraInfo();
            return;
        }

        camera.Lost += (_, reason) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(camera, _camera))
            {
                StopPreview(null);
                ShowCameraError(reason, privacy: false);
                _preview?.SetState("Camera disconnected", inRecording: false, placeholder: reason);
            }
        });
        _preview?.SetSource(camera);
        UpdateCameraInfo();
    }

    /// <summary>Releases the preview camera (on a worker thread: the camera light goes off within a moment).</summary>
    private void StopPreview(string? message)
    {
        _generation++;
        if (_camera is { } camera)
        {
            _camera = null;
            _cameraReleased = Task.Run(camera.Dispose);
        }

        _preview?.SetSource(null);
        if (message is not null)
        {
            _preview?.SetState("Camera off", inRecording: false, placeholder: message);
        }

        UpdateCameraInfo();
    }

    private void ShowCameraError(string text, bool privacy)
    {
        _cameraError.Text = text;
        _cameraError.Visibility = Visibility.Visible;
        _cameraPrivacy.Visibility = privacy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideCameraError()
    {
        _cameraError.Visibility = Visibility.Collapsed;
        _cameraPrivacy.Visibility = Visibility.Collapsed;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            Dispatcher.BeginInvoke(() =>
            {
                StopPreview("The camera was turned off because the PC was locked. Turn Camera off and on again to restart it.");
                StopMeter();
            });
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Dispatcher.Invoke(() =>
            {
                StopPreview("The camera was turned off because the PC went to sleep.");
                StopMeter();
            });
        }
    }

    // ---------------------------------------------------------------- microphone

    private void StartMeter()
    {
        _micInfo.Visibility = Visibility.Visible;
        bool blocked = PrivacySettings.MicrophoneBlocked();
        _micError.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        _micPrivacy.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        if (blocked || _meter is not null)
        {
            return;
        }

        _meter = new MicrophoneMeter(Settings.MicrophoneDeviceId);
        _meterTimer.Start();
    }

    private void StopMeter()
    {
        _meterTimer.Stop();
        MicrophoneMeter? meter = _meter;
        _meter = null;
        if (meter is not null)
        {
            _ = Task.Run(meter.Dispose); // microphone released (indicator off)
        }

        _micLevel.Value = 0;
        _micInfo.Visibility = RecordingController.SessionMicrophone ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMeter()
    {
        if (_meter is not { } m)
        {
            return;
        }

        _micLevel.Value = Math.Sqrt(m.Level) * 100;
        if (m.AccessDenied)
        {
            _micError.Visibility = Visibility.Visible;
            _micPrivacy.Visibility = Visibility.Visible;
        }

        _micStatus.Text = m.DeviceName is { } name
            ? $"{name} — say something; the bar moves when the microphone hears you."
            : "Looking for a microphone… (connect one, or check the device under More options)";
    }

    private void SetUpMicrophoneMenu(TextBlock micText)
    {
        IReadOnlyList<CoreAudio.Endpoint> endpoints;
        try
        {
            endpoints = CoreAudio.ListEndpoints(CoreAudio.eCapture); // listing does not open a microphone
        }
        catch (NativeCallException)
        {
            endpoints = [];
        }

        var choices = new List<(string? Id, string Name)> { (null, "Windows default microphone") };
        choices.AddRange(endpoints.Select(e => ((string?)e.Id, e.Name)));
        micText.Text = choices.FirstOrDefault(c => c.Id == Settings.MicrophoneDeviceId).Name ?? choices[0].Name;
        AutomationProperties.SetName(_micButton, "Microphone device: " + micText.Text);
        _micButton.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = _micButton, Placement = PlacementMode.Bottom };
            foreach ((string? id, string name) in choices)
            {
                var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = id == Settings.MicrophoneDeviceId };
                item.Click += (_, _) =>
                {
                    Save(s => s with { MicrophoneDeviceId = id });
                    micText.Text = name;
                    AutomationProperties.SetName(_micButton, "Microphone device: " + name);
                    if (_meter is not null)
                    {
                        StopMeter();
                        StartMeter(); // listen to the newly chosen microphone
                    }
                };
                menu.Items.Add(item);
            }

            _micButton.ContextMenu = menu;
            menu.IsOpen = true;
        };
    }

    // ---------------------------------------------------------------- start

    private void RequestStart()
    {
        if (RecordingController.SessionCamera && PrivacySettings.CameraBlocked())
        {
            ShowCameraError(PrivacySettings.CameraBlockedMessage, privacy: true);
            return;
        }

        _starting = true;
        StopMeter(); // the recorder opens the microphone itself
        StopPreview(null); // and the camera, once the area is chosen
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- building blocks

    private static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "SectionHeader");
        return t;
    }

    private static TextBlock SubHeader(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 10, 0, 2) };

    private static ToggleButton Switch(string label, Geometry icon, bool on, string name)
    {
        var path = new Path { Data = icon, Width = 22, Height = 22, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        path.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding(nameof(Foreground)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1) });
        var text = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var state = new TextBlock { Text = on ? "On" : "Off", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.8 };
        var stack = new StackPanel();
        stack.Children.Add(path);
        stack.Children.Add(text);
        stack.Children.Add(state);
        var toggle = new ToggleButton { Content = stack, IsChecked = on, Tag = state, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(6, 8, 6, 6), MinHeight = 74, HorizontalContentAlignment = HorizontalAlignment.Center };
        toggle.SetResourceReference(StyleProperty, "ToolbarToggle");
        AutomationProperties.SetName(toggle, name);
        AutomationProperties.SetItemStatus(toggle, on ? "On" : "Off");
        return toggle;
    }

    private static Grid Labeled(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        g.Children.Add(text);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    /// <summary>A row of segment buttons (one radio group); arrow keys move between them, Space/Enter chooses.</summary>
    private static Border Segments<T>(string groupName, string group, (string Text, string Name, T Value)[] items, T current, Action<T> chosen, bool stretch = false)
    {
        Panel row = stretch ? new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
        KeyboardNavigation.SetDirectionalNavigation(row, KeyboardNavigationMode.Cycle);
        string groupId = group + "-" + Guid.NewGuid().ToString("N");
        foreach ((string text, string name, T value) in items)
        {
            var b = new RadioButton { Content = text, GroupName = groupId, IsChecked = EqualityComparer<T>.Default.Equals(value, current), MinWidth = 44 };
            b.SetResourceReference(StyleProperty, "SegmentButton");
            if (stretch)
            {
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
                b.Padding = new Thickness(8, 6, 8, 6);
            }

            AutomationProperties.SetName(b, $"{groupName}: {name}");
            b.Checked += (_, _) => chosen(value);
            row.Children.Add(b);
        }

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(2),
            HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            Child = row,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        AutomationProperties.SetName(border, groupName);
        return border;
    }

    private static Button Dropdown(TextBlock text, string name)
    {
        var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.SetResourceReference(StyleProperty, "DropdownButton");
        var panel = new DockPanel();
        var chevron = new Path { Width = 14, Height = 14, Stretch = Stretch.Uniform };
        chevron.SetResourceReference(StyleProperty, "IconPath");
        chevron.SetResourceReference(Path.DataProperty, "IconChevronDown");
        DockPanel.SetDock(chevron, Dock.Right);
        panel.Children.Add(chevron);
        panel.Children.Add(text);
        b.Content = panel;
        AutomationProperties.SetName(b, name);
        return b;
    }

    private static Button Link(string text, Action click)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(-6, 0, 0, 4) };
        b.SetResourceReference(StyleProperty, "LinkButton");
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        return b;
    }
}
