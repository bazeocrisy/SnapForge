using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;

namespace SnapForge.Core.Tests;

public class AnnotationTests
{
    [Fact]
    public void Arrow_hit_test_follows_shaft_and_head()
    {
        var a = new ArrowAnnotation(new PointD(0, 0), new PointD(100, 0)) { StrokeWidth = 4 };
        Assert.True(a.HitTest(new PointD(50, 2), 2));
        Assert.True(a.HitTest(new PointD(98, 6), 2));
        Assert.False(a.HitTest(new PointD(50, 20), 2));
    }

    [Fact]
    public void Arrow_head_points_at_end_and_is_symmetric()
    {
        ArrowGeometry g = ArrowGeometry.Compute(new PointD(0, 0), new PointD(100, 0), 4);
        Assert.Equal(new PointD(100, 0), g.Tip);
        Assert.Equal(g.Left.X, g.Right.X, 6);
        Assert.Equal(-g.Left.Y, g.Right.Y, 6);
        Assert.True(g.ShaftEnd.X < 100);
        Assert.True(g.ShaftEnd.X > g.Left.X);
    }

    [Fact]
    public void Short_arrow_head_never_exceeds_arrow()
    {
        ArrowGeometry g = ArrowGeometry.Compute(new PointD(0, 0), new PointD(9, 0), 10);
        Assert.True(g.HeadLength <= 6.0001);
        Assert.True(g.Left.X >= 0);
    }

    [Fact]
    public void Zero_length_arrow_does_not_produce_nan()
    {
        ArrowGeometry g = ArrowGeometry.Compute(new PointD(5, 5), new PointD(5, 5), 4);
        Assert.False(double.IsNaN(g.Left.X));
        Assert.True(new ArrowAnnotation(new PointD(5, 5), new PointD(6, 5)).IsDegenerate);
    }

    [Fact]
    public void Rectangle_hit_test_only_on_outline()
    {
        var r = new RectangleAnnotation(new RectD(10, 10, 100, 50)) { StrokeWidth = 4 };
        Assert.True(r.HitTest(new PointD(10, 30), 2));
        Assert.True(r.HitTest(new PointD(60, 61), 2));
        Assert.False(r.HitTest(new PointD(60, 35), 2));
        Assert.False(r.HitTest(new PointD(200, 35), 2));
    }

    [Fact]
    public void Ellipse_hit_test_only_on_outline()
    {
        var e = new EllipseAnnotation(new RectD(0, 0, 100, 50)) { StrokeWidth = 4 };
        Assert.True(e.HitTest(new PointD(0, 25), 2));
        Assert.True(e.HitTest(new PointD(50, 50), 2));
        Assert.False(e.HitTest(new PointD(50, 25), 2));
        Assert.False(e.HitTest(new PointD(2, 2), 2));
    }

    [Fact]
    public void Translate_keeps_identity_and_style()
    {
        var a = new LineAnnotation(new PointD(0, 0), new PointD(10, 10)) { StrokeWidth = 7 };
        var moved = (LineAnnotation)a.Translate(5, -5);
        Assert.Equal(a.Id, moved.Id);
        Assert.Equal(7, moved.StrokeWidth);
        Assert.Equal(new PointD(5, -5), moved.Start);
        Assert.Equal(new PointD(15, 5), moved.End);
    }

    [Fact]
    public void Text_bounds_include_padding_and_measured_size()
    {
        var t = new TextAnnotation(new PointD(100, 100), "Hello", 20) { MeasuredSize = new PointD(60, 26) };
        Assert.Equal(new RectD(94, 94, 72, 38), t.Bounds);
        Assert.True(t.HitTest(new PointD(130, 110), 0));
    }

    [Fact]
    public void Step_numbering_continues_after_highest()
    {
        Annotation[] items =
        [
            new StepMarkerAnnotation(new PointD(0, 0), 1, 14),
            new StepMarkerAnnotation(new PointD(0, 0), 3, 14),
            new LineAnnotation(new PointD(0, 0), new PointD(1, 1)),
        ];
        Assert.Equal(4, StepNumbering.Next(items));
        Assert.Equal(1, StepNumbering.Next([]));
    }

    [Fact]
    public void Step_marker_radius_is_clamped()
    {
        Assert.Equal(12, StepMarkerAnnotation.RadiusForStroke(1));
        Assert.Equal(40, StepMarkerAnnotation.RadiusForStroke(100));
    }
}
