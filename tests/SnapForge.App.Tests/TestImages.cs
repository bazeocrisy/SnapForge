using SnapForge.Core.Imaging;

namespace SnapForge.App.Tests;

internal static class TestImages
{
    public static PixelBuffer Gradient(int w, int h)
    {
        var b = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                b.SetPixel(x, y, new ArgbColor(255, (byte)(x * 3), (byte)(y * 5), (byte)((x + y) * 2)));
            }
        }

        return b;
    }
}
