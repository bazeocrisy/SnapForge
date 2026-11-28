namespace SnapForge.Core.Imaging;

/// <summary>
/// Turns decoded image pixels into an opaque capture. Only pixels are kept: the decoder's metadata (EXIF, GPS,
/// text chunks, ICC names) never reaches the Library. Transparent areas are flattened onto white, matching what
/// most apps show for a transparent PNG.
/// </summary>
public static class ImageImport
{
    /// <summary>Largest side accepted (pixels).</summary>
    public const int MaxSide = 16384;

    /// <summary>Largest total size accepted (pixels) — 100 megapixels ≈ 400 MB uncompressed.</summary>
    public const long MaxPixels = 100_000_000;

    /// <summary>File extensions SnapForge imports.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".jpe", ".bmp"];

    public static bool IsSupportedPath(string path) =>
        Extensions.Contains(Path.GetExtension(path ?? string.Empty), StringComparer.OrdinalIgnoreCase);

    /// <summary>Null if the dimensions are acceptable, otherwise a user-readable reason.</summary>
    public static string? CheckSize(int width, int height) =>
        width <= 0 || height <= 0 ? "The image is empty."
        : width > MaxSide || height > MaxSide ? $"The image is too large ({width}×{height}); the limit is {MaxSide} pixels per side."
        : (long)width * height > MaxPixels ? $"The image is too large ({width}×{height}); the limit is {MaxPixels / 1_000_000} megapixels."
        : null;

    /// <summary>Converts straight-alpha BGRA (as decoders return for Bgra32) to an opaque capture over white.</summary>
    public static PixelBuffer FromStraightBgra(int width, int height, byte[] bgra)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (CheckSize(width, height) is { } problem)
        {
            throw new ArgumentException(problem, nameof(width));
        }

        if (bgra.Length != width * height * PixelBuffer.BytesPerPixel)
        {
            throw new ArgumentException("Pixel data does not match the dimensions.", nameof(bgra));
        }

        byte[] output = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255)
            {
                output[i] = bgra[i];
                output[i + 1] = bgra[i + 1];
                output[i + 2] = bgra[i + 2];
            }
            else
            {
                int inv = 255 - a;
                output[i] = (byte)(((bgra[i] * a) + (255 * inv) + 127) / 255);
                output[i + 1] = (byte)(((bgra[i + 1] * a) + (255 * inv) + 127) / 255);
                output[i + 2] = (byte)(((bgra[i + 2] * a) + (255 * inv) + 127) / 255);
            }

            output[i + 3] = 255;
        }

        return new PixelBuffer(width, height, output);
    }

    /// <summary>Library title for an imported file: its name without extension.</summary>
    public static string TitleFor(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path ?? string.Empty).Trim();
        return name.Length == 0 ? "Imported image" : name;
    }
}
