using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SnapForge.App.Interop;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Video;

/// <summary>Keeps SnapForge's own recording UI out of the recording (Windows 10 2004+ / Windows 11).</summary>
internal static partial class CaptureExclusion
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);

    /// <summary>Excludes the window from screen capture. Returns false if Windows does not support it.</summary>
    public static bool Apply(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        return SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE) && GetWindowDisplayAffinity(hwnd, out uint a) && a == WDA_EXCLUDEFROMCAPTURE;
    }

    /// <summary>Makes a window ignore the mouse and never take focus (the outline).</summary>
    public static void MakeClickThrough(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)(ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
    }

    /// <summary>Lets a window be clicked without taking focus from the app being recorded (the toolbar).</summary>
    public static void MakeNonActivating(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)(ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
    }
}

/// <summary>A dashed red frame just outside the recorded area. Click-through and excluded from the recording.</summary>
internal sealed class RecordingOutline : Window
{
    private const int Gap = 4;

    public RecordingOutline(PixelRect recorded, bool excludeFromCapture = true)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Title = "SnapForge recording outline";
        Content = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            StrokeThickness = 2,
            StrokeDashArray = [4, 3],
            IsHitTestVisible = false,
        };
        SourceInitialized += (_, _) =>
        {
            CaptureExclusion.MakeClickThrough(this);
            Excluded = excludeFromCapture && CaptureExclusion.Apply(this);
            PhysicalPlacement.SetBounds(this, new PixelRect(recorded.X - Gap, recorded.Y - Gap, recorded.Width + (2 * Gap), recorded.Height + (2 * Gap)), resize: true);
        };
    }

    public bool Excluded { get; private set; }
}

/// <summary>
/// RC3 recording control strip: recording dot and elapsed time, microphone activity, Pause/Resume and Stop — and a
/// "More" menu with the rest (camera preview, camera in the recording, computer audio, microphone, discard). Excluded
/// from the recording; never takes focus from the app being recorded. The same Stop, Pause and "show controls"
/// commands are in the notification-area menu, so the recording can always be stopped even if the strip is hidden
/// or off screen.
/// </summary>
internal sealed class RecordingToolbar : Window
{
    private readonly TextBlock _dot;
    private readonly TextBlock _time;
    private readonly TextBlock _status;
    private readonly Button _pause;
    private readonly ProgressBar _micLevel;
    private readonly TextBlock _micLabel;
    private readonly Button _more;
    private readonly bool _audioTrack;
    private readonly bool _cameraOverlay;
    private bool _system;
    private bool _mic;
    private bool _camera = true;
    private bool _cameraAvailable = true;
    private string? _cameraReason;
    private bool _hasPreview;
    private bool _previewVisible;
    private bool _blink;

