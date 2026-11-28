using SnapForge.Core.Geometry;

namespace SnapForge.Core.Tests;

/// <summary>FA-003 / SF-007: windows are kept inside the monitor work area (title bar and close button visible).</summary>
public class WindowFitTests
{
    private static readonly PixelRect Work1080 = new(0, 0, 1920, 1032); // 1920×1080 with a 48-px taskbar

    [Fact]
    public void FA003_editor_default_size_at_150_percent_is_shrunk_and_title_bar_visible()
    {
        // 1280×820 DIP at 150 % = 1920×1230 px, centred by Windows → top edge above the screen.
        var centred = new PixelRect(0, (1032 - 1230) / 2, 1920, 1230);
        Assert.False(WindowFit.CaptionVisible(centred, Work1080, 45));
        PixelRect fitted = WindowFit.Fit(centred, Work1080);
        Assert.Equal(new PixelRect(0, 0, 1920, 1032), fitted);
        Assert.True(WindowFit.CaptionVisible(fitted, Work1080, 45));
    }

    [Theory]
    [InlineData(-300, -200)]  // above-left of the screen
    [InlineData(1800, 900)]   // mostly off the bottom-right
    [InlineData(-5000, 4000)] // a monitor that was unplugged
    public void FA003_window_moved_off_screen_is_brought_fully_inside(int x, int y)
    {
        PixelRect fitted = WindowFit.Fit(new PixelRect(x, y, 800, 600), Work1080);
        Assert.Equal(new PixelRect(Math.Clamp(x, 0, 1120), Math.Clamp(y, 0, 432), 800, 600), fitted);
        Assert.True(WindowFit.CaptionVisible(fitted, Work1080, 45));
    }

    [Fact]
    public void FA003_window_that_fits_is_unchanged()
    {
        var r = new PixelRect(100, 50, 1000, 700);
        Assert.Equal(r, WindowFit.Fit(r, Work1080));
    }

    [Fact]
    public void FA003_secondary_monitor_with_negative_origin_and_top_taskbar()
    {
        var work = new PixelRect(-2560, 40, 2560, 1400); // left monitor, taskbar at the top
        PixelRect fitted = WindowFit.Fit(new PixelRect(-2600, 0, 3000, 1500), work);
        Assert.Equal(work, fitted);
    }

    [Fact]
    public void FA003_degenerate_inputs_are_returned_unchanged()
    {
        var r = new PixelRect(10, 10, 100, 100);
        Assert.Equal(r, WindowFit.Fit(r, new PixelRect(0, 0, 0, 0)));
    }
}

/// <summary>Isolated data profiles (remediation directive §3): names cannot escape the profiles folder.</summary>
public class ProfileNameTests
{
    [Theory]
    [InlineData("ci", true)]
    [InlineData("tester-2_b", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData(@"a\b", false)]
    [InlineData("C:", false)]
    [InlineData("this-name-is-longer-than-thirty-two-chars", false)]
    public void Profile_names_are_strictly_validated(string name, bool valid) =>
        Assert.Equal(valid, ProductInfo.IsValidProfileName(name));

    [Fact]
    public void Invalid_profile_is_rejected() =>
        Assert.Throws<ArgumentException>(() => ProductInfo.UseProfile(@"..\..\Windows"));
}
