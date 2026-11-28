using SnapForge.Core.Geometry;
using SnapForge.Core.Settings;

namespace SnapForge.Core.Tests;

/// <summary>RC3: the floating camera preview stays inside the usable area of a monitor, at any layout and scaling.</summary>
public class CameraPreviewPlacementTests
{
    // Left: 2560×1440 at 150 % with a 72-px taskbar; right: 1920×1080 at 100 %, taskbar 48 px, offset vertically.
    private static readonly PixelRect Left = new(0, 0, 2560, 1368);
    private static readonly PixelRect Right = new(2560, 200, 1920, 1032);

    [Fact]
    public void RC3_preview_dragged_past_the_right_edge_comes_back_fully_on_screen()
    {
        PixelRect r = WindowFit.KeepOnScreen(new PixelRect(4400, 900, 300, 320), [Left, Right]);
        Assert.Equal(new PixelRect(4180, 900, 300, 320), r);
    }

    [Fact]
    public void RC3_preview_below_the_taskbar_is_moved_above_it()
    {
        PixelRect r = WindowFit.KeepOnScreen(new PixelRect(1000, 1300, 300, 320), [Left, Right]);
        Assert.Equal(1368 - 320, r.Y);
        Assert.Equal(1000, r.X);
    }

    [Fact]
    public void RC3_preview_straddling_two_monitors_goes_to_the_one_it_mostly_covers()
    {
        PixelRect r = WindowFit.KeepOnScreen(new PixelRect(2500, 400, 300, 300), [Left, Right]); // 60 px left, 240 px right
        Assert.Equal(2560, r.X);
    }

    [Fact]
    public void RC3_preview_left_on_a_disconnected_monitor_returns_to_the_nearest_one()
    {
        PixelRect r = WindowFit.KeepOnScreen(new PixelRect(-3000, 100, 300, 300), [Left, Right]);
        Assert.Equal(0, r.X);
        Assert.Equal(100, r.Y);
    }

    [Fact]
    public void RC3_preview_already_inside_is_not_moved()
    {
        var inside = new PixelRect(2000, 1000, 300, 320);
        Assert.Equal(inside, WindowFit.KeepOnScreen(inside, [Left, Right]));
    }

    [Fact]
    public void RC3_default_preview_position_is_bottom_right_inside_the_work_area()
    {
        PixelRect r = WindowFit.BottomRight(Right, 280, 300, 24);
        Assert.Equal(new PixelRect(4480 - 280 - 24, 1232 - 300 - 24, 280, 300), r);
    }

    [Fact]
    public void RC3_open_in_editor_after_recording_is_off_by_default_but_kept_when_saved_on()
    {
        Assert.False(new VideoSettings().OpenAfterRecording);
        Assert.True((new VideoSettings() with { OpenAfterRecording = true }).Normalized().OpenAfterRecording);
    }
}
