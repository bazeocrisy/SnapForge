using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Tests;

public class PixelBufferTests
{
    internal static PixelBuffer Gradient(int w, int h)
    {
        var b = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                b.SetPixel(x, y, new ArgbColor(255, (byte)(x * 7), (byte)(y * 11), (byte)(x + y)));
            }
        }

        return b;
    }

    [Fact]
    public void Constructor_validates_buffer_length()
    {
        Assert.Throws<ArgumentException>(() => new PixelBuffer(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(0, 2));
    }

    [Fact]
    public void Pixel_layout_is_bgra()
    {
        var b = new PixelBuffer(1, 1);
        b.SetPixel(0, 0, new ArgbColor(1, 2, 3, 4));
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, b.Pixels);
    }

    [Fact]
    public void ForceOpaque_sets_every_alpha_to_255_and_preserves_color()
    {
        var b = new PixelBuffer(3, 2);
        b.SetPixel(1, 1, new ArgbColor(0, 10, 20, 30));
        b.ForceOpaque();
        for (int i = 3; i < b.Pixels.Length; i += 4)
        {
            Assert.Equal(255, b.Pixels[i]);
        }

        Assert.Equal(new ArgbColor(255, 10, 20, 30), b.GetPixel(1, 1));
    }

    [Fact]
    public void Crop_copies_exact_region()
    {
        PixelBuffer b = Gradient(20, 10);
        PixelBuffer c = b.Crop(new PixelRect(5, 3, 4, 2));
        Assert.Equal(4, c.Width);
        Assert.Equal(2, c.Height);
        Assert.Equal(b.GetPixel(5, 3), c.GetPixel(0, 0));
        Assert.Equal(b.GetPixel(8, 4), c.GetPixel(3, 1));
    }

    [Fact]
    public void Crop_clips_to_image_and_rejects_non_overlapping()
    {
        PixelBuffer b = Gradient(10, 10);
        PixelBuffer c = b.Crop(new PixelRect(-5, -5, 8, 8));
        Assert.Equal(3, c.Width);
        Assert.Equal(b.GetPixel(0, 0), c.GetPixel(0, 0));
        Assert.Throws<ArgumentException>(() => b.Crop(new PixelRect(20, 20, 5, 5)));
    }

    [Fact]
    public void FillRect_overwrites_region_only_and_is_opaque()
    {
        PixelBuffer b = Gradient(10, 10);
        PixelBuffer before = b.Clone();
        PixelRect written = b.FillRect(new PixelRect(2, 2, 3, 3), new ArgbColor(0, 1, 2, 3));
        Assert.Equal(new PixelRect(2, 2, 3, 3), written);
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                bool inside = x is >= 2 and < 5 && y is >= 2 and < 5;
                Assert.Equal(inside ? new ArgbColor(255, 1, 2, 3) : before.GetPixel(x, y), b.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void FillRect_clips_out_of_bounds_region()
    {
        var b = new PixelBuffer(4, 4);
        PixelRect written = b.FillRect(new PixelRect(-10, 2, 100, 100), ArgbColor.Black);
        Assert.Equal(new PixelRect(0, 2, 4, 2), written);
    }

    [Fact]
    public void Blit_round_trips_with_crop()
    {
        PixelBuffer b = Gradient(10, 10);
        PixelBuffer original = b.Clone();
        PixelBuffer patch = b.Crop(new PixelRect(3, 3, 4, 4));
        b.FillRect(new PixelRect(3, 3, 4, 4), ArgbColor.Black);
        b.Blit(patch, 3, 3);
        Assert.Equal(original.Pixels, b.Pixels);
    }

    [Fact]
    public void Blit_clips_partially_outside()
    {
        var dst = new PixelBuffer(4, 4);
        PixelBuffer src = Gradient(4, 4);
        dst.Blit(src, 2, -2);
        Assert.Equal(src.GetPixel(0, 2), dst.GetPixel(2, 0));
        Assert.Equal(new ArgbColor(0, 0, 0, 0), dst.GetPixel(0, 0));
    }

    [Theory]
    [InlineData("#FF8800", 255, 255, 136, 0)]
    [InlineData("80102030", 128, 16, 32, 48)]
    public void ArgbColor_parses_hex(string hex, byte a, byte r, byte g, byte b) =>
        Assert.Equal(new ArgbColor(a, r, g, b), ArgbColor.Parse(hex));

    [Fact]
    public void ArgbColor_rejects_bad_length() => Assert.Throws<FormatException>(() => ArgbColor.Parse("#FFF"));
}
