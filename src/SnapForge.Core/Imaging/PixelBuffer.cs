using SnapForge.Core.Geometry;

namespace SnapForge.Core.Imaging;

/// <summary>
/// A tightly packed 32-bit BGRA image (byte order B, G, R, A; stride = width * 4).
/// This is the canonical in-memory screenshot. Redaction writes directly into it, which is what
/// makes redaction permanent: there is no hidden layer underneath to recover.
/// </summary>
public sealed class PixelBuffer
{
    public const int BytesPerPixel = 4;

    public PixelBuffer(int width, int height)
        : this(width, height, Allocate(width, height))
    {
    }

    public PixelBuffer(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        }

        ImageBudget.Ensure(width, height);
        if (pixels.Length != (long)width * height * BytesPerPixel)
        {
            throw new ArgumentException("Pixel array length does not match width * height * 4.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * BytesPerPixel;
    public PixelRect Bounds => new(0, 0, Width, Height);

    /// <summary>Raw BGRA bytes. Exposed for zero-copy interop with the UI layer.</summary>
#pragma warning disable CA1819 // Interop surface: copying multi-megabyte buffers on every access is not acceptable.
    public byte[] Pixels { get; }
#pragma warning restore CA1819

    public ArgbColor GetPixel(int x, int y)
    {
        int i = IndexOf(x, y);
        return new ArgbColor(Pixels[i + 3], Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    public void SetPixel(int x, int y, ArgbColor color)
    {
        int i = IndexOf(x, y);
        Pixels[i] = color.B;
        Pixels[i + 1] = color.G;
        Pixels[i + 2] = color.R;
        Pixels[i + 3] = color.A;
    }

    /// <summary>
    /// GDI leaves the alpha byte undefined (usually 0) when copying the screen into a 32-bpp DIB.
    /// Screenshots are opaque, so force alpha to 255 to avoid invisible PNGs.
    /// </summary>
    public void ForceOpaque()
    {
        byte[] p = Pixels;
        for (int i = 3; i < p.Length; i += BytesPerPixel)
        {
            p[i] = 255;
        }
    }

    /// <summary>Returns a copy of the given region. The region is clipped to the image.</summary>
    public PixelBuffer Crop(PixelRect region)
    {
        PixelRect r = region.Intersect(Bounds);
        if (r.IsEmpty)
        {
            throw new ArgumentException($"Crop region {region} does not overlap the {Width}×{Height} image.", nameof(region));
        }

        var result = new PixelBuffer(r.Width, r.Height);
        int rowBytes = r.Width * BytesPerPixel;
        for (int row = 0; row < r.Height; row++)
        {
            Buffer.BlockCopy(Pixels, IndexOf(r.X, r.Y + row), result.Pixels, row * result.Stride, rowBytes);
        }

        return result;
    }

    /// <summary>
    /// Copies <paramref name="source"/> into this buffer with its top-left corner at (destX, destY),
    /// clipping anything that falls outside.
    /// </summary>
    public void Blit(PixelBuffer source, int destX, int destY)
    {
        ArgumentNullException.ThrowIfNull(source);
        PixelRect dest = new PixelRect(destX, destY, source.Width, source.Height).Intersect(Bounds);
        if (dest.IsEmpty)
        {
            return;
        }

        int rowBytes = dest.Width * BytesPerPixel;
        for (int row = 0; row < dest.Height; row++)
        {
            int srcIndex = source.IndexOf(dest.X - destX, dest.Y - destY + row);
            Buffer.BlockCopy(source.Pixels, srcIndex, Pixels, IndexOf(dest.X, dest.Y + row), rowBytes);
        }
    }

    /// <summary>Overwrites every pixel of <paramref name="region"/> (clipped) with an opaque color.</summary>
    /// <returns>The clipped region that was actually written.</returns>
    public PixelRect FillRect(PixelRect region, ArgbColor color)
    {
        PixelRect r = region.Intersect(Bounds);
        if (r.IsEmpty)
        {
            return r;
        }

        // Redaction must be opaque, otherwise the original could show through when composited.
        ArgbColor opaque = color with { A = 255 };
        for (int y = r.Y; y < r.Bottom; y++)
        {
            int i = IndexOf(r.X, y);
            for (int x = 0; x < r.Width; x++, i += BytesPerPixel)
            {
                Pixels[i] = opaque.B;
                Pixels[i + 1] = opaque.G;
                Pixels[i + 2] = opaque.R;
                Pixels[i + 3] = 255;
            }
        }

        return r;
    }

    public PixelBuffer Clone() => new(Width, Height, (byte[])Pixels.Clone());

    /// <summary>Validates the size against <see cref="ImageBudget"/> before allocating (FA-018).</summary>
    private static byte[] Allocate(int width, int height)
    {
        ImageBudget.Ensure(width, height);
        return new byte[(long)width * height * BytesPerPixel];
    }

    private int IndexOf(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Pixel ({x},{y}) is outside the {Width}×{Height} image.");
        }

        return (y * Width + x) * BytesPerPixel;
    }
}