    /// <param name="excludeFromCapture">Always true in the app; false only for documentation screenshots and the control half of the exclusion test.</param>
    /// <param name="cameraOverlay">Screen + camera: offer the "camera in the recording" switch.</param>
    public RecordingToolbar(string format, bool audioTrack, bool system, bool mic, string? stopHotkey, string? pauseHotkey, bool excludeFromCapture = true, bool cameraOverlay = false)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Title = "SnapForge recording";
        SetResourceReference(BackgroundProperty, "SurfaceRaisedBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        AutomationProperties_SetName(this, "Recording controls");
        _audioTrack = audioTrack;
        _system = system && audioTrack;
        _mic = mic && audioTrack;
        _cameraOverlay = cameraOverlay;

        _dot = new TextBlock { Text = "●", Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _time = new TextBlock { Text = "0:00:00", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 62, FontFamily = new FontFamily("Consolas, Segoe UI"), ToolTip = format };
        AutomationProperties_SetName(_time, "Elapsed recording time");

        _micLabel = new TextBlock { Text = "Mic", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
        _micLevel = new ProgressBar { Width = 46, Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        _micLevel.SetResourceReference(StyleProperty, "LevelMeter");
        AutomationProperties_SetName(_micLevel, "Microphone level");

        _pause = MakeButton("Pause", "Pause recording" + Hint(pauseHotkey), (_, _) => PauseClicked?.Invoke(this, EventArgs.Empty));
        Button stop = MakeButton("■  Stop", "Stop and save" + Hint(stopHotkey), (_, _) => StopClicked?.Invoke(this, EventArgs.Empty));
        stop.FontWeight = FontWeights.SemiBold;
        _more = MakeButton("More ▾", "More recording controls", (_, _) => ShowMoreMenu());

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 8, 6) };
        row.Children.Add(_dot);
        row.Children.Add(_time);
        row.Children.Add(_micLabel);
        row.Children.Add(_micLevel);
        row.Children.Add(_pause);
        row.Children.Add(stop);
        row.Children.Add(_more);
        UpdateMicLabel();

        _status = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(12, 0, 12, 6), Visibility = Visibility.Collapsed };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_status);
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = stack };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = border;

        // Drag anywhere that is not a button.
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
    }

    public event EventHandler? PauseClicked;

    public event EventHandler? StopClicked;

    public event EventHandler? CancelClicked;

    public event EventHandler<bool>? SystemToggled;

    public event EventHandler<bool>? MicToggled;

    /// <summary>Screen + camera: the user showed (true) or hid (false) the camera overlay in the recording.</summary>
    public event EventHandler<bool>? CameraToggled;

    /// <summary>The user showed (true) or hid (false) the local camera preview (the recording does not change).</summary>
    public event EventHandler<bool>? PreviewToggled;

    /// <summary>True if Windows confirmed the toolbar is excluded from screen capture.</summary>
    public bool Excluded { get; private set; }

    /// <summary>Whether the camera preview menu entry is offered, and its current state.</summary>
    public void SetPreview(bool available, bool visible)
    {
        _hasPreview = available;
        _previewVisible = visible;
    }

    /// <summary>The camera was lost: the overlay switch is turned off and disabled.</summary>
    public void SetCameraUnavailable(string reason)
    {
        _camera = false;
        _cameraAvailable = false;
        _cameraReason = reason;
    }

    public void Update(TimeSpan elapsed, bool paused, float systemLevel, float micLevel)
    {
        _ = systemLevel;
        _time.Text = elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        _blink = !_blink;
        _dot.Opacity = paused ? 0.35 : (_blink ? 1 : 0.55);
        _dot.Text = paused ? "❚❚" : "●";
        _pause.Content = paused ? "Resume" : "Pause";
        AutomationProperties_SetName(_pause, paused ? "Resume recording" : "Pause recording");
        _micLevel.Value = _mic && !paused ? Math.Sqrt(micLevel) * 100 : 0;
    }

    public void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowSaving()
    {
        _time.Text = "Saving…";
        _dot.Opacity = 0.35;
        IsEnabled = false;
    }

    /// <summary>Brings the strip back onto the screen (from the notification-area menu).</summary>
    public void Reveal()
    {
        Show();
        KeepOnScreen();
    }

    /// <summary>Places the toolbar centred below the recorded area, or above it / inside it if there is no room.</summary>
    public void PlaceNear(PixelRect recorded, PixelRect monitorWork)
    {
        UpdateLayout();
        PixelRect? b = PhysicalPlacement.GetBounds(this);
        int w = b?.Width ?? 420, h = b?.Height ?? 44;
        int x = Math.Clamp(recorded.X + ((recorded.Width - w) / 2), monitorWork.X, Math.Max(monitorWork.X, monitorWork.Right - w));
        int y = recorded.Bottom + 10;
        if (y + h > monitorWork.Bottom)
        {
            y = recorded.Y - h - 10;
        }

        if (y < monitorWork.Y)
        {
            y = Math.Max(monitorWork.Y, monitorWork.Bottom - h - 12); // inside: it is excluded from the recording anyway
        }

        PhysicalPlacement.SetBounds(this, new PixelRect(x, y, w, h), resize: false);
    }

    private void KeepOnScreen()
    {
        if (PhysicalPlacement.GetBounds(this) is { } b)
        {
            PixelRect fitted = SnapForge.Core.Geometry.WindowFit.KeepOnScreen(b, PhysicalPlacement.Layout().Monitors.Select(m => m.WorkArea).ToList());
            if (fitted != b)
            {
                PhysicalPlacement.SetBounds(this, fitted, resize: false);
            }
        }
    }

    private void UpdateMicLabel()
    {
        _micLabel.Text = !_audioTrack || !_mic ? "Mic off" : "Mic";
        _micLabel.Opacity = _mic ? 1 : 0.6;
        _micLevel.Visibility = _mic ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties_SetName(_micLabel, _mic ? "Microphone on" : "Microphone off");
    }

    private void ShowMoreMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _more, Placement = PlacementMode.Bottom };
        MenuItem Item(string header, bool isChecked, bool enabled, string? tip, Action click)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked, IsEnabled = enabled, ToolTip = tip };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }

        if (_hasPreview)
        {
            Item("Show camera _preview", _previewVisible, true, "Only for you — the recording does not change.", () =>
            {
                _previewVisible = !_previewVisible;
                PreviewToggled?.Invoke(this, _previewVisible);
            });
        }

        if (_cameraOverlay)
        {
            Item("_Camera in the recording", _camera, _cameraAvailable, _cameraAvailable ? "Turn the camera off or on in the saved video." : _cameraReason, () =>
            {
                _camera = !_camera;
                CameraToggled?.Invoke(this, _camera);
            });
        }

        string noAudio = "Audio was off when the recording started.";
        Item("_Microphone", _mic, _audioTrack, _audioTrack ? null : noAudio, () =>
        {
            _mic = !_mic;
            UpdateMicLabel();
            MicToggled?.Invoke(this, _mic);
        });
        Item("Computer _audio", _system, _audioTrack, _audioTrack ? null : noAudio, () =>
        {
            _system = !_system;
            SystemToggled?.Invoke(this, _system);
        });
        menu.Items.Add(new Separator());
        var discard = new MenuItem { Header = "_Discard recording…" };
        discard.Click += (_, _) => CancelClicked?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(discard);
        _more.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static string Hint(string? hotkey) => hotkey is null ? string.Empty : $" ({hotkey})";

    private static Button MakeButton(string text, string tooltip, RoutedEventHandler click)
    {
        var b = new Button { Content = text, ToolTip = tooltip, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(10, 3, 10, 3), Focusable = false };
        b.SetResourceReference(StyleProperty, "ToolbarButton");
        AutomationProperties_SetName(b, tooltip);
        b.Click += click;
        return b;
    }

    private static void AutomationProperties_SetName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            CancelClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
