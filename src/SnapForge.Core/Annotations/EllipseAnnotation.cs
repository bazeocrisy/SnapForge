using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>Ellipse outline inscribed in <see cref="Rect"/>.</summary>
public sealed record EllipseAnnotation(RectD Rect) : Annotation
{
    public override RectD Bounds => Rect.Inflate(StrokeWidth);

    public override bool HitTest(PointD point, double tolerance)
    {
        double band = StrokeWidth / 2 + tolerance;
        PointD c = Rect.Center;
        double rx = Rect.Width / 2, ry = Rect.Height / 2;
        return Inside(point, c, rx + band, ry + band) && !Inside(point, c, rx - band, ry - band);
    }

    public override Annotation Translate(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };

    public bool IsDegenerate => Rect.Width < MinimumShapeExtent || Rect.Height < MinimumShapeExtent;

    private static bool Inside(PointD p, PointD c, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0)
        {
            return false;
        }

        double nx = (p.X - c.X) / rx, ny = (p.Y - c.Y) / ry;
        return nx * nx + ny * ny <= 1;
    }
}
