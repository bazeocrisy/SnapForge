using System.Text;
using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

public class CaptureFormatTests
{
    [Fact]
    public void Pixel_codec_round_trips_exactly()
    {
        PixelBuffer image = PixelBufferTests.Gradient(37, 23);
        Assert.Equal(image.Pixels, PixelCodec.Decode(PixelCodec.Encode(image)).Pixels);
    }

    [Fact]
    public void Pixel_codec_rejects_garbage_truncation_and_bad_sizes()
    {
        byte[] good = PixelCodec.Encode(PixelBufferTests.Gradient(8, 8));
        Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(Encoding.ASCII.GetBytes("not an image at all")));
        Assert.ThrowsAny<Exception>(() => PixelCodec.Decode(good[..(good.Length - 10)]));
        byte[] bad = (byte[])good.Clone();
        bad[6] = 0; bad[7] = 0; bad[8] = 0; bad[9] = 0; // width 0
        Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(bad));
        byte[] newer = (byte[])good.Clone();
        newer[4] = 99;
        Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(newer));
    }

    internal static List<Annotation> AllKinds() =>
    [
        new ArrowAnnotation(new PointD(1, 2), new PointD(30, 40)) { StrokeWidth = 6, Color = ArgbColor.Parse("#FF112233") },
        new LineAnnotation(new PointD(5, 5), new PointD(9, 1)),
        new RectangleAnnotation(new RectD(1, 1, 10, 5)),
        new EllipseAnnotation(new RectD(2, 2, 8, 4)),
        new HighlightAnnotation(new RectD(0, 0, 20, 6)) { Opacity = 0.35, Color = ArgbColor.Parse("#FDD835") },
        new TextAnnotation(new PointD(3, 4), "Line 1\nLine \"2\" ✓", 24) { MeasuredSize = new PointD(80, 60) },
        new StepMarkerAnnotation(new PointD(50, 50), 7, 18),
        RedactionMaskAnnotation.For(new PixelRect(4, 4, 10, 3), ArgbColor.Black),
    ];

    [Fact]
    public void Annotations_round_trip_with_ids_and_styles()
    {
        List<Annotation> original = AllKinds();
        List<Annotation> back = CaptureFormat.DeserializeAnnotations(CaptureFormat.SerializeAnnotations(original));
        Assert.Equal(original, back);
    }

    [Fact]
    public void Unknown_annotation_type_and_future_version_are_rejected()
    {
        string unknown = """{"format":"snapforge.capture.annotations","version":1,"annotations":[{"type":"hologram","id":"00000000-0000-0000-0000-000000000001","color":"#FF000000","strokeWidth":1}]}""";
        Assert.Throws<InvalidDataException>(() => CaptureFormat.DeserializeAnnotations(Encoding.UTF8.GetBytes(unknown)));
        string future = """{"format":"snapforge.capture.annotations","version":2,"annotations":[]}""";
        Assert.Throws<InvalidDataException>(() => CaptureFormat.DeserializeAnnotations(Encoding.UTF8.GetBytes(future)));
        string nan = """{"format":"snapforge.capture.annotations","version":1,"annotations":[{"type":"line","id":"00000000-0000-0000-0000-000000000001","color":"#FF000000","strokeWidth":1,"start":[1,2],"end":[3]}]}""";
        Assert.Throws<InvalidDataException>(() => CaptureFormat.DeserializeAnnotations(Encoding.UTF8.GetBytes(nan)));
    }

    [Fact]
    public void Redaction_mask_color_is_forced_opaque_on_load()
    {
        string json = """{"format":"snapforge.capture.annotations","version":1,"annotations":[{"type":"redaction","id":"00000000-0000-0000-0000-000000000001","color":"#00000000","strokeWidth":0,"region":[0,0,5,5]}]}""";
        var mask = Assert.IsType<RedactionMaskAnnotation>(Assert.Single(CaptureFormat.DeserializeAnnotations(Encoding.UTF8.GetBytes(json))));
        Assert.Equal(255, mask.Color.A);
    }

    [Fact]
    public void Atomic_write_failure_keeps_previous_file_and_leaves_no_temp()
    {
        using var dir = new TestDirectory();
        string file = dir.Sub("data.bin");
        AtomicFile.WriteAllBytes(file, [1, 2, 3]);
        AtomicFile.WriteDecorator.Value = s => new FailingStream(s, 2, () => new IOException("There is not enough space on the disk."));
        try
        {
            Assert.Throws<IOException>(() => AtomicFile.WriteAllBytes(file, new byte[100]));
        }
        finally
        {
            AtomicFile.WriteDecorator.Value = null;
        }

        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Cleanup_removes_orphaned_temp_files_only()
    {
        using var dir = new TestDirectory();
        File.WriteAllText(dir.Sub(".x.123" + AtomicFile.TempSuffix), "partial");
        File.WriteAllText(dir.Sub("keep.json"), "{}");
        Assert.Equal(1, AtomicFile.CleanupTemporaryFiles(dir.Path));
        Assert.Equal(["keep.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Thumbnail_downscales_by_area_average_and_never_upscales()
    {
        var img = new PixelBuffer(4, 2);
        img.FillRect(new PixelRect(0, 0, 2, 2), ArgbColor.White);
        PixelBuffer t = Thumbnailer.Create(img, 2);
        Assert.Equal((2, 1), (t.Width, t.Height));
        Assert.Equal(ArgbColor.White, t.GetPixel(0, 0));
        Assert.Equal(new ArgbColor(255, 0, 0, 0), t.GetPixel(1, 0));
        Assert.Equal((4, 2), (Thumbnailer.Create(img, 100).Width, Thumbnailer.Create(img, 100).Height));
    }
}
