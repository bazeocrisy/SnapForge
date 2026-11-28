using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Tests;

public class TransformCommandTests
{
    private const int W = 30, H = 20;

    private static readonly ArgbColor MaskColor = ArgbColor.Black;

    private static readonly PixelRect MaskRegion = new(20, 10, 5, 4);

    /// <summary>A document with one annotation of every type plus a redaction (base keeps the original pixels).</summary>
    private static EditorDocument NewDoc(bool redact = true)
    {
        var d = new EditorDocument(PixelBufferTests.Gradient(W, H));
        d.Execute(new AddAnnotationCommand(new ArrowAnnotation(new PointD(2, 3), new PointD(10, 5))));
        d.Execute(new AddAnnotationCommand(new LineAnnotation(new PointD(1, 1), new PointD(4, 9)) { StrokeWidth = 2 }));
        d.Execute(new AddAnnotationCommand(new RectangleAnnotation(new RectD(4, 5, 6, 2))));
        d.Execute(new AddAnnotationCommand(new EllipseAnnotation(new RectD(10, 2, 8, 4))));
        d.Execute(new AddAnnotationCommand(new HighlightAnnotation(new RectD(0, 10, 12, 3))));
        d.Execute(new AddAnnotationCommand(new TextAnnotation(new PointD(1, 2), "Hi", 16) { MeasuredSize = new PointD(8, 4) }));
        d.Execute(new AddAnnotationCommand(new StepMarkerAnnotation(new PointD(5, 6), 1, 12)));
        if (redact)
        {
            d.Execute(new RedactCommand(MaskRegion, MaskColor));
        }

        return d;
    }

    private static (byte[] Base, byte[] Image, Annotation[] Annotations, int W, int H) Snapshot(EditorDocument d) =>
        ((byte[])d.BaseImage.Pixels.Clone(), (byte[])d.Image.Pixels.Clone(), d.Annotations.ToArray(), d.Image.Width, d.Image.Height);

    private static void AssertSame((byte[] Base, byte[] Image, Annotation[] Annotations, int W, int H) expected, EditorDocument d)
    {
        Assert.Equal((expected.W, expected.H), (d.Image.Width, d.Image.Height));
        Assert.Equal((expected.W, expected.H), (d.BaseImage.Width, d.BaseImage.Height));
        Assert.Equal(expected.Base, d.BaseImage.Pixels);
        Assert.Equal(expected.Image, d.Image.Pixels);
        Assert.Equal(expected.Annotations, d.Annotations);
    }

    private static T Get<T>(EditorDocument d)
        where T : Annotation => d.Annotations.OfType<T>().Single();

    private static void AssertNear(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }

    private static void AssertNear(RectD expected, RectD actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
        Assert.Equal(expected.Height, actual.Height, 9);
    }

    private static void AssertMaskBurned(EditorDocument d)
    {
        RedactionMaskAnnotation mask = Get<RedactionMaskAnnotation>(d);
        for (int y = mask.Region.Y; y < mask.Region.Bottom; y++)
        {
            for (int x = mask.Region.X; x < mask.Region.Right; x++)
            {
                Assert.Equal(MaskColor, d.Image.GetPixel(x, y));
            }
        }

        Assert.True(d.HasReversibleRedactions);
    }

    public static TheoryData<ImageTransform> Transforms => new(Enum.GetValues<ImageTransform>());

    // ───── TransformImageCommand ─────

    [Theory]
    [MemberData(nameof(Transforms))]
    public void Transform_changes_pixels_and_undo_restores_exactly(ImageTransform t)
    {
        EditorDocument d = NewDoc();
        var before = Snapshot(d);
        PixelBuffer expectedBase = ImageOps.Transform(d.BaseImage, t), expectedImage = ImageOps.Transform(d.Image, t);
        d.Execute(new TransformImageCommand(t));
        Assert.Equal(expectedBase.Pixels, d.BaseImage.Pixels);
        Assert.Equal(expectedImage.Pixels, d.Image.Pixels);
        AssertMaskBurned(d);
        Assert.Equal(before.Annotations.Select(a => a.Id), d.Annotations.Select(a => a.Id));
        var after = Snapshot(d);

        Assert.True(d.Undo());
        AssertSame(before, d);
        Assert.True(d.Redo());
        AssertSame(after, d);
    }

