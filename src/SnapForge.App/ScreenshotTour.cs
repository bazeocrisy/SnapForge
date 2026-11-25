using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapForge.App.Rendering;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.App.Views;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;

namespace SnapForge.App;

/// <summary>
/// <c>SnapForge.exe --screenshots &lt;folder&gt;</c>: builds a throw-away demo library in a temporary folder and saves
/// PNG renderings of the Editor, Library, Capture window and Settings in light and dark themes. Used by CI to produce
/// review screenshots; it never reads or writes the user's real library or settings.
/// </summary>
internal static class ScreenshotTour
{
    private static readonly string[] CardTitles = ["Revenue", "Orders", "Conversion"];
    private static readonly string[] CardValues = ["$48,210", "1,284", "3.9 %"];
    private static readonly string[] RowStates = ["Shipped", "Pending", "Refunded", "Shipped"];

    public static int Run(string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        string temp = Path.Combine(Path.GetTempPath(), "snapforge-tour-" + Guid.NewGuid().ToString("N"));
        var log = new List<string>();
        try
        {
            AppServices services = AppServices.Create(Path.Combine(temp, "settings.json"), Path.Combine(temp, "library"),
                s => s with { CloseToNotificationArea = false, PrivacyNoticeShown = true, Theme = ThemePreference.Dark });
            AppServices.Current = services;
            SeedLibrary(services.Library, services.Tray);

            foreach (ThemePreference theme in new[] { ThemePreference.Dark, ThemePreference.Light })
            {
                ThemeManager.Apply(theme);
                string suffix = theme == ThemePreference.Dark ? "dark" : "light";
                var main = new MainWindow(services) { Width = 1440, Height = 900, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
                Application.Current.MainWindow = main;
                main.Show();
                main.RestoreToolSettings();
                Pump(300);

                CaptureRecord demo = services.Library.Records.OrderByDescending(r => r.CreatedUtc).First(r => r.AnnotationCount > 0);
                main.OpenCaptureAsync(demo.Id).GetAwaiter();
                PumpUntil(() => main.CurrentSession?.CaptureId == demo.Id);
                main.SelectTool.IsChecked = true;
                Pump(600);
                Save(main, Path.Combine(outputFolder, $"editor-{suffix}.png"), log);

                main.ShowLibraryWorkspace();
                Pump(900);
                Save(main, Path.Combine(outputFolder, $"library-{suffix}.png"), log);
                main.ShowEditorWorkspace();

                var capture = new CaptureWindow(services) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
                capture.Show();
                Pump(400);
                Save(capture, Path.Combine(outputFolder, $"capture-window-{suffix}.png"), log);
                capture.Hide();

                var settings = new SettingsWindow(services) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, Height = 1000 };
                settings.Show();
                Pump(400);
                Save(settings, Path.Combine(outputFolder, $"settings-{suffix}.png"), log);
                settings.Close();

                main.FlushAllAsync().GetAwaiter();
                Pump(300);
                main.AllowClose = true;
                main.Close();
                Pump(200);
            }

            File.WriteAllLines(Path.Combine(outputFolder, "screenshots.txt"), log);
            return 0;
        }
#pragma warning disable CA1031 // Report the failure in the output folder and the exit code.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.Add("FAILED: " + ex);
            File.WriteAllLines(Path.Combine(outputFolder, "screenshots.txt"), log);
            return 1;
        }
        finally
        {
            try
            {
                AppServices.Current?.Dispose();
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Save(Window window, string path, List<string> log)
    {
        var root = (FrameworkElement)window.Content;
        int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (DrawingContext dc = background.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
        }

        rtb.Render(background);
        rtb.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
        log.Add($"{Path.GetFileName(path)} {w}x{h}");
    }

    /// <summary>Creates realistic-looking synthetic screenshots (no real screen content) with annotations.</summary>
    private static void SeedLibrary(CaptureLibrary library, RecentTray tray)
    {
        (string Title, string App, string[] Tags, string Folder, bool Fav, int DaysAgo)[] items =
        [
            ("Checkout error dialog", "Orders – Admin", ["bug", "checkout"], "Bug reports", true, 0),
            ("Settings page – new layout", "Preferences", ["ui"], "Design review", false, 0),
            ("Login flow step 2", "Sign in", ["docs", "auth"], "User guide", false, 1),
            ("Dashboard KPI tiles", "Analytics", ["ui"], "Design review", true, 1),
            ("Invoice template", "Billing", ["docs"], "User guide", false, 3),
            ("Build pipeline failure", "CI Runs", ["bug"], "Bug reports", false, 5),
            ("Onboarding checklist", "Welcome", ["docs"], "User guide", false, 9),
            ("Release notes draft", "Editor", [], string.Empty, false, 20),
            ("Map widget glitch", "Locations", ["bug"], "Bug reports", false, 34),
            ("Old mockup v1", "Prototype", ["ui"], "Design review", false, 70),
        ];
        DateTime now = DateTime.UtcNow;
        int n = 0;
        foreach (var item in items)
        {
            PixelBuffer image = FakeScreenshot(item.App, 1280, 760, n);
            var doc = new EditorDocument(image);
            if (n == 0)
            {
                doc.Execute(new AddAnnotationCommand(new RectangleAnnotation(new RectD(388, 250, 504, 220)) { Color = ArgbColor.Parse("#E53935"), StrokeWidth = 5 }));
                doc.Execute(new AddAnnotationCommand(new ArrowAnnotation(new PointD(1010, 140), new PointD(880, 250)) { Color = ArgbColor.Parse("#E53935"), StrokeWidth = 6 }));
                doc.Execute(new AddAnnotationCommand(new StepMarkerAnnotation(new PointD(372, 236), 1, 22) { Color = ArgbColor.Parse("#1E88E5") }));
                doc.Execute(new AddAnnotationCommand(new StepMarkerAnnotation(new PointD(760, 560), 2, 22) { Color = ArgbColor.Parse("#1E88E5") }));
                doc.Execute(new AddAnnotationCommand(new HighlightAnnotation(new RectD(560, 532, 300, 56)) { Color = ArgbColor.Parse("#FDD835") }));
                var text = new TextAnnotation(new PointD(1000, 92), "Error appears after\nclicking Pay", 26) { Color = ArgbColor.Parse("#E53935"), StrokeWidth = 4 };
                doc.Execute(new AddAnnotationCommand(text with { MeasuredSize = AnnotationRenderer.MeasureText(text.Text, text.FontSize) }));
                doc.Execute(new RedactCommand(new PixelRect(420, 400, 260, 34), ArgbColor.Black));
            }
            else if (n % 3 == 1)
            {
                doc.Execute(new AddAnnotationCommand(new ArrowAnnotation(new PointD(300, 120), new PointD(520, 300)) { StrokeWidth = 6 }));
                doc.Execute(new AddAnnotationCommand(new StepMarkerAnnotation(new PointD(560, 320), 1, 20)));
            }

            PixelBuffer flattened = ImageExporter.Flatten(doc.Image, doc.Annotations);
            CaptureRecord r = library.Create(new CaptureContent(doc.BaseImage, doc.Annotations), flattened, now.AddDays(-item.DaysAgo).AddMinutes(-n * 37), item.Title);
            library.UpdateMetadata(r.Id, m => m with { Tags = item.Tags, Folder = item.Folder, Favorite = item.Fav });
            if (n < 7)
            {
                tray.Add(r.Id);
            }

            if (n == 3)
            {
                tray.SetPinned(r.Id, true);
            }

            n++;
        }

        library.MoveToTrash([library.Create(new CaptureContent(FakeScreenshot("Scratch", 800, 500, 99), []), FakeScreenshot("Scratch", 800, 500, 99), now.AddDays(-2), "Accidental capture").Id], now);
    }

    private static PixelBuffer FakeScreenshot(string app, int width, int height, int seed)
    {
        Color[] accents = [Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x05, 0x96, 0x69), Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0xDB, 0x27, 0x77), Color.FromRgb(0xEA, 0x58, 0x0C)];
        Color accent = accents[seed % accents.Length];
        var visual = new DrawingVisual();
        var face = new Typeface("Segoe UI");
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)), null, new Rect(0, 0, 220, height));
            dc.DrawRectangle(new SolidColorBrush(accent), null, new Rect(0, 0, width, 56));
            Text(dc, app, 24, 14, 22, Brushes.White, face);
            string[] nav = ["Overview", "Orders", "Customers", "Reports", "Settings"];
            for (int i = 0; i < nav.Length; i++)
            {
                Text(dc, nav[i], 28, 90 + i * 40, 15, i == 1 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)), face);
            }

            for (int c = 0; c < 3; c++)
            {
                var card = new Rect(250 + c * 330, 90, 310, 120);
                dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), 1), card, 8, 8);
                Text(dc, CardTitles[c], card.X + 16, card.Y + 14, 14, Brushes.Gray, face);
                Text(dc, CardValues[c], card.X + 16, card.Y + 44, 30, Brushes.Black, face);
            }

            var dialog = new Rect(400, 260, 480, 300);
            dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)), 1), dialog, 10, 10);
            Text(dc, seed == 0 ? "Payment failed" : "Details", dialog.X + 24, dialog.Y + 22, 22, Brushes.Black, face);
            Text(dc, "Card ending 4242 was declined.", dialog.X + 24, dialog.Y + 70, 15, Brushes.DimGray, face);
            Text(dc, "Customer: jane.doe@example.com", dialog.X + 24, dialog.Y + 145, 15, Brushes.DimGray, face);
            dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(dialog.Right - 160, dialog.Bottom - 64, 136, 40), 6, 6);
            Text(dc, "Try again", dialog.Right - 128, dialog.Bottom - 55, 15, Brushes.White, face);
            for (int row = 0; row < 4; row++)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), null, new Rect(250, 600 + row * 34, width - 290, 1));
                Text(dc, $"#{10241 + row + seed * 7}   {RowStates[row]}   {(row + 3) * 17}.00", 260, 608 + row * 34, 14, Brushes.DimGray, face);
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        PixelBuffer buffer = BitmapConversion.FromBitmapSource(rtb);
        buffer.ForceOpaque();
        return buffer;
    }

    private static void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, Typeface face) =>
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, 1.0), new Point(x, y));

    private static void PumpUntil(Func<bool> condition)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < end)
        {
            Pump(30);
        }
    }

    private static void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
        while (DateTime.UtcNow < end);
    }
}
