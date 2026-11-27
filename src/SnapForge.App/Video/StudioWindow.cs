using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
/// SnapForge Studio (SF-006): the recording set-up window — what to record (screen, screen + camera, camera), the
/// capture area, the camera and its overlay (shape, size, corner, mirror) with a live preview, sound, and video
/// options. Every choice is saved in <see cref="VideoSettings"/> as it is made. The camera is opened only while a
/// camera mode is selected and the preview is on; closing the window, choosing Screen only, locking the PC or
/// starting the recording releases it. The window is excluded from screen capture.
/// </summary>
internal sealed class StudioWindow : Window
{
    private const double PreviewWidth = 384;
    private const double PreviewHeight = 216;

    private readonly AppServices _services;
    private readonly Func<string?, string, ICameraSource> _cameraFactory;
    private readonly StackPanel _targetSection;
    private readonly StackPanel _cameraSection;
    private readonly StackPanel _cursorRow;
    private readonly TextBlock _cameraText;
    private readonly Button _cameraButton;
    private readonly Canvas _preview;
    private readonly Border _screenMock;
    private readonly Image _cameraImage;
    private readonly TextBlock _previewStatus;
    private readonly Button _previewButton;
    private readonly TextBlock _cameraError;
    private readonly Button _cameraPrivacy;
    private readonly TextBlock _micError;
    private readonly Button _micPrivacy;
    private readonly Button _micButton;
    private readonly Button _start;
    private readonly DispatcherTimer _frameTimer;
    private IReadOnlyList<Mf.VideoDevice> _cameras = [];
    private ICameraSource? _camera;
    private WriteableBitmap? _bitmap;
    private long _shownSequence = -1;
    private int _generation;
    private Task _cameraReleased = Task.CompletedTask;

    public StudioWindow(AppServices services, Func<string?, string, ICameraSource>? cameraFactory = null)
    {
        _services = services;
        _cameraFactory = cameraFactory ?? ((id, name) => new MediaFoundationCameraSource(id, name));
        Title = "SnapForge Studio";
        Width = 448;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)FindResource("UiFont");
        AutomationProperties.SetName(this, "SnapForge Studio recording setup");

