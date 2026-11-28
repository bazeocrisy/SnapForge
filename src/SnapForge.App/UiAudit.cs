using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.App.Views;
using SnapForge.Core.Geometry;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;

namespace SnapForge.App;

/// <summary>
/// <c>SnapForge.exe --ui-audit &lt;folder&gt; [label]</c> (QA, RC5): opens every window, dialog, menu, toolbar state and
/// settings section of SnapForge on the real desktop (demo Library in a temporary folder — never the user's data),
/// and for each one records:
/// <list type="bullet">
/// <item>the effective DPI the window really runs at (per-monitor DPI of the window and of the monitor);</item>
/// <item>text smaller than <see cref="MinFontDip"/> DIP (Windows' standard UI text is 12 DIP = 9 pt);</item>
/// <item>text that is clipped or truncated (measured with the text's own font against its laid-out size);</item>
/// <item>controls that lie partly outside their window (unusable) and windows larger than the monitor's work area;</item>
/// <item>controls without an accessible name and controls that cannot be reached with the Tab key;</item>
/// <item>a real screen capture of each state (what the user actually sees at this scaling).</item>
/// </list>
/// Output: <c>ui-audit.json</c>, <c>findings.txt</c>, PNG screen captures, and one <c>UIAUDIT …</c> line per finding on
/// stdout. Exit code 0 unless <c>--strict</c> is given and a FAIL finding exists.
/// </summary>
internal static class UiAudit
{
    /// <summary>Smallest text size considered comfortably readable (Windows' default UI font size).</summary>
    public const double MinFontDip = 12;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly List<Finding> Findings = [];
    private static readonly List<StateRecord> States = [];
    private static readonly List<string> Log = [];
    private static string _out = ".";

    private sealed record Finding(string Severity, string State, string Element, string Problem);

    private static readonly Dictionary<string, int> TabStops = [];

    private sealed record StateRecord(string State, string Screenshot, double WindowDpiScale, double MonitorDpiScale, string Bounds, int Texts, int Controls, int Fails, int Warns);

    public static int Run(string outputFolder, string label, bool strict)
    {
        _out = outputFolder;
        Directory.CreateDirectory(outputFolder);
        string temp = Path.Combine(Path.GetTempPath(), "snapforge-ui-audit-" + Guid.NewGuid().ToString("N"));
        Core.Capture.ScreenLayout layout = Interop.PhysicalPlacement.Layout();
        Core.Capture.MonitorInfo primary = layout.Monitors.FirstOrDefault(m => m.IsPrimary) ?? layout.Monitors[0];
        try
        {
            AppServices services = AppServices.Create(Path.Combine(temp, "settings.json"), Path.Combine(temp, "library"),
                s => s with { CloseToNotificationArea = false, PrivacyNoticeShown = true, Theme = ThemePreference.Dark, Widget = s.Widget with { Enabled = false } });
            AppServices.Current = services;
            ThemeManager.Apply(ThemePreference.Dark);
            ScreenshotTour.SeedLibrary(services.Library, services.Tray);
            Guid? videoId = ScreenshotTour.SeedVideo(services, Log);
            RunAll(services, videoId, primary);
        }
#pragma warning disable CA1031 // The audit reports whatever went wrong instead of crashing without a report.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Findings.Add(new Finding("FAIL", "audit", "UiAudit", "the audit itself crashed: " + ex));
        }

        int fails = Findings.Count(f => f.Severity == "FAIL"), warns = Findings.Count(f => f.Severity == "WARN");
        var report = new
        {
            label,
            monitors = layout.Monitors.Select(m => new { m.DeviceName, bounds = m.Bounds.ToString(), workArea = m.WorkArea.ToString(), dpiScale = m.DpiScale, effectiveDpi = (int)Math.Round(96 * m.DpiScale) }),
            minFontDip = MinFontDip,
            states = States,
            tabStops = TabStops,
            findings = Findings,
            fails,
            warns,
            log = Log,
        };
        File.WriteAllText(Path.Combine(outputFolder, "ui-audit.json"), JsonSerializer.Serialize(report, Json));
        File.WriteAllLines(Path.Combine(outputFolder, "findings.txt"), Findings.Select(f => $"{f.Severity} [{f.State}] {f.Element}: {f.Problem}"));
        foreach (Finding f in Findings)
        {
            Console.WriteLine($"UIAUDIT {f.Severity} [{f.State}] {f.Element}: {f.Problem}");
        }

