using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Geometry;
using SnapForge.Core.Settings;

namespace SnapForge.App.Video;

/// <summary>
/// RC3: the floating camera preview — "see yourself while you talk". Shown as soon as Camera is turned on in the Record
/// panel and kept on screen for the whole recording. It is topmost, never takes focus, can be dragged anywhere and is
/// always kept inside the usable area of a monitor (at any Windows scaling, after display changes). It is EXCLUDED from
/// screen capture, so even when it sits over the recorded area it never appears in the video: the camera is composed
/// into the recording exactly once, by the recorder, at the position chosen in the Record panel. The caption says
/// whether the camera is on and whether — and where — it will be in the recording. The preview only shows frames of a
/// camera it is given; it never opens or releases a camera itself.
/// </summary>
internal sealed class CameraPreviewWindow : Window
{
    private const int MarginPx = 24;

    /// <summary>Where the preview was last left in this session (physical pixels), so it reopens there.</summary>
    private static PixelRect? s_lastBounds;

    private readonly Grid _picture;
    private readonly Image _image;
    private readonly TextBlock _placeholder;
    private readonly TextBlock _caption;
    private readonly System.Windows.Shapes.Ellipse _dot;
    private readonly DispatcherTimer _timer;
    private ICameraSource? _source;
    private WriteableBitmap? _bitmap;
    private long _shown = -1;
    private CameraOverlayShape _shape = (CameraOverlayShape)(-1);

