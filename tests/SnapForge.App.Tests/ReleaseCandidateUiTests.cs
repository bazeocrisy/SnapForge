using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SnapForge.App.Capture;
using SnapForge.App.Rendering;
using SnapForge.App.Services;
using SnapForge.Core.Capture;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;

namespace SnapForge.App.Tests;

/// <summary>1.0 release-candidate features through the real main window and real Windows capture.</summary>
[Collection(DesktopTestGroup.Name)]
public partial class ReleaseCandidateUiTests
{
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    private static MainWindow NewWindow(AppServices services)
    {
        var w = new MainWindow(services) { Width = 1200, Height = 800, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        return w;
    }

    private static async Task Close(MainWindow w)
    {
        await w.FlushAllAsync();
        w.AllowClose = true;
        w.Close();
    }

    private static void ClickMenu(MainWindow w, string top, string header)
    {
        MenuItem root = w.MainMenu.Items.OfType<MenuItem>().Single(m => (string)m.Header == top);
        MenuItem item = root.Items.OfType<MenuItem>().Single(m => (string)m.Header == header);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    [Fact]
    public void Image_menu_rotates_and_flips_the_capture_with_annotations_and_undo() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        try
        {
            EditorSession session = w.LoadImage(TestImages.Gradient(320, 200), DateTime.Now);
            EditorDocument doc = session.Document;
            ArgbColor topLeft = doc.Image.GetPixel(0, 0);

            ClickMenu(w, "_Image", "Rotate _right 90°");
            Assert.Equal((200, 320), (doc.Image.Width, doc.Image.Height));
            Assert.Equal(topLeft, doc.Image.GetPixel(199, 0)); // old top-left is now top-right

            ClickMenu(w, "_Image", "Flip _horizontal");
            Assert.Equal(topLeft, doc.Image.GetPixel(0, 0));

            doc.Undo();
            doc.Undo();
            Assert.Equal((320, 200), (doc.Image.Width, doc.Image.Height));
            Assert.Equal(topLeft, doc.Image.GetPixel(0, 0));
        }
        finally
        {
            await Close(w);
        }
    });

    [Fact]
    public void Blur_and_pixelate_change_only_their_region_and_are_kept_in_exports() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        try
        {
            EditorSession session = w.LoadImage(TestImages.Gradient(320, 200), DateTime.Now);
            EditorDocument doc = session.Document;
            PixelBuffer before = doc.Image.Clone();
            doc.Execute(new ApplyRegionEffectCommand(new PixelRect(10, 10, 100, 60), RegionEffect.Pixelate, 12));
            doc.Execute(new ApplyRegionEffectCommand(new PixelRect(150, 100, 100, 60), RegionEffect.Blur, 8));
            PixelBuffer exported = w.RenderFinalImage();
            Assert.Equal(before.GetPixel(300, 190), exported.GetPixel(300, 190)); // outside both regions
            Assert.Equal(exported.GetPixel(10, 10), exported.GetPixel(21, 21));    // one pixelate block
            Assert.NotEqual(before.GetPixel(10, 10), exported.GetPixel(21, 21));
        }
        finally
        {
            await Close(w);
        }
    });

    [Fact]
    public void New_from_clipboard_opens_the_clipboard_image_as_a_capture() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        try
        {
            PixelBuffer image = TestImages.Gradient(123, 77);
            Clipboard.SetImage(BitmapConversion.ToFrozenBitmap(image));
            ClickMenu(w, "_Capture", "New from _clipboard");
            EditorDocument doc = w.Document ?? throw new InvalidOperationException("No capture opened from the clipboard");
            Assert.Equal((123, 77), (doc.Image.Width, doc.Image.Height));
            Assert.Equal(image.GetPixel(60, 40).R, doc.Image.GetPixel(60, 40).R);
        }
        finally
        {
            await Close(w);
        }
    });

    [Fact]
    public void Repeat_last_capture_captures_the_same_screen_rectangle_without_selection() => UiThread.Run(async () =>
    {
        var region = new PixelRect(40, 30, 211, 117);
        CaptureResult? result = await CaptureController.CaptureAsync(new CaptureOptions { Mode = CaptureMode.Region }, region);
        Assert.NotNull(result);
        Assert.Equal((211, 117), (result!.Image.Width, result.Image.Height));
        Assert.Equal(region, CaptureController.LastSelection);
    });

    [Fact]
    public void Active_window_captures_the_foreground_window_of_another_program_without_clicking() => UiThread.Run(async () =>
    {
        string title = "SnapForge active window test " + Guid.NewGuid().ToString("N")[..8];
        using var proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/k title {title}") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal })!;
        try
        {
            WindowCandidate? target = null;
            var sw = Stopwatch.StartNew();
            while (target is null && sw.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(250);
                target = ScreenCaptureService.EnumerateWindows(Environment.ProcessId).FirstOrDefault(c => c.Title.Contains(title, StringComparison.Ordinal));
            }

            Assert.True(target is not null, "console window did not appear");
            _ = SetForegroundWindow(target!.Handle);
            await Task.Delay(500);
            CaptureResult? result = await CaptureController.CaptureAsync(new CaptureOptions { Mode = CaptureMode.ActiveWindow });
            Assert.NotNull(result);
            PixelRect expected = WindowPicker.CaptureRegionFor(target, ScreenCaptureService.GetScreenLayout());
            Assert.Equal((expected.Width, expected.Height), (result!.Image.Width, result.Image.Height));
        }
        finally
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }
    });
}