        Console.WriteLine($"UIAUDIT SUMMARY label={label} primary={primary.Bounds.Width}x{primary.Bounds.Height} scale={primary.DpiScale * 100:0}% effectiveDpi={(int)Math.Round(96 * primary.DpiScale)} states={States.Count} fail={fails} warn={warns}");
        try
        {
            Directory.Delete(temp, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return strict && fails > 0 ? 1 : 0;
    }

    // ------------------------------------------------------------------ the coverage list

    private static void RunAll(AppServices services, Guid? videoId, Core.Capture.MonitorInfo primary)
    {
        PixelRect work = primary.WorkArea;
        double scale = primary.DpiScale;

        // Editor (SnapForge Editor's window) with a demo capture open.
        var main = new MainWindow(services)
        {
            Width = Math.Min(1280, work.Width / scale),
            Height = Math.Min(800, work.Height / scale),
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = work.X / scale,
            Top = work.Y / scale,
        };
        Application.Current.MainWindow = main;
        main.Show();
        main.RestoreToolSettings();
        ScreenshotTour.Pump(400);
        CaptureRecord demo = services.Library.Records.OrderByDescending(r => r.CreatedUtc).First(r => r.AnnotationCount > 0);
        _ = main.OpenCaptureAsync(demo.Id);
        ScreenshotTour.PumpUntil(() => main.CurrentSession?.CaptureId == demo.Id);
        Audit(main, "editor");

        // Every editor tool: its tool options panel.
        foreach ((string name, ToggleButton tool) in new (string, ToggleButton)[]
        {
            ("select", main.SelectTool), ("arrow", main.ArrowTool), ("line", main.LineTool), ("shape", main.RectangleTool),
            ("ellipse", main.EllipseTool), ("highlight", main.HighlightTool), ("text", main.TextTool), ("step", main.StepTool),
            ("pen", main.FreehandTool), ("callout", main.CalloutTool), ("stamp", main.StampTool), ("redact", main.RedactTool),
            ("blur", main.BlurTool), ("pixelate", main.PixelateTool), ("crop", main.CropTool),
        })
        {
            tool.IsChecked = true;
            ScreenshotTour.Pump(150);
            Audit(main, "editor-tool-" + name, screenshot: name is "select" or "text" or "callout" or "stamp" or "redact" or "crop", keyboard: false);
        }

        main.SelectTool.IsChecked = true;

        // Every menu of the menu bar.
        foreach (MenuItem top in FindAll<Menu>(main).SelectMany(m => m.Items.OfType<MenuItem>()))
        {
            string name = "menu-" + Plain(top.Header);
            top.IsSubmenuOpen = true;
            ScreenshotTour.Pump(250);
            if (top.Template.FindName("PART_Popup", top) is Popup { Child: FrameworkElement popupRoot } popup && popup.IsOpen)
            {
                AuditElement(popupRoot, name, main, screenshotOf: popupRoot);
            }
            else
            {
                Findings.Add(new Finding("WARN", name, Plain(top.Header), "the menu did not open in the audit"));
            }

            top.IsSubmenuOpen = false;
            ScreenshotTour.Pump(100);
        }

        // Library workspace (all items, videos only).
        main.ShowLibraryWorkspace();
        ScreenshotTour.Pump(800);
        Audit(main, "library");
        main.LibraryPane.VideosView.IsChecked = true;
        ScreenshotTour.Pump(400);
        Audit(main, "library-videos", keyboard: false);
        main.LibraryPane.AllView.IsChecked = true;
        main.ShowEditorWorkspace();
        ScreenshotTour.Pump(300);

        // Dialogs reached from the Editor's menus (modal: inspected and closed from inside their own loop).
        AuditModal<Dialogs.ResizeImageDialog>("dialog-resize-image", () => Dialogs.ResizeImageDialog.Ask(main, 1280, 760));
        AuditModal<Dialogs.CanvasSizeDialog>("dialog-canvas-size", () => Dialogs.CanvasSizeDialog.Ask(main, 1280, 760));
        AuditModal<Dialogs.BorderDialog>("dialog-border", () => Dialogs.BorderDialog.Ask(main));
        AuditModal<Dialogs.InputDialog>("dialog-watermark", () => Dialogs.InputDialog.Ask(main, "Add watermark", "Watermark text (placed in the bottom-right corner at 45 % opacity):", "Confidential"));
        AuditModal<Video.GifExportDialog>("dialog-gif-export", () => Video.GifExportDialog.Ask(main, 1280, 6000));

        // Welcome / Getting started.
        Dialogs.WelcomeWindow.ShowFor(main, services.Library.Root, "Print Screen", "Shift+F9", () => { });
        ScreenshotTour.Pump(500);
        if (Application.Current.Windows.OfType<Dialogs.WelcomeWindow>().FirstOrDefault() is { } welcome)
        {
            Audit(welcome, "welcome");
            welcome.Close();
        }

        // Text recognition (OCR) window, if Windows has an OCR language.
        if (Ocr.OcrService.AvailableLanguages().Count > 0)
        {
            Ocr.TextRecognitionWindow.Show(main, main.RenderFinalImage(), demo.Title);
            ScreenshotTour.Pump(3000);
            if (Application.Current.Windows.OfType<Ocr.TextRecognitionWindow>().FirstOrDefault() is { } ocr)
            {
                Audit(ocr, "ocr");
                ocr.Close();
            }
        }
        else
        {
            Findings.Add(new Finding("UNVERIFIED", "ocr", "TextRecognitionWindow", "no OCR language installed on this machine"));
        }

        // Video player and video editor.
        if (videoId is { } vid && services.Library.Find(vid) is { } video)
        {
            var player = new Video.VideoPlayerWindow(video, services.Library.VideoPath(vid));
            player.Show();
            ScreenshotTour.Pump(2000);
            Audit(player, "video-player");
            player.Close();
            var editor = new Video.VideoEditorWindow(services, video, services.Library.VideoPath(vid));
            editor.Show();
            ScreenshotTour.Pump(1500);
            Audit(editor, "video-editor");
            editor.Close();
        }

        main.Hide();

        // Capture panel: image and video tabs.
        var capture = new CaptureWindow(services);
        capture.Show();
        ScreenshotTour.Pump(500);
        Audit(capture, "capture-panel");
        capture.VideoMode.IsChecked = true;
        ScreenshotTour.Pump(300);
        Audit(capture, "capture-panel-video");
        capture.ImageMode.IsChecked = true;
        capture.Close();

        // QuickDock: collapsed, expanded, keyboard mode.
        services.UpdateSettings(x => x with { Widget = x.Widget with { Enabled = true, Placement = null } });
        var widget = new CaptureWidget(services);
        widget.Show();
        ScreenshotTour.Pump(400);
        Audit(widget, "quickdock-collapsed", keyboard: false);
        widget.Expand();
        ScreenshotTour.Pump(400);
        Audit(widget, "quickdock-expanded", keyboard: false);
        widget.Collapse();
        widget.EnterKeyboardMode();
        ScreenshotTour.Pump(300);
        Audit(widget, "quickdock-keyboard");
        widget.ExitKeyboardMode(restoreForeground: false);
        widget.Close();

        // Settings: the whole page, scrolled section by section.
        var settings = new SettingsWindow(services);
        settings.Show();
        ScreenshotTour.Pump(600);
        Audit(settings, "settings");
        if (FindAll<ScrollViewer>(settings).FirstOrDefault() is { } scroller)
        {
            int page = 1;
            while (scroller.VerticalOffset + scroller.ViewportHeight < scroller.ExtentHeight - 1 && page < 12)
            {
                scroller.PageDown();
                ScreenshotTour.Pump(250);
                page++;
                ScreenshotTour.SaveScreen(settings, null, Path.Combine(_out, $"settings-page{page}.png"), Log);
            }
        }

        settings.Close();

        // Record panel (Camera and Mic off, and with More options open), recording controls, saved message, preview.
        var studio = new Video.StudioWindow(services, listCameras: () => []) { ExcludeFromCapture = false };
        studio.Show();
        ScreenshotTour.Pump(600);
        Audit(studio, "record-panel");
        if (FindAll<Expander>(studio).FirstOrDefault() is { } more)
        {
            more.IsExpanded = true;
            ScreenshotTour.Pump(400);
            Audit(studio, "record-panel-more-options");
        }

        studio.Close();
        var bar = new Video.RecordingToolbar("1920×1080 · 30 FPS", true, true, true, "Shift+F10", "Shift+F9", excludeFromCapture: false, cameraOverlay: true);
        bar.SetPreview(available: true, visible: true);
        bar.Show();
        bar.Update(TimeSpan.FromSeconds(83), false, 0.3f, 0.5f);
        ScreenshotTour.Pump(1200);
        Audit(bar, "recording-controls", keyboard: false);
        bar.Close();
        var saved = new Video.RecordingSavedWindow("Recording saved", "Recording saved: 0:01:23, 1920×1080.", Guid.NewGuid(), null, ok: true, excludeFromCapture: false);
        saved.Show();
        ScreenshotTour.Pump(500);
        Audit(saved, "recording-saved", keyboard: false);
        saved.Close();
        var failed = new Video.RecordingSavedWindow("The recording could not be finished", "The recording could not be finished normally because the disk is full. What was recorded is kept and will be recovered the next time SnapForge starts.", null, null, ok: false, excludeFromCapture: false);
        failed.Show();
        ScreenshotTour.Pump(500);
        Audit(failed, "recording-failed", keyboard: false);
        failed.Close();
        var preview = new Video.CameraPreviewWindow(excludeFromCapture: false);
        preview.SetState("In the recording · bottom right (not recording yet)", inRecording: false, placeholder: "Starting the camera…");
        preview.Show();
        ScreenshotTour.Pump(500);
        Audit(preview, "camera-preview", keyboard: false);
        preview.Close();

        Findings.Add(new Finding("UNVERIFIED", "keyboard-shortcuts / about", "MessageBox", "native Windows message boxes (system font and DPI handling), not WPF windows"));
        Findings.Add(new Finding("UNVERIFIED", "region-selection-overlay", "SelectionOverlayWindow", "covered by the real-input UI tests (InteractiveUiTests), not by this audit"));
        main.AllowClose = true;
        main.Close();
    }

    // ------------------------------------------------------------------ inspection

    private static void Audit(Window window, string state, bool screenshot = true, bool keyboard = true)
    {
        window.UpdateLayout();
        AuditElement((FrameworkElement)window.Content ?? window, state, window, screenshotOf: screenshot ? window : null, keyboard: keyboard);
    }

    private static void AuditModal<T>(string state, Action open, Func<T, bool>? match = null)
        where T : Window
    {
        bool done = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        timer.Tick += (_, _) =>
        {
            T? w = Application.Current.Windows.OfType<T>().FirstOrDefault(x => x.IsVisible && x.IsLoaded && (match?.Invoke(x) ?? true) && x is not MainWindow);
            if (w is not null)
            {
                timer.Stop();
                ScreenshotTour.Pump(300);
                Audit(w, state);
                done = true;
                w.Close();
            }
            else if (DateTime.UtcNow > deadline)
            {
                timer.Stop();
            }
        };
        timer.Start();
        open();
        timer.Stop();
        if (!done)
        {
            Findings.Add(new Finding("WARN", state, typeof(T).Name, "the dialog did not appear within 10 s"));
        }
    }

    private static void AuditElement(FrameworkElement root, string state, Window window, FrameworkElement? screenshotOf, bool keyboard = false)
    {
        int before = Findings.Count;
        double ppd = VisualTreeHelper.GetDpi(window).PixelsPerDip;
        PixelRect? bounds = ScreenshotTour.Bounds(window);
        Core.Capture.ScreenLayout layout = Interop.PhysicalPlacement.Layout();
        Core.Capture.MonitorInfo monitor = bounds is { } wb ? Interop.PhysicalPlacement.MonitorFor(layout, wb) : layout.Monitors[0];

        // The window must fit the usable area of its monitor (title bar, edges and buttons reachable).
        if (bounds is { } b && root == window.Content && (b.X < monitor.WorkArea.X - 8 || b.Y < monitor.WorkArea.Y - 1 || b.Right > monitor.WorkArea.Right + 8 || b.Bottom > monitor.WorkArea.Bottom + 8))
        {
            Findings.Add(new Finding("FAIL", state, window.GetType().Name, $"window {b} extends beyond the work area {monitor.WorkArea} at {monitor.DpiScale * 100:0}%"));
        }

        int texts = 0, controls = 0;
        var interactive = new List<Control>();
        Rect rootRect = new(0, 0, root.ActualWidth, root.ActualHeight);
        foreach (FrameworkElement e in Visible(root))
        {
            if (e is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                texts++;
                CheckText(tb, root, rootRect, state, ppd);
            }
            else if (e is Control c && c is ButtonBase or TextBoxBase or ComboBox or Slider or ListBox or Expander)
            {
                controls++;
                CheckControl(c, root, rootRect, state);
                if (c.IsEnabled && c.Focusable && KeyboardNavigation.GetIsTabStop(c) && c is not ListBoxItem)
                {
                    interactive.Add(c);
                }
            }
        }

        if (keyboard && interactive.Count > 0)
        {
            CheckTabReachability(window, interactive, state);
        }

        string shot = string.Empty;
        if (screenshotOf is not null)
        {
            shot = $"{state}.png";
            if (screenshotOf is Window w)
            {
                ScreenshotTour.SaveScreen(w, 8, Path.Combine(_out, shot), Log);
            }
            else if (PresentationSource.FromVisual(screenshotOf) is not null)
            {
                Point tl = screenshotOf.PointToScreen(new Point(0, 0));
                Point br = screenshotOf.PointToScreen(new Point(screenshotOf.ActualWidth, screenshotOf.ActualHeight));
                ScreenshotTour.SaveRegion(PixelRect.FromLtrb((int)tl.X - 8, (int)tl.Y - 8, (int)br.X + 8, (int)br.Y + 8), Path.Combine(_out, shot), Log);
            }
        }

        List<Finding> mine = Findings.Skip(before).ToList();
        States.Add(new StateRecord(state, shot, Math.Round(ppd, 3), monitor.DpiScale, bounds?.ToString() ?? "-", texts, controls,
            mine.Count(f => f.Severity == "FAIL"), mine.Count(f => f.Severity == "WARN")));
    }

    private static void CheckText(TextBlock tb, FrameworkElement root, Rect rootRect, string state, double ppd)
    {
        string label = Short(tb.Text);
        if (tb.FontSize < MinFontDip - 0.01)
        {
            Findings.Add(new Finding("WARN", state, $"text \"{label}\"", $"small text: {tb.FontSize:0.#} DIP ({tb.FontSize * ppd:0.#} px at this scale; minimum {MinFontDip} DIP)"));
        }

        if (tb.ActualWidth < 1 || tb.ActualHeight < 1)
        {
            return;
        }

        var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        var ft = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, tb.FlowDirection, typeface, tb.FontSize, Brushes.Black, ppd);
        if (tb.TextWrapping == TextWrapping.NoWrap)
        {
            if (ft.Width > tb.ActualWidth + 1.5)
            {
                bool trimmed = tb.TextTrimming != TextTrimming.None;
                if (trimmed && HasToolTip(tb, root))
                {
                    return; // shortened on purpose; the full text is in the tooltip
                }

                Findings.Add(new Finding(trimmed ? "WARN" : "FAIL", state, $"text \"{label}\"",
                    trimmed ? $"truncated with an ellipsis ({ft.Width:0} DIP of text in {tb.ActualWidth:0} DIP)" : $"clipped: {ft.Width:0} DIP of text in {tb.ActualWidth:0} DIP"));
            }
        }
        else
        {
            // WPF lays text out with rounded (and, in Display mode, pixel-snapped) widths, so a line that exactly fits can measure a
            // fraction wider here and appear to wrap. Allow 1 DIP of rounding; real clipping is far larger (a whole extra line).
            ft.MaxTextWidth = Math.Max(1, tb.ActualWidth - tb.Padding.Left - tb.Padding.Right + 1);
            if (ft.Height > tb.ActualHeight + 1.5 && (tb.MaxHeight >= double.MaxValue || tb.TextTrimming == TextTrimming.None))
            {
                Findings.Add(new Finding("FAIL", state, $"text \"{label}\"", $"clipped vertically: needs {ft.Height:0} DIP, has {tb.ActualHeight:0} DIP"));
            }
        }

        CheckInside(tb, root, rootRect, state, $"text \"{label}\"");
    }

