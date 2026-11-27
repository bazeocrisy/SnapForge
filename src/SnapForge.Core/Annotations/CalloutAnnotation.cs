using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>
/// A speech-bubble callout: a rounded box with text and a pointer (tail) to <see cref="Tail"/>. The box is filled with
/// white and outlined in <see cref="Annotation.Color"/>; the text is dark for legibility.
/// </summary>
public sealed record CalloutAnnotation(RectD Box, PointD Tail, string Text, double FontSize) : Annotation
{
    public override RectD Bounds
    {
        get
        {
            double l = Math.Min(Box.X, Tail.X), t = Math.Min(Box.Y, Tail.Y);
            double r = Math.Max(Box.Right, Tail.X), b = Math.Max(Box.Bottom, Tail.Y);
            return new RectD(l, t, r - l, b - t).Inflate(StrokeWidth / 2);
        }
    }

    public override bool HitTest(PointD point, double tolerance) =>
        Box.Inflate(tolerance).Contains(point) || SegmentHit(point, Box.Center, Tail, tolerance + StrokeWidth);

    public override Annotation Translate(double dx, double dy) =>
        this with { Box = new RectD(Box.X + dx, Box.Y + dy, Box.Width, Box.Height), Tail = new PointD(Tail.X + dx, Tail.Y + dy) };

    private static bool SegmentHit(PointD p, PointD a, PointD b, double limit)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = (dx * dx) + (dy * dy);
        double t = len2 == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / len2, 0, 1);
        double cx = a.X + (t * dx) - p.X, cy = a.Y + (t * dy) - p.Y;
        return (cx * cx) + (cy * cy) <= limit * limit;
    }
}
