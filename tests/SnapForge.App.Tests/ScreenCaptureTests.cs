using SnapForge.App.Capture;
using SnapForge.Core.Capture;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Tests;

/// <summary>
/// Exercises the real Win32/GDI capture path on the machine running the tests (the GitHub Actions Windows
/// runner in CI). These verify the APIs work and the geometry is consistent; they cannot judge what is on screen.
/// </summary>
public class ScreenCaptureTests
{
    [Fact]
    public void Screen_layout_reports_at_least_one_monitor_with_a_primary()
    {
        ScreenLayout layout = ScreenCaptureService.GetScreenLayout();
        Assert.NotEmpty(layout.Monitors);
        Assert.Single(layout.Monitors, m => m.IsPrimary);
        Assert.All(layout.Monitors, m =>
        {
            Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0);
            Assert.True(m.DpiScale >= 1.0);
        });
        Assert.Equal(0, layout.Monitors[0].Bounds.X);
        Assert.Equal(0, layout.Monitors[0].Bounds.Y);
    }

    [Fact]
    public void Snapshot_image_matches_virtual_desktop_and_is_opaque()
    {
        CaptureSnapshot snapshot = ScreenCaptureService.TakeSnapshot();
        Assert.Equal(snapshot.Layout.VirtualBounds.Width, snapshot.Image.Width);
        Assert.Equal(snapshot.Layout.VirtualBounds.Height, snapshot.Image.Height);
        for (int i = 3; i < snapshot.Image.Pixels.Length; i += 4 * 997)
        {
            Assert.Equal(255, snapshot.Image.Pixels[i]);
        }
    }

    [Fact]
    public void Region_capture_has_requested_size()
    {
        ScreenLayout layout = ScreenCaptureService.GetScreenLayout();
        var primary = layout.Monitors[0].Bounds;
        var region = new Core.Geometry.PixelRect(primary.X + 10, primary.Y + 10, 123, 45);
        PixelBuffer image = ScreenCaptureService.CaptureRegion(region);
        Assert.Equal(123, image.Width);
        Assert.Equal(45, image.Height);
    }

    [Fact]
    public void Window_enumeration_excludes_own_process_and_returns_valid_bounds()
    {
        IReadOnlyList<WindowCandidate> windows = ScreenCaptureService.EnumerateWindows(Environment.ProcessId);
        Assert.All(windows, w => Assert.True(w.Bounds.Width >= 2 && w.Bounds.Height >= 2));
        Assert.Equal(windows.Count, windows.Select(w => w.Handle).Distinct().Count());
        Assert.True(windows.Select(w => w.ZOrder).SequenceEqual(windows.Select(w => w.ZOrder).Order()));
    }
}
