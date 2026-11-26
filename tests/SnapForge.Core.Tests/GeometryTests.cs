using SnapForge.Core.Geometry;

namespace SnapForge.Core.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(10, 20, 50, 80)]
    [InlineData(50, 80, 10, 20)]
    [InlineData(50, 20, 10, 80)]
    [InlineData(10, 80, 50, 20)]
    public void FromDragPoints_normalizes_any_drag_direction(int x1, int y1, int x2, int y2)
    {
        PixelRect r = PixelRect.FromDragPoints(x1, y1, x2, y2);
        Assert.Equal(new PixelRect(10, 20, 40, 60), r);
    }

    [Fact]
    public void Intersect_of_disjoint_rects_is_empty()
    {
        Assert.True(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(10, 0, 10, 10)).IsEmpty);
    }

    [Fact]
    public void Intersect_handles_negative_virtual_desktop_coordinates()
    {
        var leftMonitor = new PixelRect(-1920, 0, 1920, 1080);
        var selection = PixelRect.FromDragPoints(-100, 100, 200, 300);
        Assert.Equal(new PixelRect(-100, 100, 100, 200), selection.Intersect(leftMonitor));
    }

    [Fact]
    public void Union_ignores_empty_rects()
    {
        var u = PixelRect.Union([new PixelRect(0, 0, 10, 10), PixelRect.Empty, new PixelRect(-5, 20, 5, 5)]);
        Assert.Equal(new PixelRect(-5, 0, 15, 25), u);
    }

    [Fact]
    public void Contains_is_half_open()
    {
        var r = new PixelRect(0, 0, 10, 10);
        Assert.True(r.Contains(0, 0));
        Assert.True(r.Contains(9, 9));
        Assert.False(r.Contains(10, 5));
        Assert.False(r.Contains(5, 10));
    }

    [Fact]
    public void Negative_size_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelRect(0, 0, -1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectD(0, 0, 1, -5));
    }

    [Fact]
    public void Covering_pixel_rect_includes_partially_covered_edge_pixels()
    {
        var r = new RectD(1.4, 2.6, 3.2, 1.1); // spans x 1.4–4.6, y 2.6–3.7
        Assert.Equal(new PixelRect(1, 2, 4, 2), r.ToCoveringPixelRect());
    }

    [Fact]
    public void Distance_to_segment_clamps_to_endpoints()
    {
        var a = new PointD(0, 0);
        var b = new PointD(10, 0);
        Assert.Equal(5, new PointD(5, 5).DistanceToSegment(a, b), 6);
        Assert.Equal(5, new PointD(-3, 4).DistanceToSegment(a, b), 6);
        Assert.Equal(5, new PointD(0, 5).DistanceToSegment(a, a), 6);
    }
}
