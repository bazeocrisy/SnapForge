using System.Diagnostics;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Tests;

public class ImageOpsTests
{
    private static readonly ArgbColor Gray = ArgbColor.FromRgb(100, 100, 100);

    /// <summary>Every pixel distinct, including the alpha channel.</summary>
    internal static PixelBuffer Unique(int w, int h)
    {
        var b = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                b.SetPixel(x, y, new ArgbColor((byte)(200 + x), (byte)x, (byte)y, (byte)(x * 13 + y * 7)));
            }
        }

        return b;
    }

    private static PixelBuffer Solid(int w, int h, ArgbColor c)
    {
        var b = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                b.SetPixel(x, y, c);
            }
        }

        return b;
    }

    private static PixelBuffer Row(params byte[] grays)
    {
        var b = new PixelBuffer(grays.Length, 1);
        for (int x = 0; x < grays.Length; x++)
        {
            b.SetPixel(x, 0, ArgbColor.FromRgb(grays[x], grays[x], grays[x]));
        }

        return b;
    }

    private static void AssertAll(PixelBuffer b, ArgbColor expected)
    {
        for (int y = 0; y < b.Height; y++)
        {
            for (int x = 0; x < b.Width; x++)
            {
                Assert.Equal(expected, b.GetPixel(x, y));
            }
        }
    }

    // ───── Lossless transforms ─────

    [Fact]
    public void Rotate90_clockwise_maps_every_pixel()
    {
        PixelBuffer src = Unique(5, 3);
        PixelBuffer r = ImageOps.Rotate90Clockwise(src);
        Assert.Equal((3, 5), (r.Width, r.Height));
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                Assert.Equal(src.GetPixel(x, y), r.GetPixel(3 - 1 - y, x));
            }
        }

        // Top-left goes to top-right.
        Assert.Equal(src.GetPixel(0, 0), r.GetPixel(2, 0));
    }

    [Fact]
    public void Rotate90_counter_clockwise_maps_every_pixel()
    {
        PixelBuffer src = Unique(5, 3);
        PixelBuffer r = ImageOps.Rotate90CounterClockwise(src);
        Assert.Equal((3, 5), (r.Width, r.Height));
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                Assert.Equal(src.GetPixel(x, y), r.GetPixel(y, 5 - 1 - x));
            }
        }
    }

    [Fact]
    public void Rotate180_and_flips_map_every_pixel()
    {
        PixelBuffer src = Unique(4, 3);
        PixelBuffer r = ImageOps.Rotate180(src), fh = ImageOps.FlipHorizontal(src), fv = ImageOps.FlipVertical(src);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(src.GetPixel(x, y), r.GetPixel(3 - x, 2 - y));
                Assert.Equal(src.GetPixel(x, y), fh.GetPixel(3 - x, y));
                Assert.Equal(src.GetPixel(x, y), fv.GetPixel(x, 2 - y));
            }
        }
    }

    [Fact]
    public void Four_rotations_and_double_flips_are_identity()
    {
        PixelBuffer src = Unique(7, 4);
        PixelBuffer r = src;
        for (int i = 0; i < 4; i++)
        {
            r = ImageOps.Rotate90Clockwise(r);
        }

        Assert.Equal(src.Pixels, r.Pixels);
        Assert.Equal(src.Pixels, ImageOps.Rotate90CounterClockwise(ImageOps.Rotate90Clockwise(src)).Pixels);
        Assert.Equal(src.Pixels, ImageOps.Rotate180(ImageOps.Rotate180(src)).Pixels);
        Assert.Equal(src.Pixels, ImageOps.FlipHorizontal(ImageOps.FlipHorizontal(src)).Pixels);
        Assert.Equal(src.Pixels, ImageOps.FlipVertical(ImageOps.FlipVertical(src)).Pixels);
        Assert.Equal(ImageOps.Rotate180(src).Pixels, ImageOps.FlipVertical(ImageOps.FlipHorizontal(src)).Pixels);
    }

    [Fact]
    public void Transforms_do_not_mutate_the_input()
    {
        PixelBuffer src = Unique(6, 5);
        byte[] copy = (byte[])src.Pixels.Clone();
        foreach (ImageTransform t in Enum.GetValues<ImageTransform>())
        {
            Assert.NotSame(src, ImageOps.Transform(src, t));
        }

        Assert.Equal(copy, src.Pixels);
    }

    [Theory]
    [InlineData(ImageTransform.Rotate90)]
    [InlineData(ImageTransform.Rotate180)]
    [InlineData(ImageTransform.Rotate270)]
    [InlineData(ImageTransform.FlipHorizontal)]
    [InlineData(ImageTransform.FlipVertical)]
    public void TransformPoint_and_TransformRect_agree_with_pixels(ImageTransform t)
    {
        PixelBuffer src = Unique(6, 4);
        PixelBuffer dst = ImageOps.Transform(src, t);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                PointD c = ImageOps.TransformPoint(new PointD(x + 0.5, y + 0.5), 6, 4, t);
                Assert.Equal(src.GetPixel(x, y), dst.GetPixel((int)Math.Floor(c.X), (int)Math.Floor(c.Y)));
                PixelRect pr = ImageOps.TransformRect(new PixelRect(x, y, 1, 1), 6, 4, t);
                Assert.Equal(1, pr.Width);
                Assert.Equal(1, pr.Height);
                Assert.Equal(src.GetPixel(x, y), dst.GetPixel(pr.X, pr.Y));
            }
        }

        PixelRect region = ImageOps.TransformRect(new PixelRect(1, 0, 3, 2), 6, 4, t);
        Assert.Equal(6, region.Area);
        Assert.True(dst.Bounds.Intersect(region) == region);
    }

    // ───── Resize ─────

    [Theory]
    [InlineData(10, 7, 3, 2)]
    [InlineData(10, 7, 23, 31)]
    [InlineData(10, 7, 4, 20)]
    [InlineData(9, 9, 9, 9)]
    public void Resize_of_a_solid_color_stays_solid(int w, int h, int nw, int nh)
    {
        foreach (ArgbColor c in new[] { ArgbColor.FromRgb(12, 200, 99), new ArgbColor(128, 40, 220, 7), new ArgbColor(0, 0, 0, 0) })
        {
            PixelBuffer r = ImageOps.Resize(Solid(w, h, c), nw, nh);
            Assert.Equal((nw, nh), (r.Width, r.Height));
            AssertAll(r, c);
        }
    }

    [Fact]
    public void Downscale_2x2_to_1x1_averages_all_four_pixels()
    {
        var src = new PixelBuffer(2, 2);
        src.SetPixel(0, 0, ArgbColor.FromRgb(10, 0, 200));
        src.SetPixel(1, 0, ArgbColor.FromRgb(20, 100, 200));
        src.SetPixel(0, 1, ArgbColor.FromRgb(30, 200, 0));
        src.SetPixel(1, 1, ArgbColor.FromRgb(40, 255, 0));
        Assert.Equal(ArgbColor.FromRgb(25, 139, 100), ImageOps.Resize(src, 1, 1).GetPixel(0, 0));
    }

    [Fact]
    public void Downscale_is_premultiplied_so_transparent_colors_do_not_bleed()
    {
        var src = new PixelBuffer(2, 2);
        src.SetPixel(0, 0, ArgbColor.FromRgb(255, 0, 0));
        src.SetPixel(1, 0, new ArgbColor(0, 0, 255, 0));
        src.SetPixel(0, 1, new ArgbColor(0, 0, 255, 0));
        src.SetPixel(1, 1, new ArgbColor(0, 0, 255, 0));
        Assert.Equal(new ArgbColor(64, 255, 0, 0), ImageOps.Resize(src, 1, 1).GetPixel(0, 0));
    }

    [Fact]
    public void Downscale_weights_partial_pixels_by_covered_area()
    {
        PixelBuffer r = ImageOps.Resize(Row(0, 90, 180), 2, 1);
        Assert.Equal(30, r.GetPixel(0, 0).R); // (0 * 1 + 90 * 0.5) / 1.5
        Assert.Equal(150, r.GetPixel(1, 0).R); // (90 * 0.5 + 180 * 1) / 1.5
        PixelBuffer half = ImageOps.Resize(Row(0, 100, 200, 250), 2, 1);
        Assert.Equal(50, half.GetPixel(0, 0).R);
        Assert.Equal(225, half.GetPixel(1, 0).R);
    }

    [Fact]
    public void Upscale_is_bilinear_with_aligned_centers_and_clamped_edges()
    {
        PixelBuffer r = ImageOps.Resize(Row(0, 200), 4, 1);
        Assert.Equal(new byte[] { 0, 50, 150, 200 }, Enumerable.Range(0, 4).Select(x => r.GetPixel(x, 0).R).ToArray());
        PixelBuffer tall = ImageOps.Resize(Row(0, 200), 4, 3);
        for (int y = 0; y < 3; y++)
        {
            Assert.Equal(50, tall.GetPixel(1, y).R);
        }
    }

    [Fact]
    public void Resize_to_same_size_is_an_exact_copy()
    {
        PixelBuffer src = Unique(5, 4);
        PixelBuffer r = ImageOps.Resize(src, 5, 4);
        Assert.NotSame(src, r);
        Assert.Equal(src.Pixels, r.Pixels);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    [InlineData(32769, 1)]
    [InlineData(1, 32769)]
    [InlineData(20000, 20001)]
    public void Invalid_sizes_throw_with_a_clear_message(int w, int h)
    {
        PixelBuffer src = Unique(2, 2);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Resize(src, w, h));
        Assert.Contains("32768", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.ResizeCanvas(src, w, h, CanvasAnchor.Center, Gray));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.ValidateSize(w, h));
    }

    [Fact]
    public void Largest_supported_sizes_validate()
    {
        ImageOps.ValidateSize(32768, 1);
        ImageOps.ValidateSize(20000, 20000);
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.AddBorder(Unique(2, 2), 16384, Gray));
    }

    [Fact]
    public void ScaleDimensions_rounds_and_keeps_at_least_one_pixel()
    {
        Assert.Equal(new PixelSize(960, 540), ImageOps.ScaleDimensions(1920, 1080, 50));
        Assert.Equal(new PixelSize(2880, 1620), ImageOps.ScaleDimensions(1920, 1080, 150));
        Assert.Equal(new PixelSize(2, 1), ImageOps.ScaleDimensions(3, 1, 50));
        Assert.Equal(new PixelSize(1, 1), ImageOps.ScaleDimensions(3, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.ScaleDimensions(10, 10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.ScaleDimensions(10, 10, double.NaN));
    }

    [Fact]
    public void FitWithAspect_keeps_the_ratio()
    {
        Assert.Equal(new PixelSize(960, 540), ImageOps.FitWithAspect(1920, 1080, 960));
        Assert.Equal(new PixelSize(500, 281), ImageOps.FitWithAspect(1920, 1080, 500, 500));
        Assert.Equal(new PixelSize(25, 50), ImageOps.FitWithAspect(100, 200, 50, 50));
        Assert.Equal(new PixelSize(400, 800), ImageOps.FitWithAspect(100, 200, 1000, 800));
        Assert.Equal(new PixelSize(1, 1), ImageOps.FitWithAspect(1000, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.FitWithAspect(10, 10, 0));
    }

    [Theory]
    [InlineData(40, 30, 10, 7)]
    [InlineData(40, 30, 97, 61)]
    [InlineData(40, 30, 13, 90)]
    public void Resize_footprint_contains_every_pixel_influenced_by_the_region(int w, int h, int nw, int nh)
    {
        var region = new PixelRect(11, 7, 9, 5);
        PixelBuffer a = Solid(w, h, ArgbColor.Black);
        PixelBuffer b = a.Clone();
        b.FillRect(region, ArgbColor.White);
        PixelRect footprint = ImageOps.ResizeFootprint(region, w, h, nw, nh);
        PixelBuffer ra = ImageOps.Resize(a, nw, nh), rb = ImageOps.Resize(b, nw, nh);
        for (int y = 0; y < nh; y++)
        {
            for (int x = 0; x < nw; x++)
            {
                if (!footprint.Contains(x, y))
                {
                    Assert.Equal(ra.GetPixel(x, y), rb.GetPixel(x, y));
                }
            }
        }

        Assert.NotEqual(ra.Pixels, rb.Pixels);
    }

    // ───── Canvas ─────

    [Theory]
    [InlineData(CanvasAnchor.TopLeft, 0, 0)]
    [InlineData(CanvasAnchor.TopCenter, 2, 0)]
    [InlineData(CanvasAnchor.TopRight, 4, 0)]
    [InlineData(CanvasAnchor.MiddleLeft, 0, 2)]
    [InlineData(CanvasAnchor.Center, 2, 2)]
    [InlineData(CanvasAnchor.MiddleRight, 4, 2)]
    [InlineData(CanvasAnchor.BottomLeft, 0, 4)]
    [InlineData(CanvasAnchor.BottomCenter, 2, 4)]
    [InlineData(CanvasAnchor.BottomRight, 4, 4)]
    public void ResizeCanvas_places_the_image_by_anchor(CanvasAnchor anchor, int ox, int oy)
    {
        PixelBuffer src = Unique(4, 2);
        var fill = new ArgbColor(0, 1, 2, 3);
        CanvasResult r = ImageOps.ResizeCanvas(src, 8, 6, anchor, fill);
        Assert.Equal((ox, oy), (r.OffsetX, r.OffsetY));
        Assert.Equal((8, 6), (r.Image.Width, r.Image.Height));
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool inside = x >= ox && x < ox + 4 && y >= oy && y < oy + 2;
                Assert.Equal(inside ? src.GetPixel(x - ox, y - oy) : fill, r.Image.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void ResizeCanvas_smaller_cuts_the_image()
    {
        PixelBuffer src = Unique(5, 5);
        CanvasResult r = ImageOps.ResizeCanvas(src, 3, 3, CanvasAnchor.Center, Gray);
        Assert.Equal((-1, -1), (r.OffsetX, r.OffsetY));
        Assert.Equal(src.GetPixel(1, 1), r.Image.GetPixel(0, 0));
        Assert.Equal(src.GetPixel(3, 3), r.Image.GetPixel(2, 2));
        CanvasResult br = ImageOps.ResizeCanvas(src, 2, 2, CanvasAnchor.BottomRight, Gray);
        Assert.Equal(src.GetPixel(3, 3), br.Image.GetPixel(0, 0));
    }

    [Fact]
    public void AddBorder_grows_the_image_and_frames_it()
    {
        PixelBuffer src = Unique(4, 3);
        var c = ArgbColor.FromRgb(1, 2, 3);
        PixelBuffer r = ImageOps.AddBorder(src, 2, c);
        Assert.Equal((8, 7), (r.Width, r.Height));
        Assert.Equal(c, r.GetPixel(0, 0));
        Assert.Equal(c, r.GetPixel(7, 6));
        Assert.Equal(c, r.GetPixel(1, 3));
        Assert.Equal(src.GetPixel(0, 0), r.GetPixel(2, 2));
        Assert.Equal(src.GetPixel(3, 2), r.GetPixel(5, 4));
        Assert.Equal(src.Pixels, ImageOps.AddBorder(src, 0, c).Pixels);
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.AddBorder(src, -1, c));
    }

    [Fact]
    public void DropShadow_without_blur_is_an_offset_silhouette()
    {
        PixelBuffer src = Solid(10, 10, ArgbColor.White);
        var shadow = new ArgbColor(128, 0, 0, 0);
        CanvasResult r = ImageOps.AddDropShadow(src, 4, 3, 0, shadow, Gray);
        Assert.Equal((14, 13), (r.Image.Width, r.Image.Height));
        Assert.Equal((0, 0), (r.OffsetX, r.OffsetY));
        Assert.Equal(ArgbColor.White, r.Image.GetPixel(9, 9));
        Assert.Equal(ArgbColor.FromRgb(50, 50, 50), r.Image.GetPixel(12, 11)); // 50% black over gray 100
        Assert.Equal(Gray, r.Image.GetPixel(12, 1));
        Assert.Equal(Gray, r.Image.GetPixel(1, 11));

        CanvasResult neg = ImageOps.AddDropShadow(src, -4, -3, 0, shadow, Gray);
        Assert.Equal((4, 3), (neg.OffsetX, neg.OffsetY));
        Assert.Equal(ArgbColor.FromRgb(50, 50, 50), neg.Image.GetPixel(1, 1));
        Assert.Equal(ArgbColor.White, neg.Image.GetPixel(4, 3));
    }

    [Fact]
    public void DropShadow_with_blur_grows_by_three_radii_and_fades()
    {
        PixelBuffer src = PixelBufferTests.Gradient(20, 10);
        CanvasResult r = ImageOps.AddDropShadow(src, 5, 5, 2, ArgbColor.Black, ArgbColor.White);
        // spread 6: left/top 6 - 5 = 1, right/bottom 5 + 6 = 11
        Assert.Equal((32, 22), (r.Image.Width, r.Image.Height));
        Assert.Equal((1, 1), (r.OffsetX, r.OffsetY));
        Assert.Equal(src.Pixels, r.Image.Crop(new PixelRect(1, 1, 20, 10)).Pixels);
        byte deep = r.Image.GetPixel(20, 14).R;
        byte edge = r.Image.GetPixel(30, 20).R;
        Assert.True(deep < edge, $"shadow center {deep} should be darker than its fringe {edge}");
        Assert.Equal(ArgbColor.White, r.Image.GetPixel(31, 0));
    }

    [Fact]
    public void Combine_horizontal_and_vertical_layouts()
    {
        PixelBuffer a = Solid(2, 3, ArgbColor.Black), b = Solid(4, 1, ArgbColor.White);
        PixelBuffer h = ImageOps.Combine([a, b], CombineDirection.Horizontal, 2, Gray, CombineAlignment.Center);
        Assert.Equal((8, 3), (h.Width, h.Height));
        Assert.Equal(ArgbColor.Black, h.GetPixel(1, 2));
        Assert.Equal(Gray, h.GetPixel(2, 1));
        Assert.Equal(Gray, h.GetPixel(4, 0));
        Assert.Equal(ArgbColor.White, h.GetPixel(4, 1));
        Assert.Equal(Gray, h.GetPixel(7, 2));

        PixelBuffer v = ImageOps.Combine([a, b], CombineDirection.Vertical, 1, Gray, CombineAlignment.End);
        Assert.Equal((4, 5), (v.Width, v.Height));
        Assert.Equal(Gray, v.GetPixel(0, 0));
        Assert.Equal(ArgbColor.Black, v.GetPixel(2, 0));
        Assert.Equal(Gray, v.GetPixel(0, 3));
        Assert.Equal(ArgbColor.White, v.GetPixel(0, 4));

        PixelBuffer s = ImageOps.Combine([a, b], CombineDirection.Horizontal, 0, Gray, CombineAlignment.Start);
        Assert.Equal(ArgbColor.White, s.GetPixel(2, 0));
        Assert.Equal(Gray, s.GetPixel(2, 1));
        Assert.Throws<ArgumentException>(() => ImageOps.Combine([], CombineDirection.Horizontal, 0, Gray, CombineAlignment.Start));
    }

    [Fact]
    public void DrawImage_blends_source_over_with_opacity()
    {
        PixelBuffer target = Solid(4, 4, Gray);
        byte[] before = (byte[])target.Pixels.Clone();
        PixelBuffer overlay = Solid(2, 2, ArgbColor.FromRgb(200, 0, 255));
        PixelBuffer r = ImageOps.DrawImage(target, overlay, 1, 1, 0.5);
        Assert.Equal(ArgbColor.FromRgb(150, 50, 178), r.GetPixel(1, 1)); // 255 * .5 + 100 * .5 = 177.5 (float rounding)
        Assert.Equal(Gray, r.GetPixel(0, 0));
        Assert.Equal(Gray, r.GetPixel(3, 3));
        Assert.Equal(before, target.Pixels);

        PixelBuffer translucent = Solid(1, 1, new ArgbColor(51, 200, 200, 200));
        Assert.Equal(ArgbColor.FromRgb(120, 120, 120), ImageOps.DrawImage(target, translucent, 0, 0, 1).GetPixel(0, 0));

        PixelBuffer empty = Solid(2, 2, new ArgbColor(0, 0, 0, 0));
        PixelBuffer onEmpty = ImageOps.DrawImage(empty, overlay, 0, 0, 0.5);
        Assert.Equal(new ArgbColor(128, 200, 0, 255), onEmpty.GetPixel(0, 0));

        PixelBuffer clipped = ImageOps.DrawImage(target, Unique(3, 3), -2, -2, 1);
        Assert.NotEqual(Gray, clipped.GetPixel(0, 0));
        Assert.Equal(Gray, clipped.GetPixel(1, 0));
        Assert.Equal(Gray, clipped.GetPixel(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.DrawImage(target, overlay, 0, 0, 1.5));
    }

    // ───── Region effects ─────

    [Fact]
    public void BoxBlur_changes_only_the_region()
    {
        PixelBuffer src = PixelBufferTests.Gradient(40, 30);
        byte[] before = (byte[])src.Pixels.Clone();
        var region = new PixelRect(10, 5, 20, 15);
        PixelBuffer r = ImageOps.BoxBlur(src, region, 3);
        Assert.Equal(before, src.Pixels);
        int changed = 0;
        for (int y = 0; y < 30; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                if (!region.Contains(x, y))
                {
                    Assert.Equal(src.GetPixel(x, y), r.GetPixel(x, y));
                }
                else if (src.GetPixel(x, y) != r.GetPixel(x, y))
                {
                    changed++;
                }
            }
        }

        Assert.True(changed > region.Area / 2, $"only {changed} pixels changed");
        Assert.Equal(src.Pixels, ImageOps.BoxBlur(src, region, 0).Pixels);
    }

    [Fact]
    public void BoxBlur_region_is_self_contained()
    {
        PixelBuffer src = PixelBufferTests.Gradient(40, 30);
        var region = new PixelRect(10, 5, 20, 15);
        PixelBuffer other = src.Clone();
        other.FillRect(new PixelRect(0, 0, 40, 5), ArgbColor.White);
        other.FillRect(new PixelRect(30, 0, 10, 30), ArgbColor.Black);
        PixelBuffer a = ImageOps.BoxBlur(src, region, 4), b = ImageOps.BoxBlur(other, region, 4);
        Assert.Equal(a.Crop(region).Pixels, b.Crop(region).Pixels);

        // Blurring a solid area leaves it solid, also with partial alpha.
        var c = new ArgbColor(90, 10, 160, 250);
        AssertAll(ImageOps.BoxBlur(Solid(9, 9, c), new PixelRect(-5, -5, 50, 50), 3), c);
    }

    [Fact]
    public void BoxBlur_spreads_a_single_pixel_symmetrically()
    {
        PixelBuffer src = Solid(21, 21, ArgbColor.Black);
        src.SetPixel(10, 10, ArgbColor.White);
        PixelBuffer r = ImageOps.BoxBlur(src, src.Bounds, 1);
        Assert.True(r.GetPixel(10, 10).R < 255);
        Assert.True(r.GetPixel(11, 10).R > 0);
        Assert.Equal(r.GetPixel(9, 10), r.GetPixel(11, 10));
        Assert.Equal(ArgbColor.Black, r.GetPixel(14, 10)); // three passes of radius 1 reach 3 pixels
    }

    [Fact]
    public void Pixelate_fills_blocks_with_their_average()
    {
        var src = new PixelBuffer(6, 3);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                byte v = (byte)((x * 10) + (y * 40));
                src.SetPixel(x, y, ArgbColor.FromRgb(v, v, v));
            }
        }

        // Region x 1..5, y 0..2 with 2-pixel blocks starting at (1, 0).
        PixelBuffer r = ImageOps.Pixelate(src, new PixelRect(1, 0, 5, 3), 2);
        Assert.Equal(src.GetPixel(0, 1), r.GetPixel(0, 1));
        Assert.Equal(35, r.GetPixel(1, 0).R); // 10, 20, 50, 60
        Assert.Equal(35, r.GetPixel(2, 1).R);
        Assert.Equal(55, r.GetPixel(3, 0).R); // 30, 40, 70, 80
        Assert.Equal(70, r.GetPixel(5, 1).R); // last block is 1 wide: 50, 90
        Assert.Equal(95, r.GetPixel(1, 2).R); // bottom blocks are 1 tall: 90, 100
        Assert.Equal(95, r.GetPixel(2, 2).R);
    }

    [Fact]
    public void Pixelate_is_premultiplied()
    {
        var src = new PixelBuffer(2, 1);
        src.SetPixel(0, 0, ArgbColor.FromRgb(255, 0, 0));
        src.SetPixel(1, 0, new ArgbColor(0, 0, 0, 255));
        PixelBuffer r = ImageOps.Pixelate(src, src.Bounds, 2);
        Assert.Equal(new ArgbColor(128, 255, 0, 0), r.GetPixel(1, 0));
    }

    [Fact]
    public void Large_rotate_and_resize_are_fast()
    {
        PixelBuffer big = PixelBufferTests.Gradient(3840, 2160);
        ImageOps.Rotate90Clockwise(Unique(8, 8)); // warm up
        ImageOps.Resize(Unique(8, 8), 3, 3);
        var sw = Stopwatch.StartNew();
        PixelBuffer rotated = ImageOps.Rotate90Clockwise(big);
        PixelBuffer down = ImageOps.Resize(big, 1920, 1080);
        PixelBuffer up = ImageOps.Resize(big, 5000, 3000);
        PixelBuffer blurred = ImageOps.BoxBlur(big, big.Bounds, 8);
        sw.Stop();
        Assert.NotEqual(big.Pixels, blurred.Pixels);
        Assert.Equal(3840, rotated.Height);
        Assert.Equal(1920, down.Width);
        Assert.Equal(3000, up.Height);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }
}
