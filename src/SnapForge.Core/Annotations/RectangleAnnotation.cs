using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>Rectangle outline.</summary>
public sealed record RectangleAnnotation(RectD Rect) : Annotation
{
    public override RectD Bounds => Rect.Inflate(StrokeWidth);

    public override bool HitTest(PointD point, double tolerance)
    {
        double band = StrokeWidth / 2 + tolerance;
        RectD outer = Rect.Inflate(band);
        RectD inner = Rect.Inflate(-band);
        bool insideInner = inner.Width > 0 && inner.Height > 0 && inner.Contains(point);
        return outer.Contains(point) && !insideInner;
    }

    public override Annotation Translate(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };

    public bool IsDegenerate => Rect.Width < MinimumShapeExtent || Rect.Height < MinimumShapeExtent;
}
