using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapForge.App.Interop;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Capture;

/// <summary>
/// Full-screen, borderless window covering exactly one monitor and showing that monitor's part of the frozen
/// capture. One per monitor (instead of one spanning window) so each renders at its own monitor's DPI.
/// Input positions come from GetCursorPos (physical pixels), never from WPF units, so selection is exact
/// across monitors with different scale factors.
/// </summary>
internal sealed class SelectionOverlayWindow : Window
{
    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)));
    private static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x30)));
    private static readonly Brush GuideBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));

    private readonly OverlaySession _session;
    private readonly PixelRect _monitorBounds;
    private readonly Path _dim;
    private readonly Rectangle _selectionBorder;
    private readonly Line _guideH;
    private readonly Line _guideV;
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeText;
    private readonly Border _hint;
    private readonly BitmapSource _monitorImage;
    private readonly Border _loupe;
    private readonly Image _loupeImage;
    private readonly TextBlock _loupeText;
    private const int LoupeSourcePixels = 15;
    private const int LoupeZoom = 8;

    public SelectionOverlayWindow(OverlaySession session, PixelRect monitorBounds, BitmapSource monitorImage, string hint)
    {
        _session = session;
        _monitorBounds = monitorBounds;
        _monitorImage = monitorImage;

        Title = "SnapForge capture";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 100;
        Height = 100;
        Focusable = true;

        var image = new Image { Source = monitorImage, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        _dim = new Path { Fill = DimBrush, IsHitTestVisible = false };
        _selectionBorder = new Rectangle { Stroke = AccentBrush, StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _guideH = new Line { Stroke = GuideBrush, StrokeThickness = 1, StrokeDashArray = [4, 4], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _guideV = new Line { Stroke = GuideBrush, StrokeThickness = 1, StrokeDashArray = [4, 4], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _sizeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Segoe UI") };
        _sizeLabel = new Border
        {
            Background = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1F, 0x22))),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 3),
            Child = _sizeText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _hint = new Border
        {
            Background = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1F, 0x22))),
            BorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = hint, Foreground = Brushes.White, FontSize = 14, FontFamily = new FontFamily("Segoe UI") },
        };

        // Magnifier: 15×15 source pixels at 8× (nearest neighbour) with a centre marker, the cursor's exact pixel
        // position in the capture and its colour — for pixel-accurate region edges.
        _loupeImage = new Image { Width = LoupeSourcePixels * LoupeZoom, Height = LoupeSourcePixels * LoupeZoom, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_loupeImage, BitmapScalingMode.NearestNeighbor);
        var marker = new Rectangle
        {
            Width = LoupeZoom + 2,
            Height = LoupeZoom + 2,
            Stroke = AccentBrush,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var zoomed = new Grid { ClipToBounds = true };
        zoomed.Children.Add(_loupeImage);
        zoomed.Children.Add(marker);
        _loupeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Segoe UI"), Margin = new Thickness(4, 2, 4, 2) };
        var loupeStack = new StackPanel();
        loupeStack.Children.Add(zoomed);
        loupeStack.Children.Add(_loupeText);
        _loupe = new Border
        {
            Background = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1F, 0x22))),
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(1),
            Child = loupeStack,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(_guideH);
        canvas.Children.Add(_guideV);
        canvas.Children.Add(_selectionBorder);
        canvas.Children.Add(_sizeLabel);
        canvas.Children.Add(_loupe);

        var root = new Grid();
        root.Children.Add(image);
        root.Children.Add(_dim);
        root.Children.Add(canvas);
        root.Children.Add(_hint);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            WindowChrome.DisableTransitions(this);
            PlaceOnMonitor();
        };
        Loaded += (_, _) => PlaceOnMonitor();
        SizeChanged += (_, _) => _session.RefreshAll();
    }

    public PixelRect MonitorBounds => _monitorBounds;

    /// <summary>Redraws the dim mask, selection frame, guides and size label from session state (physical coordinates).</summary>
    public void Refresh(PixelRect? highlight, (int X, int Y)? cursor, bool showGuides, bool showHint, bool showLoupe = false)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        _hint.Visibility = showHint ? Visibility.Visible : Visibility.Collapsed;
        var full = new RectangleGeometry(new Rect(0, 0, w, h));
        Rect? local = highlight is { } hl ? ToLocal(hl) : null;
        Rect clipped = local is { } l ? Rect.Intersect(l, new Rect(0, 0, w, h)) : Rect.Empty;

        if (!clipped.IsEmpty && clipped.Width > 0 && clipped.Height > 0)
        {
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(local!.Value));
            _selectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(_selectionBorder, local.Value.X - 1);
            Canvas.SetTop(_selectionBorder, local.Value.Y - 1);
            _selectionBorder.Width = local.Value.Width + 2;
            _selectionBorder.Height = local.Value.Height + 2;

            PixelRect r = highlight!.Value;
            _sizeText.Text = $"{r.Width} × {r.Height}";
            _sizeLabel.Visibility = Visibility.Visible;
            _sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelTop = local.Value.Y - _sizeLabel.DesiredSize.Height - 6;
            if (labelTop < 4)
            {
                labelTop = Math.Min(local.Value.Bottom + 6, h - _sizeLabel.DesiredSize.Height - 4);
            }

            Canvas.SetLeft(_sizeLabel, Math.Clamp(local.Value.X, 4, Math.Max(4, w - _sizeLabel.DesiredSize.Width - 4)));
            Canvas.SetTop(_sizeLabel, labelTop);
        }
        else
        {
            _dim.Data = full;
            _selectionBorder.Visibility = Visibility.Collapsed;
            _sizeLabel.Visibility = Visibility.Collapsed;
        }

        if (showGuides && cursor is { } c && _monitorBounds.Contains(c.X, c.Y))
        {
            Point p = ToLocal(c.X, c.Y);
            (_guideH.X1, _guideH.X2, _guideH.Y1, _guideH.Y2) = (0, w, p.Y + 0.5, p.Y + 0.5);
            (_guideV.X1, _guideV.X2, _guideV.Y1, _guideV.Y2) = (p.X + 0.5, p.X + 0.5, 0, h);
            _guideH.Visibility = _guideV.Visibility = Visibility.Visible;
        }
        else
        {
            _guideH.Visibility = _guideV.Visibility = Visibility.Collapsed;
        }

        UpdateLoupe(showLoupe ? cursor : null, w, h);
    }

    private void UpdateLoupe((int X, int Y)? cursor, double w, double h)
    {
        if (cursor is not { } c || !_monitorBounds.Contains(c.X, c.Y))
        {
            _loupe.Visibility = Visibility.Collapsed;
            return;
        }

        int px = c.X - _monitorBounds.X, py = c.Y - _monitorBounds.Y;
        int half = LoupeSourcePixels / 2;
        int sx = Math.Clamp(px - half, 0, Math.Max(0, _monitorImage.PixelWidth - LoupeSourcePixels));
        int sy = Math.Clamp(py - half, 0, Math.Max(0, _monitorImage.PixelHeight - LoupeSourcePixels));
        int sw = Math.Min(LoupeSourcePixels, _monitorImage.PixelWidth), sh = Math.Min(LoupeSourcePixels, _monitorImage.PixelHeight);
        _loupeImage.Source = new CroppedBitmap(_monitorImage, new Int32Rect(sx, sy, sw, sh));
        var pixel = new byte[4];
        _monitorImage.CopyPixels(new Int32Rect(Math.Clamp(px, 0, _monitorImage.PixelWidth - 1), Math.Clamp(py, 0, _monitorImage.PixelHeight - 1), 1, 1), pixel, 4, 0);
        _loupeText.Text = $"X {c.X}  Y {c.Y}   #{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}";

        _loupe.Visibility = Visibility.Visible;
        _loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Point p = ToLocal(c.X, c.Y);
        double lw = _loupe.DesiredSize.Width, lh = _loupe.DesiredSize.Height;
        double left = p.X + 24 + lw > w ? p.X - 24 - lw : p.X + 24;
        double top = p.Y + 24 + lh > h ? p.Y - 24 - lh : p.Y + 24;
        Canvas.SetLeft(_loupe, Math.Max(0, left));
        Canvas.SetTop(_loupe, Math.Max(0, top));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Windows applies a "suggested" size when the window lands on a monitor with another DPI; restore the exact bounds.
        Dispatcher.BeginInvoke(PlaceOnMonitor);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        _session.OnPointerDown();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _session.OnPointerMove();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        // Complete the gesture first: releasing capture raises LostMouseCapture synchronously, which would
        // otherwise abandon the drag before it is evaluated.
        _session.OnPointerUp();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        _session.Cancel();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _session.Cancel();
            e.Handled = true;
            return;
        }

        // Arrow keys move the pointer by one physical pixel (Shift: 10) for pixel-exact edges.
        (int dx, int dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if ((dx, dy) != (0, 0) && NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            NativeMethods.SetCursorPos(pt.X + (dx * step), pt.Y + (dy * step));
            _session.OnPointerMove();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _session.OnCaptureLost();
    }

    private Rect ToLocal(PixelRect r)
    {
        Point a = ToLocal(r.X, r.Y);
        Point b = ToLocal(r.Right, r.Bottom);
        return new Rect(a, b);
    }

    private Point ToLocal(int x, int y)
    {
        double sx = ActualWidth / _monitorBounds.Width, sy = ActualHeight / _monitorBounds.Height;
        return new Point((x - _monitorBounds.X) * sx, (y - _monitorBounds.Y) * sy);
    }

    private void PlaceOnMonitor()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _monitorBounds.X, _monitorBounds.Y, _monitorBounds.Width, _monitorBounds.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private static T Freeze<T>(T f)
        where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
