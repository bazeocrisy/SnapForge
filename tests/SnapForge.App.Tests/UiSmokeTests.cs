using System.Windows;
using System.Windows.Threading;
using SnapForge.App.Rendering;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Export;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Tests;

/// <summary>
/// Loads the real application resources and main window (catches XAML/resource errors that only appear at
/// runtime), drives the editor through its public surface and exports the result. Not a substitute for
/// manual acceptance testing: no real mouse input or screen selection happens here.
/// </summary>
[Collection(DesktopTestGroup.Name)]
public class UiSmokeTests
{
    [Fact]
    public void Main_window_loads_edits_and_exports() => UiThread.Run(() =>
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            Pump();

            PixelBuffer image = TestImages.Gradient(400, 300);
            window.LoadImage(image, new DateTime(2026, 1, 2, 3, 4, 5));
            Pump();

            EditorDocument doc = window.Document!;
            doc.Execute(new AddAnnotationCommand(new ArrowAnnotation(new PointD(20, 20), new PointD(200, 150))));
            doc.Execute(new AddAnnotationCommand(new StepMarkerAnnotation(new PointD(300, 60), StepNumbering.Next(doc.Annotations), 16)));
            doc.Execute(new RedactCommand(new PixelRect(250, 200, 100, 50), ArgbColor.Black));
            Pump();
            Assert.True(doc.IsDirty);

            PixelBuffer final = window.RenderFinalImage();
            Assert.Equal(400, final.Width);
            Assert.Equal(ArgbColor.Black, final.GetPixel(300, 225));
            Assert.NotEmpty(ImageExporter.Encode(final, ImageFileFormat.Png));

            Assert.True(doc.Undo());
            Assert.NotEqual(ArgbColor.Black, window.RenderFinalImage().GetPixel(300, 225));

            doc.MarkSaved(); // avoid the unsaved-changes prompt on close
        }
        finally
        {
            // Never leave unsaved state: the close prompt would block the test run.
            window.Document?.MarkSaved();
            window.Close();
        }

        return Task.CompletedTask;
    });

    [Fact]
    public void Clipboard_receives_bitmap_and_png() => Sta.Run(() =>
    {
        PixelBuffer image = TestImages.Gradient(32, 16);
        Task copy = ImageExporter.CopyToClipboardAsync(image);
        while (!copy.IsCompleted)
        {
            Pump();
        }

        copy.GetAwaiter().GetResult();
        Assert.True(Clipboard.ContainsImage());
        Assert.True(Clipboard.ContainsData("PNG"));
        var stream = Assert.IsType<System.IO.MemoryStream>(Clipboard.GetData("PNG"));
        PixelBuffer decoded = ExportTests.Decode(stream.ToArray());
        Assert.Equal(image.Pixels, decoded.Pixels);
    });

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
