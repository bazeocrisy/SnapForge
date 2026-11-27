using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>A freehand pen stroke: a polyline through the recorded pointer positions.</summary>
public sealed record FreehandAnnotation : Annotation
{
    /// <summary>Strokes keep at most this many points (long strokes are thinned evenly).</summary>
    public const int MaxPoints = 4000;

    public FreehandAnnotation(IReadOnlyList<PointD> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("A stroke needs at least one point.", nameof(points));
        }

        Points = points.Count <= MaxPoints ? [.. points] : [.. Enumerable.Range(0, MaxPoints).Select(i => points[(int)((long)i * (points.Count - 1) / (MaxPoints - 1))])];
    }

    public IReadOnlyList<PointD> Points { get; init; }

    public override RectD Bounds
    {
        get
        {
            double l = Points.Min(p => p.X), t = Points.Min(p => p.Y), r = Points.Max(p => p.X), b = Points.Max(p => p.Y);
            return new RectD(l, t, r - l, b - t).Inflate(StrokeWidth / 2);
        }
    }

    public override bool HitTest(PointD point, double tolerance)
    {
        double limit = tolerance + (StrokeWidth / 2);
        if (Points.Count == 1)
        {
            return Distance(point, Points[0]) <= limit;
        }

        for (int i = 1; i < Points.Count; i++)
        {
            if (SegmentDistance(point, Points[i - 1], Points[i]) <= limit)
            {
                return true;
            }
        }

        return false;
    }

    public override Annotation Translate(double dx, double dy) => this with { Points = [.. Points.Select(p => new PointD(p.X + dx, p.Y + dy))] };

    /// <summary>Returns a copy with every point mapped (used by whole-image transforms).</summary>
    public FreehandAnnotation Map(Func<PointD, PointD> map) => this with { Points = [.. Points.Select(map)] };

    private static double Distance(PointD a, PointD b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double SegmentDistance(PointD p, PointD a, PointD b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = (dx * dx) + (dy * dy);
        if (len2 == 0)
        {
            return Distance(p, a);
        }

        double t = Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / len2, 0, 1);
        return Distance(p, new PointD(a.X + (t * dx), a.Y + (t * dy)));
    }
}
