namespace SnapForge.Core.Imaging;

/// <summary>
/// The memory budget for one decoded image (FA-018). Every <see cref="PixelBuffer"/>, every decoded
/// <see cref="Storage.PixelCodec"/> file and every <see cref="ImageOps"/> result is limited to
/// <see cref="MaxPixels"/> = 268,435,456 pixels, i.e. 1 GiB of BGRA32 (16384 × 16384). This covers 8K screens
/// (33 MP), 5000 × 3000, and the scrolling-capture limit of 120 million pixels with room to spare, while a damaged or
/// hostile file can no longer make SnapForge try to allocate 4 GB. Sizes are checked with 64-bit arithmetic before
/// anything is allocated. Operations that need scratch memory besides their result (drop shadow: one int per
/// pixel; resize: row weights) stay within about twice the budget.
/// </summary>
public static class ImageBudget
{
    /// <summary>Largest pixel count of any image SnapForge holds in memory.</summary>
    public const long MaxPixels = 268_435_456;

    /// <summary>Bytes of BGRA32 at <see cref="MaxPixels"/> (1 GiB).</summary>
    public const long MaxBytes = MaxPixels * PixelBuffer.BytesPerPixel;

    /// <summary>True if a <paramref name="width"/> × <paramref name="height"/> image is positive and within budget.</summary>
    public static bool Fits(long width, long height) =>
        width > 0 && height > 0 && width <= int.MaxValue && height <= int.MaxValue && width * height <= MaxPixels;

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> unless the size is positive and within budget.</summary>
    public static void Ensure(long width, long height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        }

        if (!Fits(width, height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Image size {width}×{height} exceeds the memory budget of {MaxPixels:N0} pixels (1 GiB).");
        }
    }
}
