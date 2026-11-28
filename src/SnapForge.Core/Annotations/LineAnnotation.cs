using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

public sealed record LineAnnotation(PointD Start, PointD End) : Annotation
{
    public override RectD Bounds => RectD.FromPoints(Start, End).Inflate(StrokeWidth);

    public override bool HitTest(PointD point, double tolerance) =>
        point.DistanceToSegment(Start, End) <= StrokeWidth / 2 + tolerance;

    public override Annotation Translate(double dx, double dy) =>
        this with { Start = Start + new PointD(dx, dy), End = End + new PointD(dx, dy) };

    public bool IsDegenerate => Start.DistanceTo(End) < MinimumShapeExtent;
}
