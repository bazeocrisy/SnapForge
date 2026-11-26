namespace SnapForge.Core.Geometry;

/// <summary>
/// An integer rectangle in physical pixels. Used for screen coordinates (virtual-desktop space,
/// which can have negative origins on multi-monitor systems) and for image pixel regions.
/// </summary>
public readonly record struct PixelRect
{
    public PixelRect(int x, int y, int width, int height)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be non-negative.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width == 0 || Height == 0;
    public long Area => (long)Width * Height;

    public static PixelRect Empty => default;

    public static PixelRect FromLtrb(int left, int top, int right, int bottom) =>
        right <= left || bottom <= top ? new PixelRect(left, top, 0, 0) : new PixelRect(left, top, right - left, bottom - top);

    /// <summary>Normalized rectangle spanning two drag points (inclusive of both pixels).</summary>
    public static PixelRect FromDragPoints(int x1, int y1, int x2, int y2)
    {
        int left = Math.Min(x1, x2), top = Math.Min(y1, y2);
        int right = Math.Max(x1, x2), bottom = Math.Max(y1, y2);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right), bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : new PixelRect(left, top, right - left, bottom - top);
    }

    public bool IntersectsWith(PixelRect other) => !Intersect(other).IsEmpty;

    public static PixelRect Union(IEnumerable<PixelRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);
        bool any = false;
        int left = 0, top = 0, right = 0, bottom = 0;
        foreach (PixelRect r in rects)
        {
            if (r.IsEmpty)
            {
                continue;
            }

            if (!any)
            {
                (left, top, right, bottom, any) = (r.X, r.Y, r.Right, r.Bottom, true);
            }
            else
            {
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                right = Math.Max(right, r.Right);
                bottom = Math.Max(bottom, r.Bottom);
            }
        }

        return any ? new PixelRect(left, top, right - left, bottom - top) : Empty;
    }

    public PixelRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public override string ToString() => $"{X},{Y} {Width}×{Height}";
}
