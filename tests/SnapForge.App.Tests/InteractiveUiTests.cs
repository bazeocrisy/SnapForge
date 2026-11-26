using System.Windows;
using System.Windows.Controls;
using SnapForge.App.Capture;
using SnapForge.Core.Annotations;
using SnapForge.Core.Capture;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Tests;

/// <summary>
/// End-to-end UI tests driven by real mouse input (SendInput) on the test machine's desktop.
/// They cover the interactive paths the model tests cannot: overlay windows, mouse capture,
/// hit testing on the editor canvas and toolbar buttons. Keyboard input is not used (it needs the
/// foreground window, which Windows does not guarantee to a background test process).
/// </summary>
[Collection(DesktopTestGroup.Name)]
public class InteractiveUiTests
{
    private const int Step = 40; // ms between input events, so the dispatcher processes each one

    [Fact]
    public void Region_drag_completes_with_exact_rectangle() => UiThread.Run(async () =>
    {
        // End-to-end regression test for the MVP defect where a region drag could never complete.
        CaptureSnapshot snapshot = SyntheticSnapshot([]);
        PixelRect primary = snapshot.Layout.Monitors[0].Bounds;
        Task<PixelRect?> selection = OverlaySession.RunAsync(snapshot, CaptureMode.Region);
        await Task.Delay(400);

        (int x1, int y1) = (primary.X + 100, primary.Y + 120);
        (int x2, int y2) = (primary.X + 420, primary.Y + 330);
        await Drag(x1, y1, x2, y2);

        PixelRect? result = await WithTimeout(selection);
        Assert.Equal(PixelRect.FromLtrb(x1, y1, x2 + 1, y2 + 1), result);
    });

    [Fact]
    public void Region_drag_upwards_and_left_is_normalized() => UiThread.Run(async () =>
    {
        CaptureSnapshot snapshot = SyntheticSnapshot([]);
        PixelRect primary = snapshot.Layout.Monitors[0].Bounds;
        Task<PixelRect?> selection = OverlaySession.RunAsync(snapshot, CaptureMode.Region);
        await Task.Delay(400);

        await Drag(primary.X + 500, primary.Y + 400, primary.X + 200, primary.Y + 150);

        Assert.Equal(PixelRect.FromLtrb(primary.X + 200, primary.Y + 150, primary.X + 501, primary.Y + 401), await WithTimeout(selection));
    });

    [Fact]
    public void Region_click_without_drag_keeps_overlay_open_and_right_click_cancels() => UiThread.Run(async () =>
    {
        CaptureSnapshot snapshot = SyntheticSnapshot([]);
        PixelRect primary = snapshot.Layout.Monitors[0].Bounds;
        Task<PixelRect?> selection = OverlaySession.RunAsync(snapshot, CaptureMode.Region);
        await Task.Delay(400);

        await Click(primary.X + 300, primary.Y + 300);
        await Task.Delay(300);
        Assert.False(selection.IsCompleted, "A plain click must not finish a region selection.");

        NativeInput.RightClick();
        Assert.Null(await WithTimeout(selection));
    });

    [Fact]
    public void Window_mode_click_returns_the_window_under_the_cursor() => UiThread.Run(async () =>
    {
        CaptureSnapshot probe = SyntheticSnapshot([]);
        PixelRect primary = probe.Layout.Monitors[0].Bounds;
        var front = new PixelRect(primary.X + 150, primary.Y + 100, 320, 240);
        var back = new PixelRect(primary.X + 50, primary.Y + 50, 600, 500);
        CaptureSnapshot snapshot = SyntheticSnapshot([new(1, "Front", front, 0), new(2, "Back", back, 1)]);
        Task<PixelRect?> selection = OverlaySession.RunAsync(snapshot, CaptureMode.Window);
        await Task.Delay(400);

        await Click(front.X + 20, front.Y + 20);
        Assert.Equal(front, await WithTimeout(selection));
    });

    [Fact]
    public void Display_mode_click_returns_the_monitor_bounds() => UiThread.Run(async () =>
    {
        CaptureSnapshot snapshot = SyntheticSnapshot([]);
        PixelRect primary = snapshot.Layout.Monitors[0].Bounds;
        Task<PixelRect?> selection = OverlaySession.RunAsync(snapshot, CaptureMode.SingleScreen);
        await Task.Delay(400);

        await Click(primary.X + 40, primary.Y + 40);
        Assert.Equal(primary, await WithTimeout(selection));
    });

