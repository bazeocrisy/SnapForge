using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.Core.Tests;

public class SelectionTrackerTests
{
    // Primary 1920x1080 at origin, secondary 2560x1440 to the LEFT (negative coordinates).
    private static readonly ScreenLayout TwoMonitors = new(
    [
        new MonitorInfo("P", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true, 1.0),
        new MonitorInfo("L", new PixelRect(-2560, -200, 2560, 1440), new PixelRect(-2560, -200, 2560, 1400), false, 1.5),
    ]);

    private static SelectionTracker Region() => new(CaptureMode.Region, TwoMonitors, []);

    [Fact]
    public void Regression_capture_lost_before_button_up_still_completes_the_drag()
    {
        // Exact event order produced by WPF when the overlay released capture before handling mouse-up
        // (the bug in the first MVP build): Down, Move, CaptureLost(button released), Up.
        SelectionTracker t = Region();
        t.PointerDown(100, 100);
        t.PointerMove(250, 180);
        t.CaptureLost(300, 200, buttonStillPressed: false);
        t.PointerUp(300, 200);

        Assert.True(t.IsFinished);
        Assert.Equal(new PixelRect(100, 100, 201, 101), t.Result);
    }

    [Fact]
    public void Normal_order_button_up_then_capture_lost_completes_once()
    {
        SelectionTracker t = Region();
        int finishedCount = 0;
        t.Finished += (_, _) => finishedCount++;
        t.PointerDown(10, 20);
        t.PointerUp(40, 60);
        t.CaptureLost(40, 60, buttonStillPressed: false);

        Assert.Equal(new PixelRect(10, 20, 31, 41), t.Result);
        Assert.Equal(1, finishedCount);
    }

    [Fact]
    public void Capture_lost_with_button_held_abandons_drag_but_keeps_overlay_open()
    {
        SelectionTracker t = Region();
        t.PointerDown(10, 10);
        t.PointerMove(100, 100);
        t.CaptureLost(100, 100, buttonStillPressed: true);

        Assert.False(t.IsFinished);
        Assert.False(t.IsDragging);
        Assert.Null(t.Highlight);

        // The user can still make a new selection afterwards.
        t.PointerDown(5, 5);
        t.PointerUp(50, 50);
        Assert.Equal(new PixelRect(5, 5, 46, 46), t.Result);
    }

    [Fact]
    public void Click_without_drag_does_not_finish()
    {
        SelectionTracker t = Region();
        t.PointerDown(30, 30);
        t.PointerUp(30, 30);
        Assert.False(t.IsFinished);
        Assert.False(t.IsDragging);
    }

    [Fact]
    public void Drag_in_any_direction_is_normalized()
    {
        SelectionTracker t = Region();
        t.PointerDown(300, 200);
        t.PointerUp(100, 100);
        Assert.Equal(new PixelRect(100, 100, 201, 101), t.Result);
    }

    [Fact]
    public void Drag_across_monitors_with_negative_coordinates_is_exact()
    {
        SelectionTracker t = Region();
        t.PointerDown(-500, 50);
        t.PointerMove(0, 300);
        t.PointerUp(400, 600);
        Assert.Equal(PixelRect.FromLtrb(-500, 50, 401, 601), t.Result);
    }

    [Fact]
    public void Drag_beyond_the_desktop_is_clipped()
    {
        SelectionTracker t = Region();
        t.PointerDown(1900, 1000);
        t.PointerUp(5000, 5000);
        Assert.Equal(PixelRect.FromLtrb(1900, 1000, 1920, 1240), t.Result);
    }

    [Fact]
    public void Highlight_tracks_drag_live()
    {
        SelectionTracker t = Region();
        Assert.Null(t.Highlight);
        t.PointerDown(0, 0);
        t.PointerMove(9, 4);
        Assert.Equal(new PixelRect(0, 0, 10, 5), t.Highlight);
    }

    [Fact]
    public void Cancel_finishes_with_null_and_later_events_are_ignored()
    {
        SelectionTracker t = Region();
        t.PointerDown(0, 0);
        t.Cancel();
        t.PointerUp(100, 100);
        Assert.True(t.IsFinished);
        Assert.Null(t.Result);
    }

    [Fact]
    public void Window_mode_picks_frontmost_window_under_cursor_on_click()
    {
        WindowCandidate[] windows =
        [
            new(1, "Editor", new PixelRect(100, 100, 800, 600), 0),
            new(2, "Desktop", new PixelRect(0, 0, 1920, 1080), 5),
        ];
        var t = new SelectionTracker(CaptureMode.Window, TwoMonitors, windows);
        t.PointerMove(200, 200);
        Assert.Equal(new PixelRect(100, 100, 800, 600), t.Highlight);
        t.PointerDown(200, 200);
        t.PointerUp(200, 200);
        Assert.Equal(new PixelRect(100, 100, 800, 600), t.Result);
    }

    [Fact]
    public void Window_mode_click_on_nothing_keeps_overlay_open()
    {
        var t = new SelectionTracker(CaptureMode.Window, TwoMonitors, []);
        t.PointerUp(50, 50);
        Assert.False(t.IsFinished);
    }

    [Fact]
    public void Display_mode_picks_the_monitor_under_cursor()
    {
        var t = new SelectionTracker(CaptureMode.SingleScreen, TwoMonitors, []);
        t.PointerMove(-10, 10);
        t.PointerUp(-10, 10);
        Assert.Equal(new PixelRect(-2560, -200, 2560, 1440), t.Result);
    }

    [Fact]
    public void All_screens_mode_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new SelectionTracker(CaptureMode.AllScreens, TwoMonitors, []));
}
