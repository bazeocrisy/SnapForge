namespace SnapForge.Core.Imaging;

/// <summary>
/// Composites a rendered annotation layer over the screenshot. Doing the blend ourselves (instead of
/// asking the UI toolkit to redraw the photo) guarantees untouched pixels are bit-identical to the
/// capture and that redacted pixels stay exactly as redacted.
/// </summary>
public static class ImageCompositor
{
    /// <summary>
    /// Returns a new opaque image = <paramref name="background"/> with <paramref name="overlayPremultipliedBgra"/>
    /// (premultiplied BGRA, same dimensions, e.g. WPF Pbgra32) blended on top using source-over.
    /// </summary>
    public static PixelBuffer ComposeOver(PixelBuffer background, byte[] overlayPremultipliedBgra)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(overlayPremultipliedBgra);
        if (overlayPremultipliedBgra.Length != background.Pixels.Length)
        {
            throw new ArgumentException("Overlay dimensions must match the background.", nameof(overlayPremultipliedBgra));
        }

        byte[] dst = (byte[])background.Pixels.Clone();
        byte[] src = overlayPremultipliedBgra;
        for (int i = 0; i < dst.Length; i += PixelBuffer.BytesPerPixel)
        {
            int a = src[i + 3];
            if (a == 0)
            {
                dst[i + 3] = 255;
                continue;
            }

            if (a == 255)
            {
                dst[i] = src[i];
                dst[i + 1] = src[i + 1];
                dst[i + 2] = src[i + 2];
                dst[i + 3] = 255;
                continue;
            }

            int inv = 255 - a;
            dst[i] = Clamp(src[i] + Div255(dst[i] * inv));
            dst[i + 1] = Clamp(src[i + 1] + Div255(dst[i + 1] * inv));
            dst[i + 2] = Clamp(src[i + 2] + Div255(dst[i + 2] * inv));
            dst[i + 3] = 255;
        }

        return new PixelBuffer(background.Width, background.Height, dst);
    }

    /// <summary>Exact rounding division by 255 for values in [0, 65025].</summary>
    internal static int Div255(int v) => (v + 128 + ((v + 128) >> 8)) >> 8;

    private static byte Clamp(int v) => (byte)(v > 255 ? 255 : v);
}
