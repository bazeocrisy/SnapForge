namespace SnapForge.Core.Imaging;

public static class Thumbnailer
{
    /// <summary>
    /// Downscales with an area-average (box) filter so the longest side is at most <paramref name="maxSize"/>.
    /// Never upscales. Output is opaque.
    /// </summary>
    public static PixelBuffer Create(PixelBuffer source, int maxSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1);
        double scale = Math.Min(1.0, (double)maxSize / Math.Max(source.Width, source.Height));
        int w = Math.Max(1, (int)Math.Round(source.Width * scale));
        int h = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new PixelBuffer(w, h);
        byte[] src = source.Pixels, dst = result.Pixels;
        for (int ty = 0; ty < h; ty++)
        {
            int y0 = (int)((long)ty * source.Height / h), y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * source.Height / h));
            for (int tx = 0; tx < w; tx++)
            {
                int x0 = (int)((long)tx * source.Width / w), x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * source.Width / w));
                long b = 0, g = 0, r = 0, n = 0;
                for (int y = y0; y < y1; y++)
                {
                    int i = (y * source.Width + x0) * 4;
                    for (int x = x0; x < x1; x++, i += 4)
                    {
                        b += src[i];
                        g += src[i + 1];
                        r += src[i + 2];
                        n++;
                    }
                }

                int o = (ty * w + tx) * 4;
                dst[o] = (byte)(b / n);
                dst[o + 1] = (byte)(g / n);
                dst[o + 2] = (byte)(r / n);
                dst[o + 3] = 255;
            }
        }

        return result;
    }
}
