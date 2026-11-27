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

    [Theory]
    [InlineData(900)]  // MainWindow.MinWidth
    [InlineData(1280)] // default width
    public void Every_toolbar_control_stays_fully_visible_at_the_window_width(int width) => UiThread.Run(async () =>
    {
        // Regression (DEF-RC-07): adding Pen/Callout/Stamp pushed Undo/Redo/Delete off a 980-px window.
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        try
        {
            w.Width = width;
            w.LoadImage(TestImages.Gradient(320, 200), DateTime.Now);
            await Task.Delay(300);
            w.UpdateLayout();
            var content = (FrameworkElement)w.Content;
            var controls = new List<FrameworkElement> { w.UndoButton, w.RedoButton, w.DeleteButton, w.DragOutButton, w.CopyButton, w.SaveButton };
            controls.AddRange(w.ToolButtonsPanel.Children.OfType<RadioButton>().Where(b => b.Visibility == Visibility.Visible));
            Assert.True(controls.Count >= 18);
            if (width >= 1280)
            {
                // At the default window size the toolbar must stay one row (a second row costs canvas height).
                double row = w.UndoButton.ActualHeight + 12;
                Assert.True(w.ToolButtonsPanel.ActualHeight <= row,
                    $"toolbar wraps at {width} px: tools {w.ToolButtonsPanel.DesiredSize.Width:0}x{w.ToolButtonsPanel.ActualHeight:0}, " +
                    $"edit {((FrameworkElement)w.UndoButton.Parent).ActualWidth:0}, output {((FrameworkElement)w.SaveButton.Parent).ActualWidth:0}, content {content.ActualWidth:0}");
            }

            foreach (FrameworkElement c in controls)
            {
                Rect r = c.TransformToAncestor(content).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
                Assert.True(c.ActualWidth > 0 && r.Left >= 0 && r.Right <= content.ActualWidth + 0.5,
                    $"{c.Name} at {r} is outside the {content.ActualWidth}-px window content");
            }
        }
        finally
        {
            await Close(w);
        }
    });

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

/// <summary>Scrolling capture against a real WPF scroll viewer with real mouse-wheel input.</summary>
[Collection(DesktopTestGroup.Name)]
public class ScrollingCaptureUiTests
{
    [Fact]
    public void Auto_scroll_capture_stitches_the_whole_page_exactly() => UiThread.Run(async () =>
    {
        const int pageHeight = 2400, viewWidth = 420, viewHeight = 360;
        // Every row is different: vertical gradient plus 13-px stripes of varying shade.
        var page = new System.Windows.Controls.Canvas { Width = viewWidth, Height = pageHeight, SnapsToDevicePixels = true };
        page.Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(20, 60, 200), System.Windows.Media.Color.FromRgb(230, 120, 20), 90);
        for (int y = 0; y < pageHeight; y += 26)
        {
            var stripe = new System.Windows.Shapes.Rectangle { Width = 60 + (y % 300), Height = 13, Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(y % 251), (byte)((y * 7) % 253), 90)) };
            System.Windows.Controls.Canvas.SetTop(stripe, y);
            System.Windows.Controls.Canvas.SetLeft(stripe, 10);
            page.Children.Add(stripe);
        }

        var viewer = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Width = viewWidth, Height = viewHeight };
        var window = new Window
        {
            Content = viewer,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Left = 100,
            Top = 100,
            Topmost = true,
            UseLayoutRounding = true,
        };
        window.Show();
        window.Activate();
        await Task.Delay(500);
        try
        {
            System.Windows.Point origin = viewer.PointToScreen(new System.Windows.Point(0, 0));
            double scale = PresentationSource.FromVisual(viewer)!.CompositionTarget!.TransformToDevice.M11;
            var region = new PixelRect((int)Math.Round(origin.X), (int)Math.Round(origin.Y), (int)Math.Round(viewWidth * scale), (int)Math.Round(viewHeight * scale));
            (PixelBuffer? image, string status) = await ScrollingCaptureWindow.RunAutoForTestAsync(region);
            Assert.True(image is not null, status);
            Assert.True(viewer.VerticalOffset >= viewer.ScrollableHeight - 1, $"did not reach the end: {viewer.VerticalOffset}/{viewer.ScrollableHeight}; {status}");
            int expectedHeight = (int)Math.Round(pageHeight * scale);
            Assert.True(Math.Abs(image!.Height - expectedHeight) <= 2, $"stitched {image.Width}x{image.Height}, expected height {expectedHeight}; {status}");

            // Compare with the page rendered directly (no screen involved).
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(viewWidth * scale), expectedHeight, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
            viewer.ScrollToTop();
            page.Measure(new Size(viewWidth, pageHeight));
            page.Arrange(new Rect(0, 0, viewWidth, pageHeight));
            rtb.Render(page);
            PixelBuffer reference = BitmapConversion.FromBitmapSource(rtb);
            int bad = 0, rows = Math.Min(reference.Height, image.Height);
            for (int y = 0; y < rows; y += 3)
            {
                for (int x = 5; x < image.Width - 5; x += 37)
                {
                    ArgbColor a = image.GetPixel(x, y), b = reference.GetPixel(x, y);
                    if (Math.Abs(a.R - b.R) > 6 || Math.Abs(a.G - b.G) > 6 || Math.Abs(a.B - b.B) > 6)
                    {
                        bad++;
                    }
                }
            }

            Assert.True(bad == 0, $"{bad} sampled pixels differ from the page (misaligned, repeated or missing rows); {status}");
        }
        finally
        {
            window.Close();
        }
    });
}