    [Theory]
    [MemberData(nameof(Transforms))]
    public void Transform_twice_or_four_times_returns_to_the_original(ImageTransform t)
    {
        EditorDocument d = NewDoc();
        var before = Snapshot(d);
        int repeats = t is ImageTransform.Rotate90 or ImageTransform.Rotate270 ? 4 : 2;
        for (int i = 0; i < repeats; i++)
        {
            d.Execute(new TransformImageCommand(t));
        }

        AssertSame(before, d);
    }

    [Fact]
    public void Rotate90_maps_every_annotation_type()
    {
        EditorDocument d = NewDoc();
        d.Execute(new TransformImageCommand(ImageTransform.Rotate90));
        Assert.Equal((H, W), (d.Image.Width, d.Image.Height));

        // Clockwise quarter turn of a 30×20 image: (x, y) -> (20 - y, x).
        ArrowAnnotation arrow = Get<ArrowAnnotation>(d);
        Assert.Equal(new PointD(17, 2), arrow.Start);
        Assert.Equal(new PointD(15, 10), arrow.End);
        LineAnnotation line = Get<LineAnnotation>(d);
        Assert.Equal((new PointD(19, 1), new PointD(11, 4)), (line.Start, line.End));
        Assert.Equal(2, line.StrokeWidth);
        Assert.Equal(new RectD(13, 4, 2, 6), Get<RectangleAnnotation>(d).Rect);
        Assert.Equal(new RectD(14, 10, 4, 8), Get<EllipseAnnotation>(d).Rect);
        Assert.Equal(new RectD(7, 0, 3, 12), Get<HighlightAnnotation>(d).Rect);
        StepMarkerAnnotation step = Get<StepMarkerAnnotation>(d);
        Assert.Equal(new PointD(14, 5), step.Center);
        Assert.Equal(12, step.Radius);

        // Text is not rotated: its box center (5, 4) moves to (16, 5) and the box keeps its size.
        TextAnnotation text = Get<TextAnnotation>(d);
        Assert.Equal(new PointD(12, 3), text.Position);
        Assert.Equal(new PointD(8, 4), text.MeasuredSize);
        Assert.Equal(16, text.FontSize);

        Assert.Equal(new PixelRect(6, 20, 4, 5), Get<RedactionMaskAnnotation>(d).Region);
    }

    [Fact]
    public void Flips_and_180_map_annotation_geometry()
    {
        EditorDocument d = NewDoc();
        d.Execute(new TransformImageCommand(ImageTransform.FlipHorizontal));
        Assert.Equal(new PointD(28, 3), Get<ArrowAnnotation>(d).Start);
        Assert.Equal(new RectD(20, 5, 6, 2), Get<RectangleAnnotation>(d).Rect);
        Assert.Equal(new PixelRect(5, 10, 5, 4), Get<RedactionMaskAnnotation>(d).Region);
        Assert.Equal(new PointD(21, 2), Get<TextAnnotation>(d).Position);
        d.Undo();

        d.Execute(new TransformImageCommand(ImageTransform.FlipVertical));
        Assert.Equal(new PointD(2, 17), Get<ArrowAnnotation>(d).Start);
        Assert.Equal(new RectD(0, 7, 12, 3), Get<HighlightAnnotation>(d).Rect);
        Assert.Equal(new PointD(5, 14), Get<StepMarkerAnnotation>(d).Center);
        Assert.Equal(new PixelRect(20, 6, 5, 4), Get<RedactionMaskAnnotation>(d).Region);
        d.Undo();

        d.Execute(new TransformImageCommand(ImageTransform.Rotate180));
        Assert.Equal(new PointD(20, 15), Get<ArrowAnnotation>(d).End);
        Assert.Equal(new RectD(12, 14, 8, 4), Get<EllipseAnnotation>(d).Rect);
        Assert.Equal(new PixelRect(5, 6, 5, 4), Get<RedactionMaskAnnotation>(d).Region);
        Assert.Equal(new PointD(21, 14), Get<TextAnnotation>(d).Position);
        d.Undo();

        d.Execute(new TransformImageCommand(ImageTransform.Rotate270));
        Assert.Equal((H, W), (d.Image.Width, d.Image.Height));
        // Counter-clockwise: (x, y) -> (y, 30 - x).
        Assert.Equal(new PointD(3, 28), Get<ArrowAnnotation>(d).Start);
        Assert.Equal(new PixelRect(10, 5, 4, 5), Get<RedactionMaskAnnotation>(d).Region);
    }

