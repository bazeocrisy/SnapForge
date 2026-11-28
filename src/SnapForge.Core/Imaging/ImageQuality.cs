using SnapForge.Core.Geometry;

namespace SnapForge.Core.Imaging;

/// <summary>Fidelity of one region of a decoded frame against its source (luma, BT.601 weights).</summary>
/// <param name="PsnrDb">Peak signal-to-noise ratio in dB (capped at <see cref="ImageQuality.MaxPsnr"/> for identical pixels).</param>
/// <param name="Ssim">Mean structural similarity over 8×8 windows (step 4); 1 = identical.</param>
/// <param name="MeanAbsError">Mean absolute luma difference (0–255).</param>
public readonly record struct RegionQuality(double PsnrDb, double Ssim, double MeanAbsError);

/// <summary>
/// Simple, dependency-free image fidelity metrics for the recording quality comparison (<c>--video-quality</c>):
/// PSNR, SSIM (Wang et al. 2004 constants, uniform 8×8 windows) and mean absolute error on luma. Not a perceptual
/// model — enough to rank two encodings of the same source region.
/// </summary>
public static class ImageQuality
{
    public const double MaxPsnr = 99.0;

    private const int Window = 8;
    private const int Step = 4;
    private const double C1 = (0.01 * 255) * (0.01 * 255);
    private const double C2 = (0.03 * 255) * (0.03 * 255);

    /// <summary>Compares <paramref name="region"/> of two same-size BGRA buffers.</summary>
    public static RegionQuality Compare(PixelBuffer reference, PixelBuffer test, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(test);
        if (reference.Width != test.Width || reference.Height != test.Height)
        {
            throw new ArgumentException("The images must have the same size.", nameof(test));
        }

        PixelRect r = region.Intersect(reference.Bounds);
        if (r.Width <= 0 || r.Height <= 0)
        {
            throw new ArgumentException("The region is outside the image.", nameof(region));
        }

        double[] a = Luma(reference, r), b = Luma(test, r);
        double se = 0, ae = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            se += d * d;
            ae += Math.Abs(d);
        }

        double mse = se / a.Length;
        double psnr = mse <= 1e-12 ? MaxPsnr : Math.Min(MaxPsnr, 10 * Math.Log10(255.0 * 255.0 / mse));
        return new RegionQuality(Math.Round(psnr, 2), Math.Round(Ssim(a, b, r.Width, r.Height), 4), Math.Round(ae / a.Length, 3));
    }

    /// <summary>Luma (0–255) of a region, row-major.</summary>
    public static double[] Luma(PixelBuffer image, PixelRect r)
    {
        ArgumentNullException.ThrowIfNull(image);
        var y = new double[r.Width * r.Height];
        byte[] p = image.Pixels;
        for (int row = 0; row < r.Height; row++)
        {
            int o = (((r.Y + row) * image.Width) + r.X) * 4;
            for (int x = 0; x < r.Width; x++, o += 4)
            {
                y[(row * r.Width) + x] = (0.114 * p[o]) + (0.587 * p[o + 1]) + (0.299 * p[o + 2]);
            }
        }

        return y;
    }

    private static double Ssim(double[] a, double[] b, int w, int h)
    {
        if (w < Window || h < Window)
        {
            return WindowSsim(a, b, w, 0, 0, w, h);
        }

        double sum = 0;
        int n = 0;
        for (int y = 0; y + Window <= h; y += Step)
        {
            for (int x = 0; x + Window <= w; x += Step)
            {
                sum += WindowSsim(a, b, w, x, y, Window, Window);
                n++;
            }
        }

        return sum / n;
    }

    private static double WindowSsim(double[] a, double[] b, int stride, int x0, int y0, int ww, int wh)
    {
        double ma = 0, mb = 0;
        int n = ww * wh;
        for (int y = y0; y < y0 + wh; y++)
        {
            for (int x = x0; x < x0 + ww; x++)
            {
                ma += a[(y * stride) + x];
                mb += b[(y * stride) + x];
            }
        }

        ma /= n;
        mb /= n;
        double va = 0, vb = 0, cov = 0;
        for (int y = y0; y < y0 + wh; y++)
        {
            for (int x = x0; x < x0 + ww; x++)
            {
                double da = a[(y * stride) + x] - ma, db = b[(y * stride) + x] - mb;
                va += da * da;
                vb += db * db;
                cov += da * db;
            }
        }

        va /= n - 1 == 0 ? 1 : n - 1;
        vb /= n - 1 == 0 ? 1 : n - 1;
        cov /= n - 1 == 0 ? 1 : n - 1;
        return ((2 * ma * mb) + C1) * ((2 * cov) + C2) / (((ma * ma) + (mb * mb) + C1) * (va + vb + C2));
    }
}
