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
/// <c>SnapForge.exe --screenshots &lt;folder&gt;</c>: builds a throw-away demo library in a temporary folder, shows
/// the real windows on the desktop and saves screen captures of the Editor, tray, Library, capture panel (image and
/// video tabs), QuickDock (collapsed, expanded, keyboard mode, docked left) and Settings in light and dark themes. Used by CI to produce review
/// screenshots; it never reads or writes the user's real library or settings.
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
                s => s with { CloseToNotificationArea = false, PrivacyNoticeShown = true, Theme = ThemePreference.Dark, Widget = s.Widget with { Enabled = false } });
            AppServices.Current = services;
            SeedLibrary(services.Library, services.Tray);
            Guid? videoId = SeedVideo(services, log);

            // Real screenshots: each window is shown on the actual desktop and the screen pixels are captured with
            // SnapForge's own GDI capture (not an off-screen rendering). For crisp website images the display is
            // switched to 1920×1080 when the display offers that mode (temporary, not saved; restored at the end).
            bool hiRes = Video.VideoSelfTest.TrySetDisplayMode(1920, 1080);
            log.Add(hiRes ? "Display switched to 1920x1080 for the tour." : "1920x1080 not available; using the current display mode.");
            Pump(1500);
            Core.Capture.ScreenLayout layout = Interop.PhysicalPlacement.Layout();
            PixelRect work = layout.Monitors[0].WorkArea;
            log.Add($"Desktop: {string.Join("; ", layout.Monitors.Select(m => $"{m.DeviceName} {m.Bounds.Width}x{m.Bounds.Height} at {m.DpiScale * 100:0}%"))}");
            foreach (ThemePreference theme in new[] { ThemePreference.Dark, ThemePreference.Light })
            {
                services.UpdateSettings(x => x with { Theme = theme });
                ThemeManager.Apply(theme);
                string suffix = theme == ThemePreference.Dark ? "dark" : "light";
                double dip = layout.Monitors[0].DpiScale;
                var main = new MainWindow(services)
                {
                    Width = work.Width / dip,
                    Height = work.Height / dip,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = work.X / dip,
                    Top = work.Y / dip,
                };
                Application.Current.MainWindow = main;
                main.Show();
                main.RestoreToolSettings();
                Pump(300);

                CaptureRecord demo = services.Library.Records.OrderByDescending(r => r.CreatedUtc).First(r => r.AnnotationCount > 0);
                main.OpenCaptureAsync(demo.Id).GetAwaiter();
                PumpUntil(() => main.CurrentSession?.CaptureId == demo.Id);
                main.SelectTool.IsChecked = true;
                main.SelectTrayTiles(2);
                Pump(600);
                SaveScreen(main, null, Path.Combine(outputFolder, $"editor-{suffix}.png"), log);
                if (Bounds(main) is { } mb)
                {
                    // The Recent Captures tray with two tiles selected for a multi-capture drag.
                    var trayTop = main.TrayPanel.PointToScreen(new Point(0, 0));
                    var tray = PixelRect.FromLtrb(mb.X, (int)trayTop.Y, mb.Right, mb.Bottom);
                    SaveRegion(tray, Path.Combine(outputFolder, $"tray-{suffix}.png"), log);
                }

                // Welcome / Getting started.
                Dialogs.WelcomeWindow.ShowFor(main, services.Library.Root, "Print Screen", "Shift+F9", () => { });
                Pump(500);
                if (Application.Current.Windows.OfType<Dialogs.WelcomeWindow>().FirstOrDefault() is { } welcome)
                {
                    SaveScreen(welcome, null, Path.Combine(outputFolder, $"welcome-{suffix}.png"), log);
                    welcome.Close();
                }

                // Text recognition (OCR) of the open capture, when Windows has an OCR language installed.
                if (Ocr.OcrService.AvailableLanguages().Count > 0)
                {
                    Ocr.TextRecognitionWindow.Show(main, main.RenderFinalImage(), demo.Title);
                    Pump(3000);
                    if (Application.Current.Windows.OfType<Ocr.TextRecognitionWindow>().FirstOrDefault() is { } ocr)
                    {
                        SaveScreen(ocr, null, Path.Combine(outputFolder, $"ocr-{suffix}.png"), log);
                        ocr.Close();
                    }
                }
                else
                {
                    log.Add("OCR screenshot skipped: no OCR language installed on this machine.");
                }

                // RC3 Record panel as it first opens (Camera and Mic off), then with Camera and Mic on: the floating
                // camera preview shows the SYNTHETIC test camera (CI has no real camera; labelled in the caption).
                var studio = new Video.StudioWindow(services, listCameras: () => []) { ExcludeFromCapture = false };
                studio.Show();
                Pump(700);
                SaveScreen(studio, null, Path.Combine(outputFolder, $"studio-{suffix}.png"), log);
                SaveScreen(studio, null, Path.Combine(outputFolder, $"record-panel-{suffix}.png"), log);
                studio.Close();
                Video.RecordingController.SessionCamera = true;
                Video.RecordingController.SessionMicrophone = true;
                var withCamera = new Video.StudioWindow(services, (_, _) => new Video.SyntheticCameraSource(640, 360, 30), () => [new Video.Interop.Mf.VideoDevice("SnapForge test camera (synthetic)", "synthetic")]) { ExcludeFromCapture = false };
                withCamera.Show();
                Pump(1800);
                if (withCamera.Preview is { } preview)
                {
                    SaveScreen(preview, 8, Path.Combine(outputFolder, $"camera-preview-{suffix}.png"), log);
                    if (Bounds(withCamera) is { } pb && Bounds(preview) is { } cb)
                    {
                        SaveRegion(PixelRect.Union([new PixelRect(pb.X - 12, pb.Y - 12, pb.Width + 24, pb.Height + 24), new PixelRect(cb.X - 12, cb.Y - 12, cb.Width + 24, cb.Height + 24)]), Path.Combine(outputFolder, $"record-panel-camera-{suffix}.png"), log);
                    }
                }

                SaveScreen(withCamera, null, Path.Combine(outputFolder, $"record-panel-on-{suffix}.png"), log);
                withCamera.Close();
                Video.RecordingController.SessionCamera = false;
                Video.RecordingController.SessionMicrophone = false;

                main.ShowLibraryWorkspace();
                main.LibraryPane.SelectIds(services.Library.Records.OrderByDescending(r => r.CreatedUtc).Take(2).Select(r => r.Id));
                Pump(900);
                SaveScreen(main, null, Path.Combine(outputFolder, $"library-{suffix}.png"), log);
                main.LibraryPane.VideosView.IsChecked = true;
                Pump(700);
                SaveScreen(main, null, Path.Combine(outputFolder, $"library-videos-{suffix}.png"), log);
                main.LibraryPane.AllView.IsChecked = true;
                main.ShowEditorWorkspace();

                if (videoId is { } vid && services.Library.Find(vid) is { } videoRecord)
                {
                    var player = new Video.VideoPlayerWindow(videoRecord, services.Library.VideoPath(vid))
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = work.X / dip + 40,
                        Top = work.Y / dip + 30,
                        Width = Math.Min(820, work.Width / dip - 80),
                        Height = Math.Min(560, work.Height / dip - 60),
                    };
                    player.Show();
                    Pump(2500); // let the media pipeline open and play a little
                    SaveScreen(player, null, Path.Combine(outputFolder, $"player-{suffix}.png"), log);
                    player.Close();

                    var editor = new Video.VideoEditorWindow(services, videoRecord, services.Library.VideoPath(vid))
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = work.X / dip + 40,
                        Top = work.Y / dip + 30,
                        Width = Math.Min(1000, work.Width / dip - 80),
                        Height = Math.Min(680, work.Height / dip - 60),
                    };
                    editor.Show();
                    Pump(1500);
                    SaveScreen(editor, null, Path.Combine(outputFolder, $"video-editor-{suffix}.png"), log);
                    editor.Close();
                }

                var capture = new CaptureWindow(services);
                capture.Show();
                Pump(500);
                SaveScreen(capture, null, Path.Combine(outputFolder, $"capture-panel-{suffix}.png"), log);
                capture.VideoMode.IsChecked = true;
                Pump(300);
                SaveScreen(capture, null, Path.Combine(outputFolder, $"capture-panel-video-{suffix}.png"), log);
                capture.ImageMode.IsChecked = true;
                capture.Hide();

                // Recording toolbar and outline. In the app both are excluded from screen capture, so for these
                // documentation screenshots they are shown without the exclusion (the exclusion itself is verified
                // by the video self-test's pixel check).
                if (Bounds(main) is { } area)
                {
                    var recorded = new PixelRect(area.X + 120, area.Y + 120, Math.Min(640, area.Width - 240), Math.Min(360, area.Height - 260));
                    var outline = new Video.RecordingOutline(recorded, excludeFromCapture: false);
                    outline.Show();
                    var toolbar = new Video.RecordingToolbar($"{recorded.Width}×{recorded.Height} · 30 FPS", true, true, true, "Shift+F10", "Shift+F9", excludeFromCapture: false, cameraOverlay: true);
                    toolbar.SetPreview(available: true, visible: true);
                    toolbar.Show();
                    toolbar.PlaceNear(recorded, work);
                    toolbar.Update(TimeSpan.FromSeconds(83), false, 0.2f, 0.45f);
                    toolbar.UpdateLayout();
                    toolbar.InvalidateVisual();
                    Pump(1500); // run 155: the strip was photographed before it was painted (DEF-RC2-07 symptom)
                    log.Add($"recording toolbar: {toolbar.ActualWidth:0}x{toolbar.ActualHeight:0} DIP, loaded {toolbar.IsLoaded}, visible {toolbar.IsVisible}");
                    SaveScreen(toolbar, 12, Path.Combine(outputFolder, $"recording-toolbar-{suffix}.png"), log);
                    if (Bounds(toolbar) is { } tb)
                    {
                        SaveRegion(PixelRect.Union([new PixelRect(recorded.X - 12, recorded.Y - 12, recorded.Width + 24, recorded.Height + 24), tb]), Path.Combine(outputFolder, $"recording-{suffix}.png"), log);
                    }

                    toolbar.Close();
                    outline.Close();

                    // RC3: the compact confirmation after Stop (not tied to a real file here: no Show in folder).
                    var saved = new Video.RecordingSavedWindow("Recording saved", "Recording saved: 0:01:23, 1920×1080.", Guid.NewGuid(), null, ok: true, excludeFromCapture: false);
                    saved.Show();
                    Pump(500);
                    SaveScreen(saved, 12, Path.Combine(outputFolder, $"recording-saved-{suffix}.png"), log);
                    saved.Close();
                }

                // QuickDock: collapsed pill at the top centre (wide context and close-up), expanded, keyboard mode
                // with a visible focus ring, then docked on the left edge.
                services.UpdateSettings(x => x with { Widget = x.Widget with { Enabled = true, Placement = null } });
                var widget = new CaptureWidget(services);
                widget.Show();
                Pump(400);
                SaveScreen(widget, 70, Path.Combine(outputFolder, $"widget-collapsed-{suffix}.png"), log);
                SaveScreen(widget, 16, Path.Combine(outputFolder, $"quickdock-collapsed-{suffix}.png"), log);
                widget.Expand();
                Pump(400);
                SaveScreen(widget, 40, Path.Combine(outputFolder, $"widget-expanded-{suffix}.png"), log);
                widget.Collapse();
                widget.EnterKeyboardMode();
                Pump(400);
                System.Windows.Input.Keyboard.Focus(widget.RecordButton); // show the focus ring on a non-accent button
                Pump(300);
                SaveScreen(widget, 40, Path.Combine(outputFolder, $"quickdock-keyboard-{suffix}.png"), log);
                widget.ExitKeyboardMode(restoreForeground: true);
                Pump(200);
                widget.DockAt(work.X + 2, work.Y + (work.Height / 2));
                widget.Expand();
                Pump(400);
                SaveScreen(widget, 40, Path.Combine(outputFolder, $"widget-left-{suffix}.png"), log);
                services.UpdateSettings(x => x with { Widget = x.Widget with { Placement = null } });
                widget.Close();

                var settings = new SettingsWindow(services)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = work.X / dip,
                    Top = work.Y / dip,
                    Height = Math.Min(720, work.Height / dip),
                };
                settings.Show();
                Pump(400);
                SaveScreen(settings, null, Path.Combine(outputFolder, $"settings-{suffix}.png"), log);
                settings.WidgetEnabledBox.BringIntoView();
                Pump(300);
                SaveScreen(settings, null, Path.Combine(outputFolder, $"settings-widget-dragdrop-{suffix}.png"), log);
                settings.RecordingSection.BringIntoView();
                Pump(300);
                SaveScreen(settings, null, Path.Combine(outputFolder, $"settings-recording-{suffix}.png"), log);
                settings.Close();

                main.FlushAllAsync().GetAwaiter();
                Pump(300);
                main.AllowClose = true;
                main.Close();
                Pump(200);
            }

            if (hiRes)
            {
                Video.VideoSelfTest.RestoreDisplayMode();
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

    internal static PixelRect? Bounds(Window window)
    {
        nint hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return null;
        }

        PixelRect r = Capture.ScreenCaptureService.GetVisibleBounds(hwnd);
        return r.IsEmpty ? null : r;
    }

    /// <summary>Brings the window to the front and saves the screen pixels it covers (plus an optional margin).</summary>
    internal static void SaveScreen(Window window, int? margin, string path, List<string> log)
    {
        // Park the pointer in a corner so no hover state or tooltip from a previous step is captured.
        Interop.NativeMethods.SetCursorPos(0, (int)SystemParameters.PrimaryScreenHeight - 1);
        bool wasTopmost = window.Topmost;
        window.Topmost = true;
        if (window.ShowActivated)
        {
            window.Activate();
        }

        Pump(350);
        if (Bounds(window) is not { } b)
        {
            log.Add($"{Path.GetFileName(path)} skipped: window has no bounds");
            return;
        }

        int m = margin ?? 0;
        SaveRegion(PixelRect.FromLtrb(b.X - m, b.Y - m, b.Right + m, b.Bottom + m), path, log);
        window.Topmost = wasTopmost;
    }

    /// <summary>Captures a screen region with SnapForge's GDI capture and writes PNG + a small JPEG preview.</summary>
    internal static void SaveRegion(PixelRect screenRect, string path, List<string> log)
    {
        Pump(150);
        Capture.CaptureSnapshot snap = Capture.ScreenCaptureService.TakeSnapshot(includeCursor: false);
        PixelRect r = snap.Layout.ClipSelectionToBitmap(screenRect);
        if (r.IsEmpty)
        {
            log.Add($"{Path.GetFileName(path)} skipped: region off screen");
            return;
        }

        PixelBuffer image = snap.Image.Crop(r);
        File.WriteAllBytes(path, ImageExporter.Encode(image, Core.Export.ImageFileFormat.Png));

        // Small JPEG preview (for quick review where the full artifact cannot be downloaded).
        string previewDir = Path.Combine(Path.GetDirectoryName(path)!, "preview");
        Directory.CreateDirectory(previewDir);
        BitmapSource full = BitmapConversion.ToFrozenBitmap(image);
        double scale = Math.Min(1.0, 720.0 / image.Width);
        var small = new TransformedBitmap(full, new ScaleTransform(scale, scale));
        var jpeg = new JpegBitmapEncoder { QualityLevel = 55 };
        jpeg.Frames.Add(BitmapFrame.Create(small));
        using (FileStream fs = File.Create(Path.Combine(previewDir, Path.GetFileNameWithoutExtension(path) + ".jpg")))
        {
            jpeg.Save(fs);
        }

        log.Add($"{Path.GetFileName(path)} {image.Width}x{image.Height} (screen capture at {screenRect.X},{screenRect.Y})");
    }

    /// <summary>Creates realistic-looking synthetic screenshots (no real screen content) with annotations.</summary>
    /// <summary>A real 4-second screen recording of a moving demo window, saved in the Library like any recording.</summary>
    internal static Guid? SeedVideo(AppServices services, List<string> log)
    {
        try
        {
            _ = Video.Interop.Mf.MFStartup(Video.Interop.Mf.MF_VERSION, 0);
            (Guid id, string path) = services.Library.BeginVideo(DateTime.UtcNow);
            var source = new Video.SyntheticFrameSource(1280, 720, 30, 0);
            Video.RecordingResult r;
            using (Video.ScreenRecorder rec = Video.ScreenRecorder.Create(source, [], new Video.RecorderOptions(30, VideoQuality.Standard, false, false, null, 100L << 20, 1), path))
            {
                rec.Start();
                Pump(4000);
                r = rec.Stop();
            }

            CaptureRecord v = services.Library.CompleteVideo(id, new VideoInfo(r.Width, r.Height, r.FramesPerSecond, r.DurationMs, r.HasAudio), r.FirstFrame, DateTime.UtcNow, "Demo recording (test pattern)");
            services.Library.SetTags(v.Id, ["demo"]);
            services.Tray.Add(v.Id);
            log.Add($"Seeded video: {r.Width}x{r.Height}, {r.DurationMs} ms, {r.FramesWritten} frames, encoder {r.Encoder}");
            return v.Id;
        }
#pragma warning disable CA1031 // screenshots continue without the video
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.Add("Video seed failed: " + ex.Message);
            return null;
        }
    }

    internal static void SeedLibrary(CaptureLibrary library, RecentTray tray)
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
                doc.Execute(new AddAnnotationCommand(new CalloutAnnotation(new RectD(1040, 300, 210, 64), new PointD(930, 420), "Error appears after clicking Pay", 18) { Color = ArgbColor.Parse("#E53935"), StrokeWidth = 3 }));
                doc.Execute(new AddAnnotationCommand(new StampAnnotation(new PointD(1180, 560), StampKind.Warning, 56) { Color = ArgbColor.Parse("#F59E0B") }));
                doc.Execute(new AddAnnotationCommand(new FreehandAnnotation([.. Enumerable.Range(0, 40).Select(i => new PointD(640 + (i * 6), 640 + (Math.Sin(i / 3.0) * 8)))]) { Color = ArgbColor.Parse("#43A047"), StrokeWidth = 4 }));
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

    internal static void PumpUntil(Func<bool> condition)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < end)
        {
            Pump(30);
        }
    }

    internal static void Pump(int milliseconds)
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
