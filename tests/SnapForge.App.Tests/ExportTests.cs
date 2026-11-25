using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using SnapForge.App.Rendering;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Export;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Tests;

public class ExportTests
{
    [Fact]
    public void Flatten_without_annotations_is_bit_identical() => Sta.Run(() =>
    {
        PixelBuffer image = TestImages.Gradient(64, 48);
        PixelBuffer flat = ImageExporter.Flatten(image, []);
        Assert.Equal(image.Pixels, flat.Pixels);
    });

    [Fact]
    public void Flatten_draws_annotation_and_leaves_distant_pixels_untouched() => Sta.Run(() =>
    {
        PixelBuffer image = TestImages.Gradient(200, 100);
        var arrow = new ArrowAnnotation(new PointD(10, 50), new PointD(90, 50)) { Color = ArgbColor.FromRgb(0, 255, 0), StrokeWidth = 6 };
        PixelBuffer flat = ImageExporter.Flatten(image, [arrow]);

        Assert.Equal(ArgbColor.FromRgb(0, 255, 0), flat.GetPixel(50, 50));
        for (int x = 120; x < 200; x++)
        {
            for (int y = 0; y < 100; y++)
            {
                Assert.Equal(image.GetPixel(x, y), flat.GetPixel(x, y));
            }
        }
    });

    [Fact]
    public void Every_annotation_type_renders() => Sta.Run(() =>
    {
        PixelBuffer image = new(300, 200);
        image.FillRect(image.Bounds, ArgbColor.White);
        Annotation[] all =
        [
            new ArrowAnnotation(new PointD(10, 10), new PointD(100, 60)),
            new LineAnnotation(new PointD(10, 190), new PointD(100, 150)),
            new RectangleAnnotation(new RectD(150, 10, 60, 40)),
            new EllipseAnnotation(new RectD(150, 70, 60, 40)),
            new TextAnnotation(new PointD(20, 100), "Hello", 24) { MeasuredSize = AnnotationRenderer.MeasureText("Hello", 24) },
            new StepMarkerAnnotation(new PointD(250, 150), 12, 18),
        ];
        foreach (Annotation a in all)
        {
            PixelBuffer flat = ImageExporter.Flatten(image, [a]);
            Assert.NotEqual(image.Pixels, flat.Pixels);
            RectD b = a.Bounds;
            int changedOutside = CountChanged(image, flat, p => !b.Inflate(2).Contains(p));
            Assert.Equal(0, changedOutside);
        }
    });

    [Fact]
    public void Redacted_pixels_are_exactly_black_in_the_export() => Sta.Run(() =>
    {
        var doc = new EditorDocument(TestImages.Gradient(120, 80));
        var region = new PixelRect(20, 20, 40, 20);
        doc.Execute(new RedactCommand(region, ArgbColor.Black));
        doc.Execute(new AddAnnotationCommand(new RectangleAnnotation(new RectD(80, 5, 30, 30))));

        PixelBuffer flat = ImageExporter.Flatten(doc.Image, doc.Annotations);
        byte[] png = ImageExporter.Encode(flat, ImageFileFormat.Png);
        PixelBuffer decoded = Decode(png);

        for (int y = region.Y; y < region.Bottom; y++)
        {
            for (int x = region.X; x < region.Right; x++)
            {
                Assert.Equal(ArgbColor.Black, decoded.GetPixel(x, y));
            }
        }
    });

    [Fact]
    public void Png_round_trip_is_lossless() => Sta.Run(() =>
    {
        PixelBuffer image = TestImages.Gradient(73, 41); // odd sizes catch stride bugs
        PixelBuffer decoded = Decode(ImageExporter.Encode(image, ImageFileFormat.Png));
        Assert.Equal(image.Pixels, decoded.Pixels);
    });

    [Fact]
    public void Jpeg_encodes_with_correct_size_and_close_colors() => Sta.Run(() =>
    {
        var image = new PixelBuffer(64, 64);
        image.FillRect(image.Bounds, ArgbColor.FromRgb(200, 60, 30));
        byte[] jpeg = ImageExporter.Encode(image, ImageFileFormat.Jpeg);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        PixelBuffer decoded = Decode(jpeg);
        Assert.Equal(64, decoded.Width);
        ArgbColor c = decoded.GetPixel(32, 32);
        Assert.InRange(c.R, 190, 210);
        Assert.InRange(c.G, 50, 70);
        Assert.InRange(c.B, 20, 40);
    });

    [Fact]
    public void Png_contains_no_text_or_exif_metadata() => Sta.Run(() =>
    {
        string ascii = Encoding.ASCII.GetString(ImageExporter.Encode(TestImages.Gradient(16, 16), ImageFileFormat.Png));
        Assert.DoesNotContain("tEXt", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("iTXt", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("eXIf", ascii, StringComparison.Ordinal);
    });

    [Fact]
    public void Save_writes_file_replaces_existing_and_leaves_no_temp_files() => Sta.Run(() =>
    {
        string dir = Path.Combine(Path.GetTempPath(), "snapforge-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "shot.png");
            File.WriteAllText(path, "old");
            ImageExporter.SaveAsync(TestImages.Gradient(10, 10), path, ImageFileFormat.Png).GetAwaiter().GetResult();
            Assert.Equal(TestImages.Gradient(10, 10).Pixels, Decode(File.ReadAllBytes(path)).Pixels);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    });

    [Fact]
    public void Save_to_missing_folder_fails_without_side_effects() => Sta.Run(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "snapforge-missing-" + Guid.NewGuid().ToString("N"), "a.png");
        Assert.ThrowsAny<IOException>(() => ImageExporter.SaveAsync(TestImages.Gradient(4, 4), path, ImageFileFormat.Png).GetAwaiter().GetResult());
        Assert.False(File.Exists(path));
    });

    private static int CountChanged(PixelBuffer a, PixelBuffer b, Func<PointD, bool> where)
    {
        int count = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (where(new PointD(x + 0.5, y + 0.5)) && a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    internal static PixelBuffer Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return BitmapConversion.FromBitmapSource(decoder.Frames[0]);
    }
}
