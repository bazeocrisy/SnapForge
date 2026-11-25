namespace SnapForge.Core.Geometry;

/// <summary>An axis-aligned rectangle in image-pixel space with non-negative size.</summary>
public readonly record struct RectD
{
    public RectD(double x, double y, double width, double height)
    {
        if (width < 0 || height < 0 || double.IsNaN(width) || double.IsNaN(height))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be non-negative.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>Builds a normalized rectangle from two arbitrary corners (any drag direction).</summary>
    public static RectD FromPoints(PointD a, PointD b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public bool Contains(PointD p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public RectD Inflate(double amount)
    {
        double w = Math.Max(0, Width + 2 * amount), h = Math.Max(0, Height + 2 * amount);
        return new RectD(X - amount, Y - amount, w, h);
    }

    public RectD Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>
    /// Smallest integer pixel rectangle that fully covers this rectangle, so redaction never leaves
    /// a partially covered edge pixel unredacted.
    /// </summary>
    public PixelRect ToCoveringPixelRect()
    {
        int left = (int)Math.Floor(X), top = (int)Math.Floor(Y);
        int right = (int)Math.Ceiling(Right), bottom = (int)Math.Ceiling(Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}
