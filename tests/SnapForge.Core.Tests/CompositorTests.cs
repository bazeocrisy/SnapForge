using SnapForge.Core.Imaging;

namespace SnapForge.Core.Tests;

public class CompositorTests
{
    [Fact]
    public void Transparent_overlay_leaves_pixels_bit_identical()
    {
        PixelBuffer bg = PixelBufferTests.Gradient(16, 9);
        PixelBuffer result = ImageCompositor.ComposeOver(bg, new byte[bg.Pixels.Length]);
        Assert.Equal(bg.Pixels, result.Pixels);
        Assert.NotSame(bg.Pixels, result.Pixels);
    }

    [Fact]
    public void Opaque_overlay_replaces_pixels()
    {
        var bg = new PixelBuffer(1, 1);
        bg.SetPixel(0, 0, ArgbColor.White);
        PixelBuffer result = ImageCompositor.ComposeOver(bg, [10, 20, 30, 255]);
        Assert.Equal(new ArgbColor(255, 30, 20, 10), result.GetPixel(0, 0));
    }

    [Fact]
    public void Half_alpha_premultiplied_red_over_white_is_pink()
    {
        var bg = new PixelBuffer(1, 1);
        bg.SetPixel(0, 0, ArgbColor.White);
        // 50 % red premultiplied: R=128, A=128.
        PixelBuffer result = ImageCompositor.ComposeOver(bg, [0, 0, 128, 128]);
        ArgbColor c = result.GetPixel(0, 0);
        Assert.Equal(255, c.A);
        Assert.InRange(c.R, 254, 255);
        Assert.InRange(c.G, 126, 128);
        Assert.InRange(c.B, 126, 128);
    }

    [Fact]
    public void Output_is_always_opaque()
    {
        var bg = new PixelBuffer(2, 1); // alpha 0 in source
        PixelBuffer result = ImageCompositor.ComposeOver(bg, [0, 0, 0, 0, 5, 5, 5, 100]);
        Assert.Equal(255, result.GetPixel(0, 0).A);
        Assert.Equal(255, result.GetPixel(1, 0).A);
    }

    [Fact]
    public void Mismatched_overlay_size_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ImageCompositor.ComposeOver(new PixelBuffer(2, 2), new byte[4]));
    }

    [Fact]
    public void Div255_is_exact_rounding()
    {
        for (int v = 0; v <= 255 * 255; v++)
        {
            Assert.Equal((int)Math.Round(v / 255.0, MidpointRounding.AwayFromZero), ImageCompositor.Div255(v));
        }
    }
}