    [Fact]
    public void Transform_reports_size_change_only_for_quarter_turns()
    {
        EditorDocument d = NewDoc();
        var events = new List<DocumentChangedEventArgs>();
        d.Changed += (_, e) => events.Add(e);
        d.Execute(new TransformImageCommand(ImageTransform.Rotate90));
        d.Undo();
        d.Execute(new TransformImageCommand(ImageTransform.FlipHorizontal));
        Assert.All(events, e => Assert.True(e.ImageChanged));
        Assert.Equal([true, true, false], events.Select(e => e.SizeChanged));
        Assert.Equal("Flip horizontal", d.History.UndoDescription);
    }

    [Fact]
    public void Transform_without_redactions_keeps_base_and_image_identical()
    {
        EditorDocument d = NewDoc(redact: false);
        d.Execute(new TransformImageCommand(ImageTransform.Rotate270));
        Assert.Same(d.BaseImage, d.Image);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransformImageCommand((ImageTransform)42));
    }

    // ───── ResizeImageCommand ─────

    [Fact]
    public void Resize_scales_geometry_strokes_fonts_and_radii()
    {
        EditorDocument d = NewDoc();
        var before = Snapshot(d);
        d.Execute(new ResizeImageCommand(60, 40));
        Assert.Equal((60, 40), (d.Image.Width, d.Image.Height));
        ArrowAnnotation arrow = Get<ArrowAnnotation>(d);
        Assert.Equal((new PointD(4, 6), new PointD(20, 10)), (arrow.Start, arrow.End));
        Assert.Equal(8, arrow.StrokeWidth);
        Assert.Equal(4, Get<LineAnnotation>(d).StrokeWidth);
        Assert.Equal(new RectD(8, 10, 12, 4), Get<RectangleAnnotation>(d).Rect);
        Assert.Equal(new RectD(20, 4, 16, 8), Get<EllipseAnnotation>(d).Rect);
        Assert.Equal(new RectD(0, 20, 24, 6), Get<HighlightAnnotation>(d).Rect);
        StepMarkerAnnotation step = Get<StepMarkerAnnotation>(d);
        Assert.Equal((new PointD(10, 12), 24.0, 8.0), (step.Center, step.Radius, step.StrokeWidth));
        TextAnnotation text = Get<TextAnnotation>(d);
        Assert.Equal((new PointD(2, 4), 32.0, new PointD(16, 8)), (text.Position, text.FontSize, text.MeasuredSize));
        Assert.Equal("Hi", text.Text);

        RedactionMaskAnnotation mask = Get<RedactionMaskAnnotation>(d);
        Assert.True(mask.Region.Intersect(new PixelRect(40, 20, 10, 8)) == new PixelRect(40, 20, 10, 8));
        AssertMaskBurned(d);

        d.Undo();
        AssertSame(before, d);
    }

    [Fact]
    public void Non_uniform_resize_scales_axes_separately_and_sizes_by_geometric_mean()
    {
        EditorDocument d = NewDoc();
        d.Execute(new ResizeImageCommand(60, 10)); // ×2, ×0.5 → uniform factor 1
        AssertNear(new RectD(8, 2.5, 12, 1), Get<RectangleAnnotation>(d).Rect);
        Assert.Equal(4, Get<RectangleAnnotation>(d).StrokeWidth, 9);
        Assert.Equal(16, Get<TextAnnotation>(d).FontSize, 9);
        AssertNear(new PointD(10, 3), Get<StepMarkerAnnotation>(d).Center);
        Assert.Equal(12, Get<StepMarkerAnnotation>(d).Radius, 9);
    }

    [Fact]
    public void Resize_redaction_cover_hides_every_pixel_derived_from_hidden_content()
    {
        // Two documents that differ only underneath the redaction must look identical after resizing.
        foreach ((int nw, int nh) in new[] { (13, 9), (71, 47), (45, 15) })
        {
            EditorDocument a = NewDoc(), b = NewDoc();
            b.BaseImage.FillRect(MaskRegion, ArgbColor.White);
            Assert.Equal(a.Image.Pixels, b.Image.Pixels);
            a.Execute(new ResizeImageCommand(nw, nh));
            b.Execute(new ResizeImageCommand(nw, nh));
            Assert.Equal(a.Image.Pixels, b.Image.Pixels);
            Assert.NotEqual(a.BaseImage.Pixels, b.BaseImage.Pixels);
        }
    }

    [Fact]
    public void Resize_rejects_invalid_sizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeImageCommand(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeImageCommand(40000, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeImageCommand(30000, 30000));
    }

    // ───── ResizeCanvasCommand ─────

    [Fact]
    public void ResizeCanvas_offsets_annotations_by_the_anchor_offset()
    {
        EditorDocument d = NewDoc();
        var before = Snapshot(d);
        var events = new List<DocumentChangedEventArgs>();
        d.Changed += (_, e) => events.Add(e);
        d.Execute(new ResizeCanvasCommand(50, 30, CanvasAnchor.Center, ArgbColor.White));
        Assert.Equal((50, 30), (d.Image.Width, d.Image.Height));
        Assert.Equal(new PointD(12, 8), Get<ArrowAnnotation>(d).Start);
        Assert.Equal(new RectD(14, 10, 6, 2), Get<RectangleAnnotation>(d).Rect);
        Assert.Equal(new PointD(11, 7), Get<TextAnnotation>(d).Position);
        Assert.Equal(new PointD(15, 11), Get<StepMarkerAnnotation>(d).Center);
        Assert.Equal(new PixelRect(30, 15, 5, 4), Get<RedactionMaskAnnotation>(d).Region);
        AssertMaskBurned(d);
        Assert.Equal(ArgbColor.White, d.Image.GetPixel(0, 0));
        Assert.Equal(before.Image.AsSpan(0, 4).ToArray(), d.Image.Pixels.AsSpan(((5 * 50) + 10) * 4, 4).ToArray());
        Assert.True(events.Single().SizeChanged);

        d.Undo();
        AssertSame(before, d);
    }

    [Fact]
    public void Shrinking_the_canvas_clips_or_drops_redaction_masks()
    {
        EditorDocument d = NewDoc();
        d.Execute(new ResizeCanvasCommand(22, 12, CanvasAnchor.TopLeft, ArgbColor.White));
        Assert.Equal(new PixelRect(20, 10, 2, 2), Get<RedactionMaskAnnotation>(d).Region);
        AssertMaskBurned(d);
        d.Undo();

        d.Execute(new ResizeCanvasCommand(15, 15, CanvasAnchor.TopLeft, ArgbColor.White));
        Assert.Empty(d.Annotations.OfType<RedactionMaskAnnotation>());
        d.Undo();
        Assert.Single(d.Annotations.OfType<RedactionMaskAnnotation>());
    }

    // ───── AddBorderCommand / AddDropShadowCommand ─────

    [Fact]
    public void AddBorder_grows_canvas_and_shifts_annotations()
    {
        EditorDocument d = NewDoc();
        var before = Snapshot(d);
        var red = ArgbColor.FromRgb(255, 0, 0);
        d.Execute(new AddBorderCommand(3, red));
        Assert.Equal((36, 26), (d.Image.Width, d.Image.Height));
        Assert.Equal(red, d.Image.GetPixel(0, 0));
        Assert.Equal(red, d.BaseImage.GetPixel(35, 25));
        Assert.Equal(new PointD(5, 6), Get<ArrowAnnotation>(d).Start);
        Assert.Equal(new RectD(13, 5, 8, 4), Get<EllipseAnnotation>(d).Rect);
        Assert.Equal(new PixelRect(23, 13, 5, 4), Get<RedactionMaskAnnotation>(d).Region);
        AssertMaskBurned(d);
        d.Undo();
        AssertSame(before, d);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddBorderCommand(0, red));
    }

    [Fact]
    public void AddDropShadow_grows_canvas_and_shifts_by_left_top_growth()
    {
        EditorDocument d = NewDoc();
        var before = Snapshot(d);
        d.Execute(new AddDropShadowCommand(-4, 6, 1, new ArgbColor(100, 0, 0, 0), ArgbColor.White));
        // Spread 3: left grows 4 + 3 = 7, right max(0, -4 + 3) = 0, top max(0, 3 - 6) = 0, bottom 6 + 3 = 9.
        Assert.Equal((37, 29), (d.Image.Width, d.Image.Height));
        Assert.Equal(new PointD(9, 3), Get<ArrowAnnotation>(d).Start);
        Assert.Equal(new PointD(8, 2), Get<TextAnnotation>(d).Position);
        Assert.Equal(new PixelRect(27, 10, 5, 4), Get<RedactionMaskAnnotation>(d).Region);
        AssertMaskBurned(d);
        Assert.Equal(before.Base, d.BaseImage.Crop(new PixelRect(7, 0, W, H)).Pixels);
        d.Undo();
        AssertSame(before, d);
    }

    // ───── ApplyRegionEffectCommand ─────

    [Theory]
    [InlineData(RegionEffect.Blur, 3)]
    [InlineData(RegionEffect.Pixelate, 4)]
    public void Region_effect_changes_only_the_region_and_undo_restores(RegionEffect effect, int strength)
    {
        EditorDocument d = NewDoc(redact: false);
        var before = Snapshot(d);
        var region = new PixelRect(5, 4, 15, 10);
        var events = new List<DocumentChangedEventArgs>();
        d.Changed += (_, e) => events.Add(e);
        var command = new ApplyRegionEffectCommand(region, effect, strength);
        d.Execute(command);
        Assert.Equal(region, command.AppliedRegion);
        Assert.Same(d.BaseImage, d.Image);
        PixelBuffer original = new(W, H, (byte[])before.Base.Clone());
        int changed = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (!region.Contains(x, y))
                {
                    Assert.Equal(original.GetPixel(x, y), d.Image.GetPixel(x, y));
                }
                else if (original.GetPixel(x, y) != d.Image.GetPixel(x, y))
                {
                    changed++;
                }
            }
        }

        Assert.True(changed > 0);
        Assert.Equal(before.Annotations, d.Annotations);
        Assert.True(events.Single().ImageChanged);
        Assert.False(events.Single().SizeChanged);
        var after = Snapshot(d);

        d.Undo();
        AssertSame(before, d);
        d.Redo();
        AssertSame(after, d);
    }

    [Theory]
    [InlineData(RegionEffect.Blur, 4)]
    [InlineData(RegionEffect.Pixelate, 3)]
    public void Region_effect_keeps_redactions_intact_and_does_not_leak_hidden_pixels(RegionEffect effect, int strength)
    {
        EditorDocument a = NewDoc(), b = NewDoc();
        b.BaseImage.FillRect(MaskRegion, ArgbColor.White); // differs only under the redaction
        var region = new PixelRect(12, 4, 18, 16);
        var beforeA = Snapshot(a);
        byte[] hiddenA = a.BaseImage.Crop(MaskRegion).Pixels;
        a.Execute(new ApplyRegionEffectCommand(region, effect, strength));
        b.Execute(new ApplyRegionEffectCommand(region, effect, strength));

        AssertMaskBurned(a);
        Assert.Equal(a.Image.Pixels, b.Image.Pixels);
        Assert.Equal(hiddenA, a.BaseImage.Crop(MaskRegion).Pixels);

        a.Undo();
        AssertSame(beforeA, a);
    }

    [Fact]
    public void Region_effect_validates_arguments()
    {
        Assert.Throws<ArgumentException>(() => new ApplyRegionEffectCommand(PixelRect.Empty, RegionEffect.Blur, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplyRegionEffectCommand(new PixelRect(0, 0, 5, 5), RegionEffect.Blur, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplyRegionEffectCommand(new PixelRect(0, 0, 5, 5), RegionEffect.Pixelate, 1));
        EditorDocument d = NewDoc();
        Assert.Throws<InvalidOperationException>(() => d.Execute(new ApplyRegionEffectCommand(new PixelRect(100, 100, 5, 5), RegionEffect.Blur, 2)));
        Assert.Equal("Pixelate", new ApplyRegionEffectCommand(new PixelRect(0, 0, 5, 5), RegionEffect.Pixelate, 2).Description);
    }

    [Fact]
    public void Commands_combine_and_unwind_through_history()
    {
        EditorDocument d = NewDoc();
        var before = Snapshot(d);
        d.Execute(new TransformImageCommand(ImageTransform.Rotate90));
        d.Execute(new ApplyRegionEffectCommand(new PixelRect(0, 0, 10, 10), RegionEffect.Blur, 2));
        d.Execute(new ResizeImageCommand(37, 51));
        d.Execute(new ResizeCanvasCommand(50, 60, CanvasAnchor.BottomRight, ArgbColor.White));
        d.Execute(new AddBorderCommand(2, ArgbColor.Black));
        d.Execute(new AddDropShadowCommand(3, 3, 2, ArgbColor.Black, ArgbColor.White));
        d.Execute(new CropCommand(new PixelRect(5, 5, 30, 30)));
        while (d.Undo())
        {
        }

        Assert.Equal(before.Base, d.BaseImage.Pixels);
        Assert.Equal((W, H), (d.Image.Width, d.Image.Height));
        Assert.Empty(d.Annotations);
    }
}
