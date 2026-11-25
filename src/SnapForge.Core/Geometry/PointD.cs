namespace SnapForge.Core.Geometry;

/// <summary>A point in image-pixel space. Fractional values are allowed (sub-pixel mouse input).</summary>
public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);

    public double DistanceTo(PointD other)
    {
        double dx = other.X - X, dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Shortest distance from this point to the segment a–b.</summary>
    public double DistanceToSegment(PointD a, PointD b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double lengthSquared = vx * vx + vy * vy;
        if (lengthSquared <= double.Epsilon)
        {
            return DistanceTo(a);
        }

        double t = ((X - a.X) * vx + (Y - a.Y) * vy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return DistanceTo(new PointD(a.X + t * vx, a.Y + t * vy));
    }
}