    [Fact]
    public void Annotations_can_be_drawn_selected_moved_undone_and_deleted_with_the_mouse() => UiThread.Run(async () =>
    {
        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = 980,
            Height = 700,
            Topmost = true,
        };
        try
        {
            window.Show();
            var image = new PixelBuffer(400, 300);
            image.FillRect(image.Bounds, ArgbColor.White);
            window.LoadImage(image, DateTime.Now);
            await Task.Delay(600);
            EditorDocument doc = window.Document!;
            Assert.Equal(1.0, window.Editor.Zoom, 3); // small image is shown at actual pixels on a 100 % display

            // Draw a rectangle.
            await ClickElement(window.RectangleTool);
            await DragImage(window, 50, 50, 150, 120);
            var rect = Assert.IsType<RectangleAnnotation>(Assert.Single(doc.Annotations));
            AssertNear(new RectD(50, 50, 100, 70), rect.Rect);

            // Select it by its left edge and move it by (30, 20).
            await ClickElement(window.SelectTool);
            await DragImage(window, 50, 85, 80, 105);
            var moved = Assert.IsType<RectangleAnnotation>(Assert.Single(doc.Annotations));
            Assert.Equal(rect.Id, moved.Id);
            AssertNear(new RectD(80, 70, 100, 70), moved.Rect);

            // Undo and redo through the toolbar.
            await ClickElement(window.UndoButton);
            AssertNear(new RectD(50, 50, 100, 70), ((RectangleAnnotation)doc.Annotations[0]).Rect);
            await ClickElement(window.RedoButton);
            AssertNear(new RectD(80, 70, 100, 70), ((RectangleAnnotation)doc.Annotations[0]).Rect);

            // Arrow and two numbered steps.
            await ClickElement(window.ArrowTool);
            await DragImage(window, 220, 200, 330, 260);
            Assert.IsType<ArrowAnnotation>(doc.Annotations[^1]);
            await ClickElement(window.StepTool);
            await ClickImage(window, 300, 60);
            await ClickImage(window, 340, 60);
            Assert.Equal([1, 2], doc.Annotations.OfType<StepMarkerAnnotation>().Select(s => s.Number));

            // Redact over the arrow's start: pixels and the arrow underneath must be covered.
            await ClickElement(window.RedactTool);
            await DragImage(window, 200, 180, 260, 240);
            Assert.IsType<RedactionMaskAnnotation>(doc.Annotations[^1]);
            PixelBuffer final = window.RenderFinalImage();
            for (int y = 182; y < 238; y += 5)
            {
                for (int x = 202; x < 258; x += 5)
                {
                    Assert.Equal(ArgbColor.Black, final.GetPixel(x, y));
                }
            }

            // Select the rectangle again and delete it with the Delete button.
            await ClickElement(window.SelectTool);
            await ClickImage(window, 80, 100);
            Assert.True(window.DeleteButton.IsEnabled);
            await ClickElement(window.DeleteButton);
            Assert.DoesNotContain(doc.Annotations, a => a is RectangleAnnotation);
        }
        finally
        {
            await window.FlushAllAsync();
            window.AllowClose = true;
            window.Close();
        }
    });

    private static CaptureSnapshot SyntheticSnapshot(IReadOnlyList<WindowCandidate> windows)
    {
        ScreenLayout layout = ScreenCaptureService.GetScreenLayout();
        var image = new PixelBuffer(layout.VirtualBounds.Width, layout.VirtualBounds.Height);
        image.FillRect(image.Bounds, ArgbColor.FromRgb(90, 110, 130));
        return new CaptureSnapshot(layout, image, windows, DateTime.Now);
    }

    private static async Task Drag(int x1, int y1, int x2, int y2)
    {
        NativeInput.MoveTo(x1, y1);
        await Task.Delay(Step);
        Assert.Equal((x1, y1), NativeInput.CursorPosition);
        NativeInput.LeftDown();
        await Task.Delay(Step);
        for (int i = 1; i <= 6; i++)
        {
            NativeInput.MoveTo(x1 + (x2 - x1) * i / 6, y1 + (y2 - y1) * i / 6);
            await Task.Delay(Step / 2);
        }

        await Task.Delay(Step);
        NativeInput.LeftUp();
        await Task.Delay(Step * 3);
    }

    private static async Task Click(int x, int y)
    {
        NativeInput.MoveTo(x, y);
        await Task.Delay(Step);
        NativeInput.LeftDown();
        await Task.Delay(Step);
        NativeInput.LeftUp();
        await Task.Delay(Step * 3);
    }

    private static (int X, int Y) ImageToScreen(MainWindow window, double x, double y)
    {
        Point p = window.Editor.PointToScreen(new Point(x, y));
        return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    private static Task DragImage(MainWindow window, double x1, double y1, double x2, double y2)
    {
        (int ax, int ay) = ImageToScreen(window, x1, y1);
        (int bx, int by) = ImageToScreen(window, x2, y2);
        return Drag(ax, ay, bx, by);
    }

    private static Task ClickImage(MainWindow window, double x, double y)
    {
        (int sx, int sy) = ImageToScreen(window, x, y);
        return Click(sx, sy);
    }

    private static Task ClickElement(FrameworkElement element)
    {
        Point p = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return Click((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    private static void AssertNear(RectD expected, RectD actual, double tolerance = 1.01)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Width, expected.Width - tolerance, expected.Width + tolerance);
        Assert.InRange(actual.Height, expected.Height - tolerance, expected.Height + tolerance);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task)
    {
        Task winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (winner != task)
        {
            throw new TimeoutException("The overlay did not complete; input was not processed as expected.");
        }

        return await task;
    }
}
