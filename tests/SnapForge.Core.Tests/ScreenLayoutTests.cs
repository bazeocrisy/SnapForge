using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.Core.Tests;

public class ScreenLayoutTests
{
    private static MonitorInfo M(string name, int x, int y, int w, int h, bool primary = false, double scale = 1) =>
        new(name, new PixelRect(x, y, w, h), new PixelRect(x, y, w, h - 40), primary, scale);

    [Fact]
    public void Single_monitor_virtual_bounds_equal_monitor()
    {
        var layout = new ScreenLayout([M("A", 0, 0, 2560, 1440, true, 1.5)]);
        Assert.Equal(new PixelRect(0, 0, 2560, 1440), layout.VirtualBounds);
        Assert.Empty(layout.UncoveredAreas());
    }

    [Fact]
    public void Monitor_left_of_primary_gives_negative_origin_and_offsets_bitmap()
    {
        var layout = new ScreenLayout([M("P", 0, 0, 1920, 1080, true), M("L", -2560, -200, 2560, 1440)]);
        Assert.Equal(PixelRect.FromLtrb(-2560, -200, 1920, 1240), layout.VirtualBounds);
        Assert.Equal(new PixelRect(2560, 200, 100, 100), layout.ToBitmapSpace(new PixelRect(0, 0, 100, 100)));
        Assert.Equal("P", layout.Monitors[0].DeviceName);
    }

    [Fact]
    public void Monitors_ordered_primary_then_left_to_right()
    {
        var layout = new ScreenLayout([M("R", 3840, 0, 1920, 1080), M("P", 1920, 0, 1920, 1080, true), M("L", 0, 0, 1920, 1080)]);
        Assert.Equal(["P", "L", "R"], layout.Monitors.Select(m => m.DeviceName));
    }

    [Fact]
    public void MonitorAt_finds_correct_monitor()
    {
        var layout = new ScreenLayout([M("P", 0, 0, 1920, 1080, true), M("R", 1920, 0, 1280, 1024)]);
        Assert.Equal("R", layout.MonitorAt(1920, 5)!.DeviceName);
        Assert.Equal("P", layout.MonitorAt(1919, 5)!.DeviceName);
        Assert.Null(layout.MonitorAt(2000, 1050));
    }

    [Fact]
    public void Uncovered_areas_found_for_mismatched_heights()
    {
        var layout = new ScreenLayout([M("P", 0, 0, 1920, 1080, true), M("R", 1920, 0, 1280, 1024)]);
        IReadOnlyList<PixelRect> gaps = layout.UncoveredAreas();
        Assert.Equal([PixelRect.FromLtrb(1920, 1024, 3200, 1080)], gaps);
        long covered = layout.Monitors.Sum(m => m.Bounds.Area) + gaps.Sum(g => g.Area);
        Assert.Equal(layout.VirtualBounds.Area, covered);
    }

    [Fact]
    public void Uncovered_areas_partition_staggered_layout()
    {
        var layout = new ScreenLayout([M("P", 0, 300, 1920, 1080, true), M("U", 1920, 0, 1920, 1080), M("D", -1280, 900, 1280, 1024)]);
        IReadOnlyList<PixelRect> gaps = layout.UncoveredAreas();
        long covered = layout.Monitors.Sum(m => m.Bounds.Area) + gaps.Sum(g => g.Area);
        Assert.Equal(layout.VirtualBounds.Area, covered);
        foreach (PixelRect g in gaps)
        {
            Assert.DoesNotContain(layout.Monitors, m => m.Bounds.IntersectsWith(g));
        }
    }

    [Fact]
    public void Selection_spanning_monitors_is_clipped_to_virtual_desktop()
    {
        var layout = new ScreenLayout([M("P", 0, 0, 1920, 1080, true), M("L", -1920, 0, 1920, 1080)]);
        PixelRect clipped = layout.ClipSelectionToBitmap(PixelRect.FromDragPoints(-100, -50, 100, 2000));
        Assert.Equal(new PixelRect(1820, 0, 200, 1080), clipped);
    }

    [Fact]
    public void Empty_monitor_list_rejected() => Assert.Throws<ArgumentException>(() => new ScreenLayout([]));

    [Fact]
    public void Window_picker_prefers_frontmost_window()
    {
        WindowCandidate[] windows =
        [
            new(1, "Back", new PixelRect(0, 0, 500, 500), 2),
            new(2, "Front", new PixelRect(100, 100, 100, 100), 0),
            new(3, "Middle", new PixelRect(50, 50, 300, 300), 1),
        ];
        Assert.Equal("Front", WindowPicker.WindowAt(windows, 150, 150)!.Title);
        Assert.Equal("Middle", WindowPicker.WindowAt(windows, 60, 60)!.Title);
        Assert.Equal("Back", WindowPicker.WindowAt(windows, 10, 10)!.Title);
        Assert.Null(WindowPicker.WindowAt(windows, 600, 600));
    }

    [Fact]
    public void Maximized_window_overhang_is_clipped_to_desktop()
    {
        var layout = new ScreenLayout([M("P", 0, 0, 1920, 1080, true)]);
        var w = new WindowCandidate(1, "Max", new PixelRect(-8, -8, 1936, 1096), 0);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), WindowPicker.CaptureRegionFor(w, layout));
    }

    [Fact]
    public void Display_label_is_human_readable()
    {
        Assert.Equal("Display 1 (primary) — 2560×1440 at 150%", M("P", 0, 0, 2560, 1440, true, 1.5).DisplayLabel(0));
    }
}