    /// <param name="excludeFromCapture">Always true in the app; false only for documentation screenshots.</param>
    public CameraPreviewWindow(bool excludeFromCapture = true)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Title = "SnapForge camera preview";
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        SetResourceReference(ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(this, "Camera preview");

        _image = new Image { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5) };
        AutomationProperties.SetName(_image, "Camera picture");
        _placeholder = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14), FontSize = 12 };
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        _picture = new Grid { ClipToBounds = true };
        _picture.SetResourceReference(Panel.BackgroundProperty, "CanvasBackdropBrush");
        _picture.Children.Add(_image);
        _picture.Children.Add(_placeholder);

        _dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(2, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        _caption = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 210 };
        AutomationProperties.SetName(_caption, "Camera status");
        var hide = new Button { Content = "Hide", Padding = new Thickness(8, 1, 8, 1), Focusable = false, ToolTip = "Hide this preview. What is recorded does not change; show it again from the recording controls or the notification-area icon." };
        hide.SetResourceReference(StyleProperty, "ToolbarButton");
        AutomationProperties.SetName(hide, "Hide the camera preview");
        hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        var captionRow = new DockPanel { Margin = new Thickness(6, 6, 2, 2), LastChildFill = true };
        DockPanel.SetDock(hide, Dock.Right);
        captionRow.Children.Add(hide);
        captionRow.Children.Add(_dot);
        captionRow.Children.Add(_caption);

        var stack = new StackPanel();
        stack.Children.Add(_picture);
        stack.Children.Add(captionRow);
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Child = stack, Cursor = Cursors.SizeAll };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = border;

        SetShape(CameraOverlayShape.Circle, mirror: true);
        SetState("Camera preview", inRecording: false, placeholder: "Starting the camera…");

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => ShowFrame(), Dispatcher);
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not ButtonBase)
            {
                DragMove();
                KeepOnScreen();
            }
        };
        SourceInitialized += (_, _) =>
        {
            CaptureExclusion.MakeNonActivating(this);
            Excluded = excludeFromCapture && CaptureExclusion.Apply(this);
        };
        Loaded += (_, _) => PlaceInitially();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(KeepOnScreen, DispatcherPriority.Background);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _timer.Stop();
            _source = null;
        };
    }

    /// <summary>The user pressed Hide on the preview.</summary>
    public event EventHandler? HideRequested;

    /// <summary>True if Windows confirmed the preview is excluded from screen capture.</summary>
    public bool Excluded { get; private set; }

    /// <summary>The camera whose frames are shown (not owned), or null.</summary>
    public ICameraSource? Source => _source;

    /// <summary>Number of camera frames drawn so far (self-test evidence that the preview is live).</summary>
    public int FramesShown { get; private set; }

    /// <summary>Shows the frames of <paramref name="camera"/> (or nothing). The preview never opens or releases it.</summary>
    public void SetSource(ICameraSource? camera)
    {
        _source = camera;
        _shown = -1;
        if (camera is null)
        {
            _timer.Stop();
            _image.Source = null;
            _bitmap = null;
        }
        else
        {
            _timer.Start();
        }
    }

    /// <summary>Same shape and mirroring as the overlay in the recording.</summary>
    public void SetShape(CameraOverlayShape shape, bool mirror)
    {
        _image.RenderTransform = new ScaleTransform(mirror ? -1 : 1, 1);
        if (shape == _shape)
        {
            return;
        }

        _shape = shape;
        (double w, double h) = shape == CameraOverlayShape.Circle ? (176.0, 176.0) : (232.0, 131.0);
        _picture.Width = w;
        _picture.Height = h;
        _picture.Clip = shape == CameraOverlayShape.Circle
            ? new EllipseGeometry(new Rect(0, 0, w, h))
            : new RectangleGeometry(new Rect(0, 0, w, h), 12, 12);
        _caption.MaxWidth = Math.Max(150, w - 50);
        if (IsLoaded)
        {
            Dispatcher.BeginInvoke(KeepOnScreen, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Caption (what happens to the camera in the recording) and, when no live picture is shown, the text in its place.
    /// </summary>
    /// <param name="caption">For example "In the recording · bottom right".</param>
    /// <param name="inRecording">True: the camera is (or will be) in the saved video — red dot; false: grey dot.</param>
    /// <param name="placeholder">Shown instead of the picture (camera off, paused, starting); null shows the live picture.</param>
    public void SetState(string caption, bool inRecording, string? placeholder = null)
    {
        _caption.Text = caption;
        _dot.Fill = inRecording ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98));
        _placeholder.Text = placeholder ?? string.Empty;
        _placeholder.Visibility = placeholder is null ? Visibility.Collapsed : Visibility.Visible;
        _image.Visibility = placeholder is null ? Visibility.Visible : Visibility.Hidden;
        AutomationProperties.SetHelpText(this, placeholder is null ? caption : caption + ". " + placeholder);
    }

    /// <summary>Text currently shown (caption and placeholder), for self-tests and screenshots.</summary>
    public string Describe() => _placeholder.Visibility == Visibility.Visible ? $"{_caption.Text} | {_placeholder.Text}" : _caption.Text;

    /// <summary>Moves the preview fully onto the usable area of the monitor it is (mostly) on.</summary>
    public void KeepOnScreen()
    {
        if (PhysicalPlacement.GetBounds(this) is not { } b)
        {
            return;
        }

        PixelRect fitted = WindowFit.KeepOnScreen(b, PhysicalPlacement.Layout().Monitors.Select(m => m.WorkArea).ToList());
        if (fitted != b)
        {
            PhysicalPlacement.SetBounds(this, fitted, resize: false);
        }

        s_lastBounds = fitted;
    }

    private void PlaceInitially()
    {
        UpdateLayout();
        if (PhysicalPlacement.GetBounds(this) is not { } b)
        {
            return;
        }

        PixelRect target;
        if (s_lastBounds is { } last)
        {
            target = new PixelRect(last.X, last.Y, b.Width, b.Height);
        }
        else
        {
            Core.Capture.ScreenLayout layout = PhysicalPlacement.Layout();
            Core.Capture.MonitorInfo primary = layout.Monitors.FirstOrDefault(m => m.IsPrimary) ?? layout.Monitors[0];
            target = WindowFit.BottomRight(primary.WorkArea, b.Width, b.Height, MarginPx);
        }

        PhysicalPlacement.SetBounds(this, target, resize: false);
        KeepOnScreen();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(KeepOnScreen, DispatcherPriority.Background);

    private unsafe void ShowFrame()
    {
        if (_source is not { IsRunning: true } camera || camera.FrameSequence == _shown)
        {
            return;
        }

        _shown = camera.FrameSequence;
        bool drawn = camera.WithFrame(long.MaxValue, (bgra, w, h, _) =>
        {
            if (_bitmap is null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                _image.Source = _bitmap;
            }

            fixed (byte* p = bgra)
            {
                _bitmap.WritePixels(new Int32Rect(0, 0, w, h), (nint)p, w * h * 4, w * 4);
            }
        });
        if (drawn)
        {
            FramesShown++;
        }
    }

    /// <summary>Caption for the preview: whether and where the camera appears in the recording.</summary>
    internal static string Placement(VideoSettings v) => v.StudioMode == StudioMode.Camera
        ? "The recording is this camera picture"
        : $"In the recording · {Corner(v.CameraCorner)}";

    internal static string Corner(CameraCorner corner) => corner switch
    {
        CameraCorner.TopLeft => "top left",
        CameraCorner.TopRight => "top right",
        CameraCorner.BottomLeft => "bottom left",
        _ => "bottom right",
    };
}

/// <summary>
/// RC3: live microphone level for the Record panel before recording starts ("confirm the mic is picking up sound").
/// Opens the chosen microphone in shared mode on a background thread only while Mic is turned on in the panel; it
/// records nothing and keeps nothing. Disposing releases the microphone (the Windows microphone indicator goes off).
/// </summary>
internal sealed class MicrophoneMeter : IDisposable
{
    private readonly Thread _thread;
    private volatile bool _stop;
    private float _level;

    public MicrophoneMeter(string? deviceId)
    {
        _thread = new Thread(() => Run(deviceId)) { IsBackground = true, Name = "SnapForge microphone meter" };
        _thread.Start();
    }

    /// <summary>Peak level 0–1, decaying smoothly.</summary>
    public float Level => Volatile.Read(ref _level);

    /// <summary>Windows refused access to the microphone (privacy settings).</summary>
    public bool AccessDenied { get; private set; }

    /// <summary>Device in use, or null while none is available.</summary>
    public string? DeviceName { get; private set; }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run(string? deviceId)
    {
        _ = Com.CoInitializeEx(0, Com.COINIT_MULTITHREADED);
        try
        {
            using var source = new WasapiSource(ScreenRecorder.AudioNames.Microphone, loopback: false, deviceId) { Enabled = true };
            while (!_stop)
            {
                float peak = 0;
                _ = source.Read((_, data, silent) =>
                {
                    if (!silent)
                    {
                        foreach (float s in data)
                        {
                            peak = Math.Max(peak, Math.Abs(s));
                        }
                    }
                });
                AccessDenied = source.AccessDenied;
                DeviceName = source.DeviceName;
                Volatile.Write(ref _level, Math.Max(Math.Min(peak, 1f), Volatile.Read(ref _level) * 0.82f));
                Thread.Sleep(40);
            }
        }
        catch (Exception ex) when (ex is NativeCallException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            RecordingLog.Write("microphone meter: " + ex.Message);
        }
    }
}
