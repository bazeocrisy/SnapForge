using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SnapForge.App.Interop;
using SnapForge.App.Themes;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Capture;

/// <summary>
/// Scrolling capture of one screen area (a document, web page or list that scrolls inside the selected area).
/// <para><b>Auto-scroll</b> sends mouse-wheel input to the area and captures after each step until the content stops
/// moving. <b>Manual</b>: the user scrolls (wheel, keyboard or scrollbar) and presses Add frame. Every frame is aligned
/// with the previous one by <see cref="ScrollStitcher"/>; frames that cannot be aligned are rejected (never guessed),
/// and the user is told to scroll less or finish. The control panel is excluded from screen capture. The result opens
/// in the Editor for review before it is used. Works only where content scrolls inside a fixed area; apps that redraw
/// or animate while scrolling may not align — no universal compatibility is claimed.</para>
/// </summary>
internal sealed partial class ScrollingCaptureWindow : Window
{
    private const int MaxFrames = 400;
    private const int WheelNotchesPerStep = 3;
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(400);

    private readonly PixelRect _region;
    private readonly ScrollStitcher _stitcher = new(new ScrollStitcherOptions { MaxOutputPixels = 120_000_000, MaxLength = 30_000 });
    private readonly TextBlock _status;
    private readonly Button _auto, _add, _finish, _cancel;
    private readonly TaskCompletionSource<PixelBuffer?> _done = new();
    private bool _autoRunning;
    private bool _stopAuto;

