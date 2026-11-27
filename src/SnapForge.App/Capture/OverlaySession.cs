using System.Windows.Input;
using SnapForge.App.Rendering;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Capture;

/// <summary>
/// Runs one interactive selection over the frozen snapshot: one overlay per monitor, with all selection
/// logic in <see cref="SelectionTracker"/> (physical virtual-desktop coordinates, unit tested).
/// Completes with the chosen screen rectangle, or null if cancelled.
/// </summary>
internal sealed class OverlaySession
{
    private readonly CaptureSnapshot _snapshot;
    private readonly SelectionTracker _tracker;
    private readonly List<SelectionOverlayWindow> _windows = [];
    private readonly TaskCompletionSource<PixelRect?> _completion = new();

    private OverlaySession(CaptureSnapshot snapshot, CaptureMode mode)
    {
        _snapshot = snapshot;
        _tracker = new SelectionTracker(mode, snapshot.Layout, snapshot.Windows);
        _tracker.Finished += (_, _) => Close();
    }

    public static Task<PixelRect?> RunAsync(CaptureSnapshot snapshot, CaptureMode mode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var session = new OverlaySession(snapshot, mode);
        session.Open();
        return session._completion.Task;
    }

    public void OnPointerDown() => Apply(_tracker.PointerDown);

    public void OnPointerMove() => Apply(_tracker.PointerMove);

    public void OnPointerUp() => Apply(_tracker.PointerUp);

    public void OnCaptureLost()
    {
        (int x, int y) = ScreenCaptureService.GetCursorPosition();
        _tracker.CaptureLost(x, y, Mouse.LeftButton == MouseButtonState.Pressed);
        RefreshAll();
    }

    public void Cancel() => _tracker.Cancel();

    public void RefreshAll()
    {
        if (_tracker.IsFinished)
        {
            return;
        }

        (int X, int Y) cursor = ScreenCaptureService.GetCursorPosition();
        bool guides = _tracker.Mode == CaptureMode.Region && !_tracker.IsDragging;
        foreach (SelectionOverlayWindow w in _windows)
        {
            w.Refresh(_tracker.Highlight, cursor, guides, showHint: !_tracker.IsDragging && w.MonitorBounds.Contains(cursor.X, cursor.Y), showLoupe: _tracker.Mode == CaptureMode.Region);
        }
    }

    private void Apply(Action<int, int> input)
    {
        (int x, int y) = ScreenCaptureService.GetCursorPosition();
        input(x, y);
        RefreshAll();
    }

    private void Open()
    {
        string hint = _tracker.Mode switch
        {
            CaptureMode.Region => "Drag to select an area  ·  Esc or right-click to cancel",
            CaptureMode.Window => "Click a window to capture it  ·  Esc to cancel",
            CaptureMode.SingleScreen => "Click a display to capture it  ·  Esc to cancel",
            _ => string.Empty,
        };

        try
        {
            foreach (MonitorInfo monitor in _snapshot.Layout.Monitors)
            {
                var image = BitmapConversion.ToFrozenBitmap(_snapshot.Image.Crop(_snapshot.Layout.ToBitmapSpace(monitor.Bounds)));
                var window = new SelectionOverlayWindow(this, monitor.Bounds, image, hint);
                _windows.Add(window);
                window.Show();
            }
        }
        catch
        {
            // Never leave topmost full-screen overlays behind if one of them fails to open.
            _tracker.Cancel();
            throw;
        }

        (int X, int Y) cursor = ScreenCaptureService.GetCursorPosition();
        SelectionOverlayWindow active = _windows.FirstOrDefault(w => w.MonitorBounds.Contains(cursor.X, cursor.Y)) ?? _windows[0];
        active.Activate();
        active.Focus();
        Apply(_tracker.PointerMove);
    }

    private void Close()
    {
        SelectionOverlayWindow[] windows = [.. _windows];
        _windows.Clear();
        foreach (SelectionOverlayWindow w in windows)
        {
            w.Close();
        }

        _completion.TrySetResult(_tracker.Result);
    }
}
