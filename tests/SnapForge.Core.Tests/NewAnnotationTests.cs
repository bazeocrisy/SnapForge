using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Storage;
using Xunit;

namespace SnapForge.Core.Tests;

public class NewAnnotationTests
{
    private static readonly PointD[] Stroke = [new(10, 10), new(20, 15), new(40, 30)];

    [Fact]
    public void Freehand_callout_and_stamp_round_trip_through_the_capture_format()
    {
        Annotation[] items =
        [
            new FreehandAnnotation(Stroke) { StrokeWidth = 3 },
            new CalloutAnnotation(new RectD(50, 50, 120, 40), new PointD(20, 120), "Look here\nsecond line", 16),
            new StampAnnotation(new PointD(200, 100), StampKind.Warning, 48),
        ];
        JsonArray json = AnnotationSerializer.ToJson(items);
        List<Annotation> back = AnnotationSerializer.FromJson(JsonNode.Parse(json.ToJsonString())!.AsArray());
        Assert.Equal(Stroke, ((FreehandAnnotation)back[0]).Points);
        var c = (CalloutAnnotation)back[1];
        Assert.Equal(("Look here\nsecond line", 16.0, new PointD(20, 120)), (c.Text, c.FontSize, c.Tail));
        Assert.Equal(StampKind.Warning, ((StampAnnotation)back[2]).Kind);
        Assert.Equal(items.Select(a => a.Id), back.Select(a => a.Id));
    }

    [Fact]
    public void Malformed_new_annotations_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => AnnotationSerializer.FromJson(JsonNode.Parse("""[{"id":"6a1b6f2e-3f6b-4b5e-9b1a-1a2b3c4d5e6f","color":"#FF000000","strokeWidth":2,"type":"freehand","points":[1,2,3]}]""")!.AsArray()));
        Assert.Throws<InvalidDataException>(() => AnnotationSerializer.FromJson(JsonNode.Parse("""[{"id":"6a1b6f2e-3f6b-4b5e-9b1a-1a2b3c4d5e6f","color":"#FF000000","strokeWidth":2,"type":"stamp","center":[1,2],"kind":"skull","size":4}]""")!.AsArray()));
    }

    [Fact]
    public void Freehand_hit_test_follows_the_stroke_and_long_strokes_are_thinned()
    {
        var f = new FreehandAnnotation(Stroke) { StrokeWidth = 4 };
        Assert.True(f.HitTest(new PointD(15, 12.5), 1));
        Assert.False(f.HitTest(new PointD(40, 10), 1));
        var many = new FreehandAnnotation([.. Enumerable.Range(0, 10_000).Select(i => new PointD(i, i))]);
        Assert.Equal(FreehandAnnotation.MaxPoints, many.Points.Count);
        Assert.Equal(new PointD(9999, 9999), many.Points[^1]);
    }

    [Fact]
    public void New_annotations_follow_rotation_and_resize()
    {
        var doc = new EditorDocument(new PixelBuffer(100, 50));
        doc.Execute(new AddAnnotationCommand(new FreehandAnnotation([new(10, 10), new(20, 10)])));
        doc.Execute(new AddAnnotationCommand(new StampAnnotation(new PointD(90, 40), StampKind.Check, 10)));
        doc.Execute(new TransformImageCommand(ImageTransform.Rotate90));
        var f = doc.Annotations.OfType<FreehandAnnotation>().Single();
        var s = doc.Annotations.OfType<StampAnnotation>().Single();
        // 100x50 rotated clockwise: (x, y) → (49 - y + 1, x) in pixel-edge coordinates → (50 - y, x).
        Assert.Equal(new PointD(40, 10), f.Points[0]);
        Assert.Equal(new PointD(10, 90), s.Center);
        doc.Execute(new ResizeImageCommand(100, 200));
        Assert.Equal(20, doc.Annotations.OfType<StampAnnotation>().Single().Size, 3);
    }
}

public class OverlayImageCommandTests
{
    [Fact]
    public void Overlay_blends_with_opacity_and_undo_restores_exactly()
    {
        var img = new PixelBuffer(20, 10);
        img.FillRect(img.Bounds, ArgbColor.White);
        var overlay = new PixelBuffer(4, 4);
        overlay.FillRect(overlay.Bounds, ArgbColor.Black);
        var doc = new EditorDocument(img.Clone());
        doc.Execute(new OverlayImageCommand(overlay, 18, 8, 0.5));
        ArgbColor mid = doc.Image.GetPixel(19, 9);
        Assert.InRange(mid.R, 120, 135);              // 50 % black over white
        Assert.Equal(ArgbColor.White, doc.Image.GetPixel(17, 9)); // outside the overlay
        doc.Undo();
        Assert.True(doc.Image.Pixels.AsSpan().SequenceEqual(img.Pixels));
    }
}