    private static void CheckControl(Control c, FrameworkElement root, Rect rootRect, string state)
    {
        // Parts of another control's template (a ComboBox's drop-down button, a ScrollBar's arrows) are announced as part of
        // that control, which is checked itself.
        if (c.TemplatedParent is Control)
        {
            return;
        }

        string name = AutomationProperties.GetName(c);
        string label = !string.IsNullOrWhiteSpace(name) ? name : c is ContentControl { Content: string s } ? s.Replace("_", string.Empty, StringComparison.Ordinal) : c is HeaderedContentControl { Header: string h } ? h : string.Empty;
        if (string.IsNullOrWhiteSpace(label) && c is not TextBoxBase && c is not ScrollBar && !HasVisibleText(c) && c.GetType().Name is not ("RepeatButton" or "Thumb"))
        {
            Findings.Add(new Finding("WARN", state, c.GetType().Name, "no accessible name (screen readers announce nothing useful)"));
        }

        CheckInside(c, root, rootRect, state, $"{c.GetType().Name} \"{Short(label)}\"");
    }

    /// <summary>An element partly outside the visible part of its window (and not inside a scrolling area) is unusable.</summary>
    private static void CheckInside(FrameworkElement e, FrameworkElement root, Rect rootRect, string state, string label)
    {
        if (HasAncestor<ScrollViewer>(e, root) || HasAncestor<Popup>(e, root))
        {
            return;
        }

        Rect r;
        try
        {
            r = e.TransformToAncestor(root).TransformBounds(new Rect(e.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (r.Right > rootRect.Right + 1.5 || r.Bottom > rootRect.Bottom + 1.5 || r.X < -1.5 || r.Y < -1.5)
        {
            double hidden = (Math.Max(0, r.Right - rootRect.Right) * r.Height) + (Math.Max(0, r.Bottom - rootRect.Bottom) * r.Width);
            Findings.Add(new Finding(hidden > r.Width * r.Height * 0.25 ? "FAIL" : "WARN", state, label, $"partly outside the window ({r.Right:0}×{r.Bottom:0} DIP vs {rootRect.Width:0}×{rootRect.Height:0})"));
        }
    }

    private static void CheckTabReachability(Window window, List<Control> interactive, string state)
    {
        // Press Tab (MoveFocus Next is what the Tab key does) until focus comes back to an element already visited.
        // Focus can land on a ContentElement such as a Hyperlink, so both kinds are followed.
        var seen = new HashSet<DependencyObject>();
        window.Activate();
        ScreenshotTour.Pump(100);
        Keyboard.Focus(window);
        window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        for (int i = 0; i < 600; i++)
        {
            if (Keyboard.FocusedElement is not DependencyObject d || !seen.Add(d))
            {
                break;
            }

            if (d is UIElement { IsVisible: false } || (d is FrameworkContentElement ce && ce.Parent is UIElement { IsVisible: false }))
            {
                Findings.Add(new Finding("FAIL", state, d.GetType().Name, "the Tab key stops on an element that is not visible (focus disappears)"));
            }

            bool moved = d switch
            {
                UIElement u => u.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                ContentElement c => c.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                _ => false,
            };
            if (!moved)
            {
                break;
            }
        }

        // Windows convention: Tab reaches one option of a group (the selected one); the arrow keys move between the options.
        // An option counts as reachable when the arrow keys get to it from an option the Tab key reached.
        var arrowed = new HashSet<DependencyObject>();
        foreach (RadioButton start in seen.OfType<RadioButton>().ToList())
        {
            foreach (FocusNavigationDirection dir in new[] { FocusNavigationDirection.Down, FocusNavigationDirection.Right, FocusNavigationDirection.Up, FocusNavigationDirection.Left })
            {
                Keyboard.Focus(start);
                for (int i = 0; i < 40; i++)
                {
                    if (Keyboard.FocusedElement is not UIElement u || !u.MoveFocus(new TraversalRequest(dir)) || Keyboard.FocusedElement is not RadioButton r || !arrowed.Add(r))
                    {
                        break;
                    }
                }
            }
        }

        TabStops[state] = seen.Count;
        foreach (Control c in interactive.Where(c => !seen.Contains(c) && !arrowed.Contains(c) && !seen.Any(s => s is Visual v && c.IsAncestorOf(v))))
        {
            string label = AutomationProperties.GetName(c) is { Length: > 0 } n ? n : c is ContentControl { Content: string s } ? s : c.GetType().Name;
            Findings.Add(new Finding("WARN", state, $"{c.GetType().Name} \"{Short(label)}\"", $"not reachable with the Tab or arrow keys (Tab visited {seen.Count} element(s), last: {Describe(seen.LastOrDefault())})"));
        }
    }

    private static bool HasToolTip(FrameworkElement e, FrameworkElement root)
    {
        for (DependencyObject? d = e; d is not null && d != root; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { ToolTip: not null })
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(DependencyObject? d) => d switch
    {
        null => "none",
        FrameworkElement f when AutomationProperties.GetName(f) is { Length: > 0 } n => $"{f.GetType().Name} \"{Short(n)}\"",
        ContentControl { Content: string s } c => $"{c.GetType().Name} \"{Short(s)}\"",
        _ => d.GetType().Name,
    };

    // ------------------------------------------------------------------ helpers

    private static IEnumerable<FrameworkElement> Visible(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            DependencyObject d = stack.Pop();
            if (d is UIElement { IsVisible: false })
            {
                continue;
            }

            if (d is FrameworkElement fe)
            {
                yield return fe;
            }

            int n = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(d) : 0;
            for (int i = n - 1; i >= 0; i--)
            {
                stack.Push(VisualTreeHelper.GetChild(d, i));
            }
        }
    }

    private static List<T> FindAll<T>(DependencyObject root)
        where T : DependencyObject => Visible(root).OfType<T>().ToList();

    private static bool HasAncestor<T>(DependencyObject e, DependencyObject stop)
    {
        for (DependencyObject? p = VisualTreeHelper.GetParent(e); p is not null && p != stop; p = VisualTreeHelper.GetParent(p))
        {
            if (p is T)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasVisibleText(DependencyObject d) => Visible(d).OfType<TextBlock>().Any(t => !string.IsNullOrWhiteSpace(t.Text));

    private static string Plain(object? header) => (header?.ToString() ?? "?").Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static string Short(string s)
    {
        string one = s.Replace('\n', ' ').Replace('\r', ' ');
        return one.Length <= 60 ? one : one[..57] + "…";
    }
}