        VideoSettings v = Settings;
        var root = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };

        var heading = new TextBlock { Text = "SnapForge Studio", FontSize = 18, FontWeight = FontWeights.SemiBold };
        var sub = new TextBlock { Text = "Set up your recording. Choices are remembered.", FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        root.Children.Add(heading);
        root.Children.Add(sub);

        // ---- What to record
        root.Children.Add(Header("RECORD"));
        root.Children.Add(Segments("Record", "mode",
            [("Screen", "Screen only", StudioMode.Screen), ("Screen + camera", "Screen and camera", StudioMode.ScreenAndCamera), ("Camera", "Camera only", StudioMode.Camera)],
            v.StudioMode, m => Save(s => s with { StudioMode = m }, modeChanged: true)));

        // ---- Capture area
        _targetSection = new StackPanel();
        _targetSection.Children.Add(Header("CAPTURE AREA"));
        _targetSection.Children.Add(Segments("Capture area", "target",
            [("Region", "Select a region", RecordingSource.Region), ("Window", "A window", RecordingSource.Window), ("Display", "A whole display", RecordingSource.Display)],
            v.Source, t => Save(s => s with { Source = t })));
        root.Children.Add(_targetSection);

        // ---- Camera
        _cameraSection = new StackPanel();
        _cameraSection.Children.Add(Header("CAMERA"));
        _cameraText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _cameraButton = Dropdown(_cameraText, "Camera device");
        _cameraButton.Click += (_, _) => ShowCameraMenu();
        _cameraSection.Children.Add(_cameraButton);

        _screenMock = new Border { Width = PreviewWidth, Height = PreviewHeight, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        _screenMock.SetResourceReference(Border.BackgroundProperty, "CanvasBackdropBrush");
        _screenMock.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _cameraImage = new Image { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5) };
        AutomationProperties.SetName(_cameraImage, "Camera preview");
        _previewStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Width = PreviewWidth - 40, FontSize = 12 };
        _previewStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        _previewButton = new Button { Content = "Show camera _preview", Padding = new Thickness(10, 4, 10, 4) };
        _previewButton.SetResourceReference(StyleProperty, "ToolbarButton");
        AutomationProperties.SetName(_previewButton, "Turn on the camera preview");
        _previewButton.Click += (_, _) => StartPreview();
        _preview = new Canvas { Width = PreviewWidth, Height = PreviewHeight, Margin = new Thickness(0, 8, 0, 6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        _preview.Children.Add(_screenMock);
        _preview.Children.Add(_cameraImage);
        _preview.Children.Add(_previewStatus);
        _preview.Children.Add(_previewButton);
        _cameraSection.Children.Add(_preview);

        _cameraError = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
        _cameraError.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        _cameraPrivacy = Link("Open camera privacy settings", () => PrivacySettings.Open(PrivacySettings.CameraUri));
        _cameraSection.Children.Add(_cameraError);
        _cameraSection.Children.Add(_cameraPrivacy);

        _cameraSection.Children.Add(Labeled("Shape", Segments("Camera shape", "shape",
            [("Circle", "Circle", CameraOverlayShape.Circle), ("Rounded", "Rounded rectangle", CameraOverlayShape.RoundedRectangle)],
            v.CameraShape, x => Save(s => s with { CameraShape = x }))));
        _cameraSection.Children.Add(Labeled("Size", Segments("Camera size", "size",
            [("S", "Small", CameraOverlaySize.Small), ("M", "Medium", CameraOverlaySize.Medium), ("L", "Large", CameraOverlaySize.Large)],
            v.CameraSize, x => Save(s => s with { CameraSize = x }))));
        _cameraSection.Children.Add(Labeled("Position", Segments("Camera position", "corner",
            [("↖", "Top left", CameraCorner.TopLeft), ("↗", "Top right", CameraCorner.TopRight), ("↙", "Bottom left", CameraCorner.BottomLeft), ("↘", "Bottom right", CameraCorner.BottomRight)],
            v.CameraCorner, x => Save(s => s with { CameraCorner = x }))));
        var mirror = new CheckBox { Content = "_Mirror the camera picture", IsChecked = v.CameraMirror, Margin = new Thickness(0, 4, 0, 0) };
        mirror.Click += (_, _) => Save(s => s with { CameraMirror = mirror.IsChecked == true });
        _cameraSection.Children.Add(mirror);
        root.Children.Add(_cameraSection);

        // ---- Sound
        root.Children.Add(Header("SOUND"));
        var system = new CheckBox { Content = "_System audio (what you hear)", IsChecked = v.RecordSystemAudio };
        system.Click += (_, _) => Save(s => s with { RecordSystemAudio = system.IsChecked == true });
        root.Children.Add(system);
        var micText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _micButton = Dropdown(micText, "Microphone device");
        var mic = new CheckBox { Content = "Mi_crophone", IsChecked = v.RecordMicrophone, Margin = new Thickness(0, 6, 0, 0) };
        _micError = new TextBlock { Text = PrivacySettings.MicrophoneBlockedMessage, TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
        _micError.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        _micPrivacy = Link("Open microphone privacy settings", () => PrivacySettings.Open(PrivacySettings.MicrophoneUri));
        mic.Click += (_, _) =>
        {
            Save(s => s with { RecordMicrophone = mic.IsChecked == true });
            UpdateMicrophone(mic.IsChecked == true);
        };
        SetUpMicrophoneMenu(micText);
        root.Children.Add(mic);
        root.Children.Add(_micButton);
        root.Children.Add(_micError);
        root.Children.Add(_micPrivacy);

        // ---- Video
        root.Children.Add(Header("VIDEO"));
        root.Children.Add(Labeled("Frame rate", Segments("Frame rate", "fps",
            [("30 FPS", "30 frames per second", 30), ("60 FPS", "60 frames per second", 60)],
            v.FramesPerSecond, x => Save(s => s with { FramesPerSecond = x }))));
        root.Children.Add(Labeled("Quality", Segments("Quality", "quality",
            [("Standard", "Standard quality", VideoQuality.Standard), ("High", "High quality (larger files)", VideoQuality.High)],
            v.Quality, x => Save(s => s with { Quality = x }))));
        root.Children.Add(Labeled("Countdown", Segments("Countdown", "countdown",
            [("Off", "No countdown", 0), ("3 s", "3 seconds", 3), ("5 s", "5 seconds", 5), ("10 s", "10 seconds", 10)],
            v.Countdown ? v.CountdownSeconds : 0, x => Save(s => s with { Countdown = x > 0, CountdownSeconds = x > 0 ? x : s.CountdownSeconds }))));
        _cursorRow = new StackPanel();
        var cursor = new CheckBox { Content = "Show the mouse _pointer", IsChecked = v.CaptureCursor, Margin = new Thickness(0, 4, 0, 0) };
        cursor.Click += (_, _) => Save(s => s with { CaptureCursor = cursor.IsChecked == true });
        _cursorRow.Children.Add(cursor);
        root.Children.Add(_cursorRow);
        var keep = new CheckBox { Content = "_Keep recordings in the Library", IsChecked = v.SaveToLibrary, Margin = new Thickness(0, 4, 0, 0), ToolTip = "Off = you choose where to save each recording when it stops (no history is kept)." };
        keep.Click += (_, _) => Save(s => s with { SaveToLibrary = keep.IsChecked == true });
        root.Children.Add(keep);
        var open = new CheckBox { Content = "_Open the recording when it is saved", IsChecked = v.OpenAfterRecording, Margin = new Thickness(0, 4, 0, 0) };
        open.Click += (_, _) => Save(s => s with { OpenAfterRecording = open.IsChecked == true });
        root.Children.Add(open);

        // ---- Footer
        string hint = string.Join("  ·  ", new[] { v.RecordHotkey is { } r ? $"{r} pause/resume" : null, v.StopHotkey is { } st ? $"{st} stop" : null }.OfType<string>());
        var hintText = new TextBlock { Text = hint, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        _start = new Button { Content = "_Start recording", IsDefault = true, MinWidth = 132, Padding = new Thickness(14, 5, 14, 5) };
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

        _frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => ShowFrame(), Dispatcher);
        SourceInitialized += (_, _) =>
        {
            WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
            Excluded = CaptureExclusion.Apply(this); // never part of a recording
        };
        Loaded += (_, _) =>
        {
            LoadCameras();
            UpdateMode();
            UpdateMicrophone(v.RecordMicrophone);
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                StopPreview("The camera preview was turned off while Studio was minimized.");
            }
        };
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closed += (_, _) =>
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _frameTimer.Stop();
            StopPreview(null);
        };
    }

    /// <summary>The user pressed Start: settings are saved and the camera is being released.</summary>
    public event EventHandler? StartRequested;

    /// <summary>True if Windows confirmed the window is excluded from screen capture.</summary>
    public bool Excluded { get; private set; }

    /// <summary>Completes when the preview camera has been released (the recorder may then open it).</summary>
    public Task CameraReleased => _cameraReleased;

    /// <summary>The preview camera, if one is running (tests).</summary>
    internal ICameraSource? PreviewCamera => _camera;

    private VideoSettings Settings => _services.Settings.Video;

    private void Save(Func<VideoSettings, VideoSettings> change, bool modeChanged = false)
    {
        _services.UpdateSettings(s => s with { Video = change(s.Video).Normalized() });
        if (modeChanged)
        {
            UpdateMode(userChoseCamera: true);
        }
        else
        {
            UpdatePreviewLayout();
        }
    }

    // ---------------------------------------------------------------- mode, preview

    private void UpdateMode(bool userChoseCamera = false)
    {
        StudioMode mode = Settings.StudioMode;
        bool camera = mode != StudioMode.Screen;
        _cameraSection.Visibility = camera ? Visibility.Visible : Visibility.Collapsed;
        _targetSection.Visibility = mode == StudioMode.Camera ? Visibility.Collapsed : Visibility.Visible;
        _cursorRow.Visibility = mode == StudioMode.Camera ? Visibility.Collapsed : Visibility.Visible;
        if (!camera)
        {
            StopPreview(null); // Screen only: the camera is never open
        }
        else if (userChoseCamera && _camera is null)
        {
            StartPreview(); // choosing a camera mode is the explicit action that turns the camera on
        }
        else if (_camera is null)
        {
            ShowPreviewOff("Camera preview is off.");
        }

        UpdatePreviewLayout();
        UpdateStart();
    }

    private void UpdateStart()
    {
        bool needsCamera = Settings.StudioMode != StudioMode.Screen;
        _start.IsEnabled = !needsCamera || _cameras.Count > 0;
        _start.ToolTip = _start.IsEnabled ? null : "No camera is connected. Connect one, or choose Screen.";
    }

    private void LoadCameras()
    {
        try
        {
            _cameras = MediaFoundationCameraSource.Enumerate(); // listing does not open a camera
        }
        catch (NativeCallException)
        {
            _cameras = [];
        }
        catch (DllNotFoundException)
        {
            _cameras = [];
        }

        Mf.VideoDevice? chosen = CurrentCamera();
        _cameraText.Text = chosen?.Name ?? (_cameras.Count == 0 ? "No camera found" : "Camera");
        _cameraButton.IsEnabled = _cameras.Count > 0;
        AutomationProperties.SetName(_cameraButton, "Camera device: " + _cameraText.Text);
        if (_cameras.Count == 0)
        {
            ShowCameraError("No camera was found. Connect a camera, or choose Screen.", privacy: false);
        }
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
                StopPreview(null);
                StartPreview(); // choosing a device is an explicit action
            };
            menu.Items.Add(item);
        }

        _cameraButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void StartPreview()
    {
        if (Settings.StudioMode == StudioMode.Screen || _camera is not null)
        {
            return;
        }

        LoadCameras();
        if (CurrentCamera() is not { } device)
        {
            UpdateStart();
            return;
        }

        if (PrivacySettings.CameraBlocked())
        {
            ShowCameraError(PrivacySettings.CameraBlockedMessage, privacy: true);
            ShowPreviewOff("Camera access is blocked by Windows.");
            return;
        }

        HideCameraError();
        ICameraSource camera = _cameraFactory(device.SymbolicLink, device.Name);
        _camera = camera;
        int generation = ++_generation;
        ShowPreviewOff("Starting the camera…", button: false);
        Task previousRelease = _cameraReleased;
        _ = Task.Run(async () =>
        {
            await previousRelease.ConfigureAwait(false); // the device may still be closing from a previous preview
            camera.Start();
        }).ContinueWith(t => Dispatcher.BeginInvoke(() => OnPreviewStarted(camera, generation, t.Exception?.GetBaseException())), TaskScheduler.Default);
    }

    private void OnPreviewStarted(ICameraSource camera, int generation, Exception? error)
    {
        if (generation != _generation || !ReferenceEquals(camera, _camera))
        {
            return; // superseded (mode changed, window closed): StopPreview releases it
        }

        if (error is not null)
        {
            _camera = null;
            _cameraReleased = Task.Run(camera.Dispose);
            CameraException ce = error as CameraException ?? new CameraException(CameraFailure.Failed, "The camera could not be started: " + error.Message);
            ShowCameraError(ce.Message, privacy: ce.Kind == CameraFailure.AccessDenied);
            ShowPreviewOff(ce.Kind == CameraFailure.Busy ? "The camera is in use by another app." : "The camera is not available.", button: true, buttonText: "_Try again");
            return;
        }

        camera.Lost += (_, reason) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(camera, _camera))
            {
                StopPreview(null);
                ShowCameraError(reason, privacy: false);
            }
        });
        _previewStatus.Visibility = Visibility.Collapsed;
        _previewButton.Visibility = Visibility.Collapsed;
        _shownSequence = -1;
        _frameTimer.Start();
    }

    /// <summary>Releases the preview camera (on a worker thread: the camera light goes off within a moment).</summary>
    private void StopPreview(string? message)
    {
        _generation++;
        _frameTimer.Stop();
        if (_camera is { } camera)
        {
            _camera = null;
            _cameraReleased = Task.Run(camera.Dispose);
        }

        _cameraImage.Source = null;
        _bitmap = null;
        if (Settings.StudioMode != StudioMode.Screen)
        {
            ShowPreviewOff(message ?? "Camera preview is off.");
        }
    }

    private void ShowPreviewOff(string text, bool button = true, string buttonText = "Show camera _preview")
    {
        _cameraImage.Source = null;
        _previewStatus.Text = text;
        _previewStatus.Visibility = Visibility.Visible;
        _previewButton.Content = buttonText;
        _previewButton.Visibility = button && _cameras.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_previewStatus, 20);
        Canvas.SetTop(_previewStatus, (PreviewHeight / 2) - 34);
        _previewButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_previewButton, (PreviewWidth - _previewButton.DesiredSize.Width) / 2);
        Canvas.SetTop(_previewButton, (PreviewHeight / 2) + 4);
    }

    private unsafe void ShowFrame()
    {
        if (_camera is not { IsRunning: true } camera || camera.FrameSequence == _shownSequence)
        {
            return;
        }

        _shownSequence = camera.FrameSequence;
        camera.WithFrame(long.MaxValue, (bgra, w, h, _) =>
        {
            if (_bitmap is null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                _cameraImage.Source = _bitmap;
                UpdatePreviewLayout();
            }

            fixed (byte* p = bgra)
            {
                _bitmap.WritePixels(new Int32Rect(0, 0, w, h), (nint)p, w * h * 4, w * 4);
            }
        });
    }

    /// <summary>Lays the preview out like the recording: camera only fills the box; Screen + camera shows the overlay in its corner.</summary>
    private void UpdatePreviewLayout()
    {
        VideoSettings v = Settings;
        _cameraImage.RenderTransform = new ScaleTransform(v.CameraMirror ? -1 : 1, 1);
        if (v.StudioMode == StudioMode.Camera)
        {
            _cameraImage.Width = PreviewWidth;
            _cameraImage.Height = PreviewHeight;
            _cameraImage.Stretch = Stretch.Uniform;
            _cameraImage.Clip = null;
            Canvas.SetLeft(_cameraImage, 0);
            Canvas.SetTop(_cameraImage, 0);
            return;
        }

        OverlayLayout layout = OverlayLayout.For((int)PreviewWidth, (int)PreviewHeight, v.CameraShape, v.CameraSize, v.CameraCorner, v.CameraMirror);
        _cameraImage.Stretch = Stretch.UniformToFill;
        _cameraImage.Width = layout.Width;
        _cameraImage.Height = layout.Height;
        _cameraImage.Clip = v.CameraShape == CameraOverlayShape.Circle
            ? new EllipseGeometry(new Rect(0, 0, layout.Width, layout.Height))
            : new RectangleGeometry(new Rect(0, 0, layout.Width, layout.Height), layout.CornerRadius, layout.CornerRadius);
        Canvas.SetLeft(_cameraImage, layout.X);
        Canvas.SetTop(_cameraImage, layout.Y);
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
            Dispatcher.BeginInvoke(() => StopPreview("The camera preview was turned off because the PC was locked."));
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Dispatcher.Invoke(() => StopPreview("The camera preview was turned off because the PC went to sleep."));
        }
    }

    // ---------------------------------------------------------------- microphone

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
                };
                menu.Items.Add(item);
            }

            _micButton.ContextMenu = menu;
            menu.IsOpen = true;
        };
    }

    private void UpdateMicrophone(bool on)
    {
        _micButton.IsEnabled = on;
        bool blocked = on && PrivacySettings.MicrophoneBlocked();
        _micError.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        _micPrivacy.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- start

    private void RequestStart()
    {
        if (Settings.StudioMode != StudioMode.Screen && PrivacySettings.CameraBlocked())
        {
            ShowCameraError(PrivacySettings.CameraBlockedMessage, privacy: true);
            return;
        }

        StopPreview(null); // the recorder opens the camera itself once the area is chosen
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- building blocks

    private static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "SectionHeader");
        return t;
    }

    private static Grid Labeled(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        g.Children.Add(text);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    /// <summary>A row of segment buttons (one radio group); arrow keys move between them, Space/Enter chooses.</summary>
    private static Border Segments<T>(string groupName, string group, (string Text, string Name, T Value)[] items, T current, Action<T> chosen)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        KeyboardNavigation.SetDirectionalNavigation(row, KeyboardNavigationMode.Cycle);
        string groupId = group + "-" + Guid.NewGuid().ToString("N");
        foreach ((string text, string name, T value) in items)
        {
            var b = new RadioButton { Content = text, GroupName = groupId, IsChecked = EqualityComparer<T>.Default.Equals(value, current), MinWidth = 44 };
            b.SetResourceReference(StyleProperty, "SegmentButton");
            AutomationProperties.SetName(b, $"{groupName}: {name}");
            b.Checked += (_, _) => chosen(value);
            row.Children.Add(b);
        }

        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        AutomationProperties.SetName(border, groupName);
        return border;
    }

    private static Button Dropdown(TextBlock text, string name)
    {
        var b = new Button { MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
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
