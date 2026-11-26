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
/// The floating recording toolbar: recording indicator and elapsed time, Pause/Resume, Stop and save,
/// Cancel and discard, system-audio and microphone toggles with level meters, resolution and frame rate, and a line
/// for notices (device changes, low disk space). Excluded from the recording; does not take focus when clicked.
/// </summary>
internal sealed class RecordingToolbar : Window
{
    private readonly TextBlock _dot;
    private readonly TextBlock _time;
    private readonly TextBlock _status;
    private readonly Button _pause;
    private readonly ToggleButton _systemToggle;
    private readonly ToggleButton _micToggle;
    private readonly ProgressBar _systemLevel;
    private readonly ProgressBar _micLevel;
    private bool _blink;

    /// <param name="excludeFromCapture">Always true in the app; false only for documentation screenshots and the control half of the exclusion test.</param>
    public RecordingToolbar(string format, bool audioTrack, bool system, bool mic, string? stopHotkey, string? pauseHotkey, bool excludeFromCapture = true)
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
        AutomationProperties_SetName(this, "Recording toolbar");

        _dot = new TextBlock { Text = "●", Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _time = new TextBlock { Text = "0:00:00", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, FontFamily = new FontFamily("Consolas, Segoe UI") };
        AutomationProperties_SetName(_time, "Elapsed recording time");
        var formatText = new TextBlock { Text = format, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Opacity = 0.75 };
        AutomationProperties_SetName(formatText, "Resolution and frame rate: " + format);

        _pause = MakeButton("Pause", "Pause recording" + Hint(pauseHotkey), (_, _) => PauseClicked?.Invoke(this, EventArgs.Empty));
        Button stop = MakeButton("Stop", "Stop and save" + Hint(stopHotkey), (_, _) => StopClicked?.Invoke(this, EventArgs.Empty));
        stop.FontWeight = FontWeights.SemiBold;
        Button cancel = MakeButton("Cancel", "Cancel and discard the recording", (_, _) => CancelClicked?.Invoke(this, EventArgs.Empty));

        (_systemToggle, _systemLevel) = MakeToggle("System audio", system, audioTrack, (on) => SystemToggled?.Invoke(this, on));
        (_micToggle, _micLevel) = MakeToggle("Microphone", mic, audioTrack, (on) => MicToggled?.Invoke(this, on));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 10, 4) };
        row.Children.Add(_dot);
        row.Children.Add(_time);
        row.Children.Add(formatText);
        row.Children.Add(_pause);
        row.Children.Add(stop);
        row.Children.Add(cancel);
        row.Children.Add(new Separator { Width = 1, Margin = new Thickness(6, 2, 6, 2) });
        row.Children.Add(Pair(_systemToggle, _systemLevel));
        row.Children.Add(Pair(_micToggle, _micLevel));

        _status = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Margin = new Thickness(12, 0, 12, 6), Visibility = Visibility.Collapsed };
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

    /// <summary>True if Windows confirmed the toolbar is excluded from screen capture.</summary>
    public bool Excluded { get; private set; }

    public void Update(TimeSpan elapsed, bool paused, float systemLevel, float micLevel)
    {
        _time.Text = elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        _blink = !_blink;
        _dot.Opacity = paused ? 0.35 : (_blink ? 1 : 0.55);
        _dot.Text = paused ? "❚❚" : "●";
        _pause.Content = paused ? "Resume" : "Pause";
        AutomationProperties_SetName(_pause, paused ? "Resume recording" : "Pause recording");
        _systemLevel.Value = _systemToggle.IsChecked == true ? Math.Sqrt(systemLevel) * 100 : 0;
        _micLevel.Value = _micToggle.IsChecked == true ? Math.Sqrt(micLevel) * 100 : 0;
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

    /// <summary>Places the toolbar centred below the recorded area, or above it / inside it if there is no room.</summary>
    public void PlaceNear(PixelRect recorded, PixelRect monitorWork)
    {
        UpdateLayout();
        PixelRect? b = PhysicalPlacement.GetBounds(this);
        int w = b?.Width ?? 520, h = b?.Height ?? 44;
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

    private static string Hint(string? hotkey) => hotkey is null ? string.Empty : $" ({hotkey})";

    private static Button MakeButton(string text, string tooltip, RoutedEventHandler click)
    {
        var b = new Button { Content = text, ToolTip = tooltip, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(10, 3, 10, 3), Focusable = false };
        b.SetResourceReference(StyleProperty, "ToolbarButton");
        AutomationProperties_SetName(b, tooltip);
        b.Click += click;
        return b;
    }

    private static (ToggleButton, ProgressBar) MakeToggle(string name, bool on, bool available, Action<bool> changed)
    {
        var t = new ToggleButton
        {
            Content = name,
            IsChecked = on && available,
            IsEnabled = available,
            Margin = new Thickness(4, 0, 2, 0),
            Padding = new Thickness(8, 3, 8, 3),
            Focusable = false,
            ToolTip = available ? $"Record {name.ToLowerInvariant()} (click to turn off or on)" : "Audio was off when the recording started. Turn on system audio or the microphone before recording.",
        };
        t.SetResourceReference(StyleProperty, "ToolbarToggle");
        AutomationProperties_SetName(t, name);
        t.Click += (_, _) => changed(t.IsChecked == true);
        var level = new ProgressBar { Width = 44, Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(4, 2, 6, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        level.SetResourceReference(StyleProperty, "LevelMeter");
        AutomationProperties_SetName(level, name + " level");
        return (t, level);
    }

    private static StackPanel Pair(UIElement a, UIElement b)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(a);
        p.Children.Add(b);
        return p;
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
