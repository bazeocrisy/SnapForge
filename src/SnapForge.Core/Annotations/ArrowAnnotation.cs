using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

public sealed record ArrowAnnotation(PointD Start, PointD End) : Annotation
{
    public ArrowGeometry Geometry => ArrowGeometry.Compute(Start, End, StrokeWidth);

    public override RectD Bounds => RectD.FromPoints(Start, End).Inflate(Geometry.HeadHalfWidth + StrokeWidth);

    public override bool HitTest(PointD point, double tolerance) =>
        point.DistanceToSegment(Start, End) <= StrokeWidth / 2 + tolerance
        || point.DistanceTo(End) <= Geometry.HeadLength + tolerance;

    public override Annotation Translate(double dx, double dy) =>
        this with { Start = Start + new PointD(dx, dy), End = End + new PointD(dx, dy) };

    public bool IsDegenerate => Start.DistanceTo(End) < MinimumShapeExtent;
}

/// <summary>Arrowhead triangle and shortened shaft for an arrow, so the shaft never pokes through the tip.</summary>
public readonly record struct ArrowGeometry(PointD ShaftStart, PointD ShaftEnd, PointD Tip, PointD Left, PointD Right, double HeadLength, double HeadHalfWidth)
{
    public static ArrowGeometry Compute(PointD start, PointD end, double strokeWidth)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double headLength = Math.Max(12, strokeWidth * 4);
        if (length < headLength * 1.5)
        {
            // Short arrows: shrink the head so it never exceeds two thirds of the arrow.
            headLength = length * 2 / 3;
        }

        double headHalfWidth = headLength * 0.6;
        if (length <= double.Epsilon)
        {
            return new ArrowGeometry(start, end, end, end, end, 0, 0);
        }

        double ux = dx / length, uy = dy / length;
        var basePoint = new PointD(end.X - ux * headLength, end.Y - uy * headLength);
        var left = new PointD(basePoint.X + uy * headHalfWidth, basePoint.Y - ux * headHalfWidth);
        var right = new PointD(basePoint.X - uy * headHalfWidth, basePoint.Y + ux * headHalfWidth);

        // End the shaft slightly inside the head so the joint is seamless but the tip stays sharp.
        var shaftEnd = new PointD(end.X - ux * headLength * 0.8, end.Y - uy * headLength * 0.8);
        return new ArrowGeometry(start, shaftEnd, end, left, right, headLength, headHalfWidth);
    }
}
