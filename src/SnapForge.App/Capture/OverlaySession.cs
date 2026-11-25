using SnapForge.App.Rendering;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Capture;

/// <summary>
/// Runs one interactive selection over the frozen snapshot: one overlay per monitor, shared state in
/// physical virtual-desktop coordinates. Completes with the chosen screen rectangle, or null if cancelled.
/// </summary>
internal sealed class OverlaySession
{
    private const int MinimumRegionSize = 2;

    private readonly CaptureSnapshot _snapshot;
    private readonly CaptureMode _mode;
    private readonly List<SelectionOverlayWindow> _windows = [];
    private readonly TaskCompletionSource<PixelRect?> _completion = new();
    private (int X, int Y)? _dragStart;
    private PixelRect? _highlight;
    private bool _finished;

    private OverlaySession(CaptureSnapshot snapshot, CaptureMode mode)
    {
        _snapshot = snapshot;
        _mode = mode;
    }

    public static Task<PixelRect?> RunAsync(CaptureSnapshot snapshot, CaptureMode mode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (mode == CaptureMode.AllScreens)
        {
            throw new ArgumentException("All-screens capture does not need a selection.", nameof(mode));
        }

        var session = new OverlaySession(snapshot, mode);
        session.Open();
        return session._completion.Task;
    }

    public void OnPointerDown()
    {
        if (_finished)
        {
            return;
        }

        if (_mode == CaptureMode.Region)
        {
            _dragStart = ScreenCaptureService.GetCursorPosition();
            UpdateHighlight();
        }
    }

    public void OnPointerMove()
    {
        if (!_finished)
        {
            UpdateHighlight();
        }
    }

    public void OnPointerUp()
    {
        if (_finished)
        {
            return;
        }

        UpdateHighlight();
        switch (_mode)
        {
            case CaptureMode.Region:
                if (_dragStart is not null && _highlight is { } region && region.Width >= MinimumRegionSize && region.Height >= MinimumRegionSize)
                {
                    Finish(region);
                }
                else
                {
                    // A click without a drag: keep the overlay open and let the user try again.
                    _dragStart = null;
                    UpdateHighlight();
                }

                break;
            case CaptureMode.Window:
            case CaptureMode.SingleScreen:
                if (_highlight is { } picked)
                {
                    Finish(picked);
                }

                break;
        }
    }

    public void OnCaptureLost()
    {
        // Alt+Tab or a system dialog stole the mouse mid-drag: abandon the drag rather than finishing it.
        if (!_finished && _dragStart is not null && System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            _dragStart = null;
            UpdateHighlight();
        }
    }

    public void Cancel() => Finish(null);

    public void RefreshAll()
    {
        (int X, int Y) cursor = ScreenCaptureService.GetCursorPosition();
        bool guides = _mode == CaptureMode.Region && _dragStart is null;
        foreach (SelectionOverlayWindow w in _windows)
        {
            w.Refresh(_highlight, cursor, guides, showHint: _dragStart is null && w.MonitorBounds.Contains(cursor.X, cursor.Y));
        }
    }

    private void Open()
    {
        ScreenLayout layout = _snapshot.Layout;
        string hint = _mode switch
        {
            CaptureMode.Region => "Drag to select an area  ·  Esc or right-click to cancel",
            CaptureMode.Window => "Click a window to capture it  ·  Esc to cancel",
            CaptureMode.SingleScreen => "Click a display to capture it  ·  Esc to cancel",
            _ => string.Empty,
        };

        foreach (MonitorInfo monitor in layout.Monitors)
        {
            var image = BitmapConversion.ToFrozenBitmap(_snapshot.Image.Crop(layout.ToBitmapSpace(monitor.Bounds)));
            var window = new SelectionOverlayWindow(this, monitor.Bounds, image, hint);
            _windows.Add(window);
            window.Show();
        }

        (int X, int Y) cursor = ScreenCaptureService.GetCursorPosition();
        SelectionOverlayWindow active = _windows.FirstOrDefault(w => w.MonitorBounds.Contains(cursor.X, cursor.Y)) ?? _windows[0];
        active.Activate();
        active.Focus();
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        (int X, int Y) c = ScreenCaptureService.GetCursorPosition();
        _highlight = _mode switch
        {
            CaptureMode.Region when _dragStart is { } s =>
                PixelRect.FromLtrb(Math.Min(s.X, c.X), Math.Min(s.Y, c.Y), Math.Max(s.X, c.X) + 1, Math.Max(s.Y, c.Y) + 1)
                    .Intersect(_snapshot.Layout.VirtualBounds),
            CaptureMode.Region => null,
            CaptureMode.Window => WindowPicker.WindowAt(_snapshot.Windows, c.X, c.Y) is { } w
                ? WindowPicker.CaptureRegionFor(w, _snapshot.Layout)
                : null,
            CaptureMode.SingleScreen => _snapshot.Layout.MonitorAt(c.X, c.Y)?.Bounds,
            _ => null,
        };
        if (_highlight is { IsEmpty: true })
        {
            _highlight = null;
        }

        RefreshAll();
    }

    private void Finish(PixelRect? result)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        foreach (SelectionOverlayWindow w in _windows)
        {
            w.Close();
        }

        _windows.Clear();
        _completion.TrySetResult(result is { IsEmpty: false } ? result : null);
    }
}