    private ScrollingCaptureWindow(PixelRect region, PixelBuffer first)
    {
        _region = region;
        Title = "Scrolling capture — SnapForge";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont");

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _auto = new Button { Content = "_Auto-scroll", MinWidth = 100, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Scrolls the area with the mouse wheel and captures until the end" };
        _add = new Button { Content = "Add _frame", MinWidth = 100, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Scroll the area yourself, then press this (or Space)" };
        _finish = new Button { Content = "_Finish", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        _cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        _auto.Click += async (_, _) => await AutoScrollAsync().ConfigureAwait(true);
        _add.Click += (_, _) => AddFrame(manual: true);
        _finish.Click += (_, _) => Complete(cancel: false);
        _cancel.Click += (_, _) => Complete(cancel: true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_auto);
        buttons.Children.Add(_add);
        buttons.Children.Add(_finish);
        buttons.Children.Add(_cancel);
        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(_status);
        root.Children.Add(buttons);
        Content = root;

        _stitcher.AddFrame(first);
        _status.Text = $"Area {region.Width} × {region.Height}. Press Auto-scroll, or scroll the area yourself and press Add frame after each step (scroll less than a screenful).";
        SourceInitialized += (_, _) =>
        {
            WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
            // The panel must never appear in the frames it captures.
            _ = SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
        };
        Loaded += (_, _) => PlaceOutsideRegion();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && !_autoRunning)
            {
                AddFrame(manual: true);
                e.Handled = true;
            }
        };
        Closed += (_, _) => _done.TrySetResult(null);
    }

    /// <summary>Runs a scrolling capture of <paramref name="region"/>; returns the stitched image or null if cancelled.</summary>
    public static Task<PixelBuffer?> RunAsync(PixelRect region)
    {
        PixelBuffer first = ScreenCaptureService.CaptureRegion(region);
        var w = new ScrollingCaptureWindow(region, first);
        w.Show();
        w.Activate();
        return w._done.Task;
    }

    /// <summary>Test hook: opens the session, runs Auto-scroll to the end and finishes (real wheel input).</summary>
    internal static async Task<(PixelBuffer? Image, string Status)> RunAutoForTestAsync(PixelRect region)
    {
        PixelBuffer first = ScreenCaptureService.CaptureRegion(region);
        var w = new ScrollingCaptureWindow(region, first);
        w.Show();
        await w.AutoScrollAsync().ConfigureAwait(true);
        string status = w._status.Text;
        w.Complete(cancel: false);
        return (await w._done.Task.ConfigureAwait(true), status);
    }

    private void PlaceOutsideRegion()
    {
        // Physical pixels → this window's DIPs.
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        ScreenLayout layout = ScreenCaptureService.GetScreenLayout();
        double w = ActualWidth * scale, h = ActualHeight * scale;
        PixelRect desk = layout.VirtualBounds;
        double x = _region.Right + 12, y = _region.Y;
        if (x + w > desk.Right)
        {
            x = _region.X - w - 12;
        }

        if (x < desk.X)
        {
            x = _region.X;
            y = _region.Bottom + 12 + h <= desk.Bottom ? _region.Bottom + 12 : Math.Max(desk.Y, _region.Y - h - 12);
        }

        Left = x / scale;
        Top = y / scale;
    }

    private StitchStatus AddFrame(bool manual)
    {
        if (_stitcher.FrameCount >= MaxFrames)
        {
            _status.Text = "Maximum number of frames reached. Press Finish.";
            return StitchStatus.LimitReached;
        }

        PixelBuffer frame = ScreenCaptureService.CaptureRegion(_region);
        StitchResult r = _stitcher.AddFrame(frame);
        _status.Text = r.Status switch
        {
            StitchStatus.Appended => $"Captured {_stitcher.FrameCount} frames — {_stitcher.Length} px so far.",
            StitchStatus.NoMovement => manual ? "Nothing new — scroll the area first, then press Add frame. Press Finish when you are done." : "The content stopped moving.",
            StitchStatus.Unaligned => "This frame could not be matched to the previous one (scrolled too far, or the content changed). Scroll back a little and press Add frame, or press Finish.",
            StitchStatus.LimitReached => "Maximum length reached. Press Finish.",
            _ => _status.Text,
        };
        return r.Status;
    }

    private async Task AutoScrollAsync()
    {
        if (_autoRunning)
        {
            _stopAuto = true;
            return;
        }

        _autoRunning = true;
        _stopAuto = false;
        _auto.Content = "_Stop";
        _add.IsEnabled = _finish.IsEnabled = false;
        NativeMethods.GetCursorPos(out NativeMethods.POINT original);
        int still = 0;
        try
        {
            for (int step = 0; step < MaxFrames && !_stopAuto; step++)
            {
                // The wheel goes to the window under the pointer: put it in the middle of the area for each step.
                NativeMethods.SetCursorPos(_region.X + (_region.Width / 2), _region.Y + (_region.Height / 2));
                SendWheel(-WheelNotchesPerStep * 120);
                await Task.Delay(SettleTime).ConfigureAwait(true);
                StitchStatus s = AddFrame(manual: false);
                if (s == StitchStatus.NoMovement)
                {
                    if (++still >= 2)
                    {
                        _status.Text = $"Reached the end: {_stitcher.FrameCount} frames, {_stitcher.Length} px. Press Finish to review it in the Editor.";
                        break;
                    }
                }
                else if (s == StitchStatus.Appended)
                {
                    still = 0;
                }
                else
                {
                    break; // Unaligned / limit: message already shown; the user decides
                }
            }
        }
        finally
        {
            NativeMethods.SetCursorPos(original.X, original.Y);
            _autoRunning = false;
            _auto.Content = "_Auto-scroll";
            _add.IsEnabled = _finish.IsEnabled = true;
        }
    }

    private void Complete(bool cancel)
    {
        _stopAuto = true;
        PixelBuffer? result = cancel ? null : _stitcher.Build();
        _done.TrySetResult(result);
        Close();
    }

    private static void SendWheel(int delta)
    {
        var input = new INPUT { Type = 0 /* INPUT_MOUSE */, Mouse = new MOUSEINPUT { MouseData = unchecked((uint)delta), Flags = 0x0800 /* MOUSEEVENTF_WHEEL */ } };
        _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public MOUSEINPUT Mouse;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] INPUT[] inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
