using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.App.Capture;
using SnapForge.App.Interop;
using SnapForge.App.Rendering;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.App.Views;
using SnapForge.Core.Capture;
using SnapForge.Core.Editing;
using SnapForge.Core.Export;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Tests;

/// <summary>
/// Milestone 3 (Gate B): capture panel, desktop widget, drag and drop out of SnapForge (real OLE drags with real
/// mouse input), import, high contrast, launch at startup and background library indexing. Runs on the CI Windows
/// desktop; results are automated evidence, not a substitute for testing on a real PC.
/// </summary>
[Collection(DesktopTestGroup.Name)]
public class GateBUiTests
{
    private const int Step = 40;
    private static readonly string[] ThreeTitles = ["First", "Second", "Third"];
    private static readonly string[] Abc = ["A", "B", "C"];

    // ------------------------------------------------------------------ capture panel

    [Fact]
    public void Capture_panel_is_compact_horizontal_and_marks_unavailable_modes() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var panel = new CaptureWindow(services);
        panel.Show();
        await Task.Delay(300);
        try
        {
            var content = (FrameworkElement)panel.Content;
            Assert.InRange(content.ActualWidth, 600, 760);
            Assert.InRange(content.ActualHeight, 150, 260);
            Assert.True(content.ActualWidth > content.ActualHeight * 2, "panel should be horizontal");

            Assert.True(panel.ImageMode.IsChecked);
            Assert.True(panel.ImageMode.IsEnabled);
            foreach (FrameworkElement unavailable in new FrameworkElement[] { panel.VideoMode, panel.AllInOneMode, panel.EffectsButton, panel.ShareButton })
            {
                Assert.False(unavailable.IsEnabled);
                Assert.Contains("Not available yet", AutomationProperties.GetName(unavailable), StringComparison.Ordinal);
                Assert.Contains("Not available yet", unavailable.ToolTip as string, StringComparison.Ordinal);
            }

            Assert.True(panel.CaptureButton.IsDefault);
            Assert.Equal("Capture", AutomationProperties.GetName(panel.CaptureButton));
            Assert.Equal("Print Screen", panel.HotkeyText.Text); // default hotkey, shown under the button
            Assert.True(panel.SaveBox.IsChecked); // Save to Library preserved
        }
        finally
        {
            panel.Close();
        }
    });

    [Fact]
    public void Capture_panel_selection_delay_reset_and_presets_work() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var panel = new CaptureWindow(services);
        panel.Show();
        await Task.Delay(200);
        try
        {
            // Selection dropdown: every item maps to a working capture mode.
            panel.SelectionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            ContextMenu menu = panel.SelectionButton.ContextMenu!;
            Assert.Equal(["Region", "Window", "Display", "Full screen"], menu.Items.OfType<MenuItem>().Select(i => (string)i.Header));
            menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Window").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            await Task.Delay(100);
            Assert.Equal(CaptureMode.Window, services.Settings.Capture.Mode);
            Assert.Equal("Window", panel.SelectionText.Text);

            // Time delay: picker value, switch off, switch on again restores the chosen value.
            panel.SetDelay(15);
            await Task.Delay(100);
            Assert.True(panel.DelayBox.IsChecked);
            Assert.Equal("Time Delay (15 s)", panel.DelayBox.Content);
            panel.DelayBox.IsChecked = false;
            panel.DelayBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(100);
            Assert.Equal(0, services.Settings.Capture.DelaySeconds);
            panel.DelayBox.IsChecked = true;
            panel.DelayBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(100);
            Assert.Equal(15, services.Settings.Capture.DelaySeconds);

            // Reset with undo.
            CaptureOptions before = services.Settings.Capture;
            panel.ResetOptions();
            await Task.Delay(100);
            Assert.Equal(new CaptureOptions(), services.Settings.Capture);
            Assert.Equal(Visibility.Visible, panel.ResetNotice.Visibility);
            panel.UndoReset();
            await Task.Delay(100);
            Assert.Equal(before, services.Settings.Capture);
            Assert.Equal(Visibility.Collapsed, panel.ResetNotice.Visibility);

            // Presets flyout lists the renamed built-in preset (no misleading "Menu capture").
            panel.PresetsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            ContextMenu presets = panel.PresetsButton.ContextMenu!;
            List<string> names = [.. presets.Items.OfType<MenuItem>().Select(i => i.Header as string ?? string.Empty)];
            Assert.Contains(CapturePreset.DelayedRegionPresetName, names);
            Assert.DoesNotContain(names, n => n.Contains("Menu capture", StringComparison.OrdinalIgnoreCase));
            presets.IsOpen = false;
        }
        finally
        {
            panel.Close();
        }
    });

    [Fact]
    public void Capture_panel_remembers_its_position_and_explains_print_screen_conflicts() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var panel = new CaptureWindow(services);
        panel.Show();
        await Task.Delay(200);
        PixelRect work = PhysicalPlacement.Layout().Monitors[0].WorkArea;
        PixelRect b = PhysicalPlacement.GetBounds(panel)!.Value;
        PhysicalPlacement.SetBounds(panel, new PixelRect(work.X + 37, work.Y + 41, b.Width, b.Height), resize: false);
        await Task.Delay(100);
        panel.Close(); // no notification icon in tests: really closes, and remembers the position
        Assert.Equal(work.X + 37, services.Settings.CapturePanelPosition!.X);

        var again = new CaptureWindow(services);
        again.Show();
        await Task.Delay(200);
        PixelRect restored = PhysicalPlacement.GetBounds(again)!.Value;
        again.Close();
        Assert.Equal((work.X + 37, work.Y + 41), (restored.X, restored.Y));

        string help = CaptureWindow.HotkeyConflictHelp(new HotkeyGesture(HotkeyModifiers.None, "PrintScreen"), "already registered");
        Assert.Contains("Use the Print screen key to open screen capture", help, StringComparison.Ordinal);
        Assert.Contains("Settings → Accessibility → Keyboard", help, StringComparison.Ordinal);
    });

    // ------------------------------------------------------------------ widget

    private static long ExStyle(Window w) => (long)NativeMethods.GetWindowLongPtr(new WindowInteropHelper(w).Handle, NativeMethods.GWL_EXSTYLE);

    [Fact]
    public void Widget_is_enabled_topmost_non_activating_and_docked_top_centre_by_default() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        Assert.True(services.Settings.Widget.Enabled);
        Assert.True(services.Settings.Widget.Topmost);
        var widget = new CaptureWidget(services);
        widget.Show();
        await Task.Delay(300);
        try
        {
            Assert.True(widget.Topmost);
            Assert.False(widget.ShowInTaskbar);
            Assert.NotEqual(0, ExStyle(widget) & NativeMethods.WS_EX_NOACTIVATE);
            Assert.NotEqual(0, ExStyle(widget) & NativeMethods.WS_EX_TOOLWINDOW);
            Assert.False(widget.IsExpanded);

            MonitorInfo primary = PhysicalPlacement.Layout().Monitors[0];
            PixelRect r = PhysicalPlacement.GetBounds(widget)!.Value;
            Assert.Equal(primary.Bounds.Y, r.Y);
            Assert.InRange(r.X + (r.Width / 2), primary.Bounds.X + (primary.Bounds.Width / 2) - 2, primary.Bounds.X + (primary.Bounds.Width / 2) + 2);
            Assert.InRange(r.Height, 1, (int)Math.Ceiling(CaptureWidget.TabAcross * primary.DpiScale) + 1);

            services.UpdateSettings(s => s with { Widget = s.Widget with { Topmost = false } });
            widget.ApplySettings();
            Assert.False(widget.Topmost);
        }
        finally
        {
            widget.Close();
        }
    });

    [Fact]
    public void Widget_expands_on_hover_without_taking_focus_and_collapses_when_left() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var widget = new CaptureWidget(services);
        widget.Show();
        await Task.Delay(300);
        try
        {
            nint foreground = NativeMethods.GetForegroundWindow();
            PixelRect r = PhysicalPlacement.GetBounds(widget)!.Value;
            NativeInput.MoveTo(r.X + (r.Width / 2), r.Y + (r.Height / 2));
            await WaitFor(() => widget.IsExpanded, 5);
            Assert.False(widget.IsActive); // never activated by hovering
            Assert.Equal(foreground, NativeMethods.GetForegroundWindow());
            Assert.NotEqual(new WindowInteropHelper(widget).Handle, NativeMethods.GetForegroundWindow());
            PixelRect expanded = PhysicalPlacement.GetBounds(widget)!.Value;
            Assert.True(expanded.Width > r.Width && expanded.Height > r.Height);
            Assert.Equal(r.Y, expanded.Y); // still docked at the top

            NativeInput.MoveTo(r.X + (r.Width / 2), r.Y + 400);
            await WaitFor(() => !widget.IsExpanded, 5);

            // Click mode: hovering does not open it, a real click does — still without activation.
            services.UpdateSettings(s => s with { Widget = s.Widget with { ExpandMode = WidgetExpandMode.Click } });
            widget.ApplySettings();
            r = PhysicalPlacement.GetBounds(widget)!.Value;
            NativeInput.MoveTo(r.X + (r.Width / 2), r.Y + (r.Height / 2));
            await Task.Delay(800);
            Assert.False(widget.IsExpanded);
            NativeInput.LeftDown();
            await Task.Delay(Step);
            NativeInput.LeftUp();
            await WaitFor(() => widget.IsExpanded, 5);
            Assert.False(widget.IsActive); // clicking a non-activating window does not activate it
            Assert.NotEqual(new WindowInteropHelper(widget).Handle, NativeMethods.GetForegroundWindow());
            NativeInput.MoveTo(r.X + (r.Width / 2), r.Y + 400);
            await WaitFor(() => !widget.IsExpanded, 5);
        }
        finally
        {
            widget.Close();
        }
    });

    [Fact]
    public void Widget_docks_to_left_and_right_edges_and_remembers_the_place() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var widget = new CaptureWidget(services);
        widget.Show();
        await Task.Delay(200);
        MonitorInfo primary = PhysicalPlacement.Layout().Monitors[0];
        widget.DockAt(primary.Bounds.X + 3, primary.Bounds.Y + (primary.Bounds.Height / 3));
        await Task.Delay(200);
        PixelRect left = PhysicalPlacement.GetBounds(widget)!.Value;
        Assert.Equal(DockEdge.Left, services.Settings.Widget.Placement!.Edge);
        Assert.Equal(primary.Bounds.X, left.X);
        Assert.True(left.Height > left.Width, "a side tab is vertical");
        widget.Close();

        // A new widget (restart) restores the same edge and offset.
        var again = new CaptureWidget(services);
        again.Show();
        await Task.Delay(200);
        PixelRect restored = PhysicalPlacement.GetBounds(again)!.Value;
        Assert.Equal(left, restored);

        again.DockAt(primary.Bounds.Right - 2, primary.Bounds.Y + (primary.Bounds.Height / 2));
        await Task.Delay(200);
        PixelRect right = PhysicalPlacement.GetBounds(again)!.Value;
        Assert.Equal(DockEdge.Right, services.Settings.Widget.Placement!.Edge);
        Assert.Equal(primary.Bounds.Right, right.Right);
        again.Close();
    });

    [Fact]
    public void Widget_is_hidden_during_capture_and_never_in_the_captured_pixels() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create(s => s with { Theme = ThemePreference.Dark });
        ThemeManager.Apply(ThemePreference.Dark);
        var widget = new CaptureWidget(services);
        widget.Show();
        widget.Expand();
        await Task.Delay(400);
        try
        {
            PixelRect r = PhysicalPlacement.GetBounds(widget)!.Value;
            static int AccentPixels(CaptureSnapshot snap, PixelRect screen)
            {
                PixelRect area = snap.Layout.ClipSelectionToBitmap(screen);
                int n = 0;
                for (int y = area.Y; y < area.Bottom; y++)
                {
                    for (int x = area.X; x < area.Right; x++)
                    {
                        ArgbColor c = snap.Image.GetPixel(x, y);
                        if (c.R == 0xFF && c.G == 0x7A && c.B == 0x30)
                        {
                            n++;
                        }
                    }
                }

                return n;
            }

            // Control: while visible, the widget's accent Capture button is on screen.
            int visible = AccentPixels(ScreenCaptureService.TakeSnapshot(), r);
            Assert.True(visible > 50, $"control failed: {visible} accent pixels visible");

            CaptureResult? result = await CaptureController.CaptureAsync(new CaptureOptions { Mode = CaptureMode.AllScreens });
            Assert.NotNull(result);
            var layout = PhysicalPlacement.Layout();
            var fake = new CaptureSnapshot(layout, result!.Image, [], DateTime.Now);
            Assert.Equal(0, AccentPixels(fake, r));
            await WaitFor(() => widget.IsVisible, 5); // shown again afterwards
        }
        finally
        {
            widget.Close();
        }
    });

    // ------------------------------------------------------------------ drag and drop

    private static string NewTempStore()
    {
        string root = Path.Combine(Path.GetTempPath(), "snapforge-drag-tests", Guid.NewGuid().ToString("N"));
        CaptureDragDrop.Store = new TempExportStore(root);
        return root;
    }

    private static PixelBuffer Decode(string path)
    {
        BitmapDecoder d = BitmapDecoder.Create(new MemoryStream(File.ReadAllBytes(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(d.Frames[0], PixelFormats.Bgra32, null, 0);
        byte[] px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        return new PixelBuffer(bgra.PixelWidth, bgra.PixelHeight, px);
    }

    /// <summary>A library capture whose top-left 40×20 is redacted (originals kept in the editable copy).</summary>
    private static async Task<(MainWindow Window, CaptureRecord Record, PixelBuffer Original)> RedactedCaptureAsync(AppServices services, string title)
    {
        var w = new MainWindow(services) { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        PixelBuffer original = TestImages.Gradient(160, 100);
        EditorSession s = w.LoadImage(original, new DateTime(2026, 11, 25, 13, 49, 5));
        s.Document.Execute(new RedactCommand(new PixelRect(0, 0, 40, 20), ArgbColor.Black));
        s.Title = title;
        await w.FlushAllAsync();
        await WaitFor(() => s.CaptureId is not null && services.Library.Find(s.CaptureId.Value) is { HasReversibleRedactions: true });
        services.Library.Rename(s.CaptureId!.Value, title);
        return (w, services.Library.Find(s.CaptureId!.Value)!, original);
    }

    [Fact]
    public void Drag_data_offers_only_flattened_redacted_images_named_by_title_and_time() => UiThread.Run(async () =>
    {
        string store = NewTempStore();
        AppServices services = TestServices.Create();
        (MainWindow w, CaptureRecord rec, PixelBuffer original) = await RedactedCaptureAsync(services, "Checkout: error?");
        try
        {
            await w.CloseAllSessionsForTestAsync();
            var item = w.DragItemFor(rec.Id)!;
            var data = new CaptureDragDrop.LazyCaptureFiles([item], new DragDropSettings(), CaptureDragDrop.Store);
            Assert.Equal([DataFormats.FileDrop, CaptureDragDrop.KeysFormat, "Preferred DropEffect"], data.GetFormats());
            Assert.False(data.FilesCreated); // nothing written until a target asks
            Assert.False(Directory.Exists(store) && Directory.EnumerateFileSystemEntries(store).Any());

            string[] files = (string[])data.GetData(DataFormats.FileDrop)!;
            string file = Assert.Single(files);
            Assert.StartsWith(Path.Combine(store, string.Empty), file, StringComparison.OrdinalIgnoreCase);
            string expectedStamp = rec.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal($"Checkout. error {expectedStamp}.png", Path.GetFileName(file));

            // Pixel-level: same as File → Export, redacted area exactly black, rest identical to the original.
            PixelBuffer dropped = Decode(file);
            CaptureContent content = services.Library.Load(rec.Id);
            var doc = new EditorDocument(content.BaseImage, content.Annotations);
            Assert.Equal(ImageExporter.Flatten(doc.Image, doc.Annotations).Pixels, dropped.Pixels);
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 40; x++)
                {
                    Assert.Equal(ArgbColor.Black, dropped.GetPixel(x, y));
                }
            }

            Assert.Equal(original.GetPixel(100, 60), dropped.GetPixel(100, 60));
            string ascii = Encoding.ASCII.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain("tEXt", ascii, StringComparison.Ordinal);
            Assert.DoesNotContain("eXIf", ascii, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!)); // no .sfcap, no base image
            Assert.Equal([rec.Id], CaptureDragDrop.KeysOf(new DataObject(data)));

            // JPEG option.
            var jpeg = new CaptureDragDrop.LazyCaptureFiles([item], new DragDropSettings { Format = ImageFileFormat.Jpeg, JpegQuality = 80 }, CaptureDragDrop.Store);
            string jpg = Assert.Single((string[])jpeg.GetData(DataFormats.FileDrop)!);
            Assert.EndsWith(".jpg", jpg, StringComparison.Ordinal);
            Assert.Equal((160, 100), (Decode(jpg).Width, Decode(jpg).Height));

            // Library unchanged by any of this (copy semantics).
            Assert.True(services.Library.Find(rec.Id)!.HasReversibleRedactions);
            Assert.Single(services.Library.Records);
        }
        finally
        {
            w.AllowClose = true;
            w.Close();
        }
    });

    /// <summary>A tiny window that accepts file drops and records what it received through OLE.</summary>
    private sealed class DropTarget : Window
    {
        public DropTarget()
        {
            Width = 220;
            Height = 160;
            Title = "Drop target";
            AllowDrop = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.LightSteelBlue;
            DragEnter += (_, _) => Log("target: drag enter");
            DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += (_, e) =>
            {
                Log("target: drop");
                Files = e.Data.GetData(DataFormats.FileDrop) as string[];
                Log($"target: received {Files?.Length ?? 0} file(s)");
                e.Handled = true;
            };
        }

        public string[]? Files { get; private set; }
    }

    /// <summary>Events seen during the current drag test (included in failure messages).</summary>
    private static readonly List<string> Trace = [];

    private static void Log(string message)
    {
        lock (Trace)
        {
            Trace.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }

    private static string TraceText()
    {
        lock (Trace)
        {
            return string.Join(" | ", Trace);
        }
    }

    private static async Task SlowDrag(int x1, int y1, int x2, int y2, bool cancelWithRightClick = false)
    {
        lock (Trace)
        {
            Trace.Clear();
        }

        CaptureDragDrop.BeforeDragHook = d => Log($"drag started with {d.Items.Count} item(s)");

        // Watchdog on another thread: if the OLE drag loop is still running long after the input sequence, end it
        // with real Esc + button-up input so a broken drag fails this test instead of hanging every later test.
        using var watchdog = new Timer(_ =>
        {
            if (CaptureDragDrop.IsDragging)
            {
                Log("watchdog: drag still running, sending Esc and button up");
                NativeInput.KeyPress(0x1B);
                NativeInput.LeftUp();
            }
        }, null, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(3));
        try
        {
            await SlowDragCore(x1, y1, x2, y2, cancelWithRightClick);
        }
        finally
        {
            CaptureDragDrop.BeforeDragHook = null;
        }
    }

    private static async Task SlowDragCore(int x1, int y1, int x2, int y2, bool cancelWithRightClick)
    {
        Log($"down at {x1},{y1}");
        NativeInput.MoveTo(x1, y1);
        await Task.Delay(Step * 2);
        NativeInput.LeftDown();
        await Task.Delay(Step * 2);
        for (int i = 1; i <= 16; i++)
        {
            NativeInput.MoveTo(x1 + ((x2 - x1) * i / 16), y1 + ((y2 - y1) * i / 16));
            await Task.Delay(Step);
        }

        await Task.Delay(Step * 4);
        if (cancelWithRightClick)
        {
            NativeInput.RightClick(); // a right click during a left-button drag cancels it (OLE)
            await Task.Delay(Step * 2);
        }

        Log($"up at {x2},{y2}; dragging={CaptureDragDrop.IsDragging}");
        NativeInput.LeftUp();
        await Task.Delay(Step * 6);
        Log($"after up; dragging={CaptureDragDrop.IsDragging}");
    }

    private static (int X, int Y) Centre(FrameworkElement e)
    {
        Point p = e.PointToScreen(new Point(e.ActualWidth / 2, e.ActualHeight / 2));
        return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    [Fact]
    public void Real_mouse_drag_from_tray_delivers_png_files_through_OLE() => UiThread.Run(async () =>
    {
        NewTempStore();
        AppServices services = TestServices.Create();
        (MainWindow w, CaptureRecord rec, _) = await RedactedCaptureAsync(services, "Tray drag");
        var target = new DropTarget();
        try
        {
            await WaitFor(() => w.TrayKeys.Contains(rec.Id));
            await Task.Delay(300);
            FrameworkElement tile = w.TrayTileElement(rec.Id)!;
            (int tx, int ty) = Centre(tile);
            target.Left = (tx + 260) / PhysicalPlacement.Layout().Monitors[0].DpiScale;
            target.Top = (ty - 300) / PhysicalPlacement.Layout().Monitors[0].DpiScale;
            target.Show();
            await Task.Delay(300);
            (int dx, int dy) = Centre(target);

            await SlowDrag(tx, ty, dx, dy);
            await WaitFor(() => target.Files is not null, 10, TraceText);
            string file = Assert.Single(target.Files!);
            Assert.True(File.Exists(file));
            Assert.EndsWith(".png", file, StringComparison.Ordinal);
            Assert.Equal(ArgbColor.Black, Decode(file).GetPixel(5, 5)); // redaction burned in
            Assert.Contains("Copied \"Tray drag\"", w.StatusText.Text, StringComparison.Ordinal);
        }
        finally
        {
            target.Close();
            w.AllowClose = true;
            w.Close();
        }
    });

    [Fact]
    public void Real_mouse_drag_of_a_library_multi_selection_delivers_one_file_per_capture() => UiThread.Run(async () =>
    {
        NewTempStore();
        AppServices services = TestServices.Create();
        PixelBuffer img = TestImages.Gradient(80, 60);
        foreach (string t in ThreeTitles)
        {
            services.Library.Create(new CaptureContent(img, []), img, DateTime.UtcNow, t);
        }

        var w = new MainWindow(services) { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        var target = new DropTarget();
        try
        {
            w.ShowLibraryWorkspace();
            await WaitFor(() => w.LibraryPane.VisibleCount == 3);
            await Task.Delay(400);
            w.LibraryPane.SelectIds(services.Library.Records.Select(r => r.Id));
            await Task.Delay(200);
            FrameworkElement tileElement = w.LibraryPane.TileElement(services.Library.Records[0].Id)!;
            (int tx, int ty) = Centre(tileElement);
            target.Left = (tx + 250) / PhysicalPlacement.Layout().Monitors[0].DpiScale;
            target.Top = (ty + 120) / PhysicalPlacement.Layout().Monitors[0].DpiScale;
            target.Show();
            await Task.Delay(300);
            (int dx, int dy) = Centre(target);

            await SlowDrag(tx, ty, dx, dy);
            await WaitFor(() => target.Files is not null, 10, TraceText);
            Assert.Equal(3, target.Files!.Length);
            Assert.Equal(3, target.Files.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(target.Files, f => Assert.Equal((80, 60), (Decode(f).Width, Decode(f).Height)));
            Assert.Equal(3, services.Library.Records.Count); // nothing moved or deleted
        }
        finally
        {
            target.Close();
            w.AllowClose = true;
            w.Close();
        }
    });

    [Fact]
    public void Reordering_inside_the_tray_creates_no_files_and_cancelled_drags_leave_nothing() => UiThread.Run(async () =>
    {
        string store = NewTempStore();
        AppServices services = TestServices.Create();
        PixelBuffer img = TestImages.Gradient(80, 60);
        var ids = new List<Guid>();
        foreach (string t in Abc)
        {
            CaptureRecord r = services.Library.Create(new CaptureContent(img, []), img, DateTime.UtcNow, t);
            services.Tray.Add(r.Id);
            ids.Add(r.Id);
        }

        var w = new MainWindow(services) { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        try
        {
            await WaitFor(() => w.TrayKeys.Count == 3);
            await Task.Delay(400);
            Assert.Equal([ids[2], ids[1], ids[0]], w.TrayKeys); // newest first: C, B, A

            // Drag A (rightmost) to the far left of the tray.
            (int ax, int ay) = Centre(w.TrayTileElement(ids[0])!);
            (int cx, _) = Centre(w.TrayTileElement(ids[2])!);
            await SlowDrag(ax, ay, cx - 60, ay);
            await WaitFor(() => w.TrayKeys[0] == ids[0], 5);
            Assert.Equal([ids[0], ids[2], ids[1]], services.Tray.Items.Select(i => i.Id));
            Assert.False(CaptureDragDrop.LastDrag!.FilesCreated);
            Assert.False(Directory.Exists(store) && Directory.EnumerateFileSystemEntries(store).Any());

            // A drag cancelled with a right click outside any target produces no files either.
            (int bx, int by) = Centre(w.TrayTileElement(ids[1])!);
            await SlowDrag(bx, by, bx, by - 300, cancelWithRightClick: true);
            Assert.False(CaptureDragDrop.LastDrag!.FilesCreated);
            Assert.Equal([ids[0], ids[2], ids[1]], services.Tray.Items.Select(i => i.Id));
        }
        finally
        {
            w.AllowClose = true;
            w.Close();
        }
    });

    [Fact]
    public void Tray_ctrl_click_selects_several_without_opening() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        PixelBuffer img = TestImages.Gradient(80, 60);
        foreach (string t in Abc.Take(2))
        {
            services.Tray.Add(services.Library.Create(new CaptureContent(img, []), img, DateTime.UtcNow, t).Id);
        }

        var w = new MainWindow(services) { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        try
        {
            await WaitFor(() => w.TrayKeys.Count == 2);
            Assert.Equal(2, w.SelectTrayTiles(2).Count);
            Assert.Null(w.CurrentSession); // selecting does not open anything
            Assert.Contains("Drag to another app", w.TrayTooltip(w.TrayKeys[0]), StringComparison.Ordinal);
        }
        finally
        {
            w.AllowClose = true;
            w.Close();
        }
    });

    // ------------------------------------------------------------------ import

    [Fact]
    public void Png_and_jpeg_import_keeps_pixels_only_and_rejects_bad_files() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var w = new MainWindow(services) { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        string dir = Path.Combine(Path.GetTempPath(), "snapforge-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            PixelBuffer source = TestImages.Gradient(64, 40);
            // PNG with a text chunk (author/location-like metadata) that must not reach the Library.
            var png = new PngBitmapEncoder();
            var meta = new BitmapMetadata("png");
            meta.SetQuery("/tEXt/{str=Comment}", "secret-location-42");
            png.Frames.Add(BitmapFrame.Create(BitmapConversion.ToFrozenBitmap(source), null, meta, null));
            string pngPath = Path.Combine(dir, "Screenshot one.png");
            using (FileStream fs = File.Create(pngPath))
            {
                png.Save(fs);
            }

            Assert.Contains("secret-location-42", Encoding.ASCII.GetString(File.ReadAllBytes(pngPath)), StringComparison.Ordinal);
            string jpgPath = Path.Combine(dir, "photo.jpg");
            File.WriteAllBytes(jpgPath, ImageExporter.Encode(source, ImageFileFormat.Jpeg));
            string badPath = Path.Combine(dir, "broken.png");
            File.WriteAllText(badPath, "not an image");
            string gifPath = Path.Combine(dir, "anim.gif");
            File.WriteAllText(gifPath, "GIF89a");

            w.ShowImportErrors = false;
            IReadOnlyList<ImportOutcome> results = await w.ImportFilesAsync([pngPath, jpgPath, badPath, gifPath], openFirst: false);
            Assert.NotNull(results[0].Record);
            Assert.NotNull(results[1].Record);
            Assert.Null(results[2].Record);
            Assert.Null(results[3].Record);
            Assert.Equal("Screenshot one", results[0].Record!.Title);
            Assert.Equal(source.Pixels, services.Library.Load(results[0].Record!.Id).BaseImage.Pixels);
            Assert.Equal((64, 40), (results[1].Record!.Width, results[1].Record!.Height));
            Assert.True(services.Tray.Contains(results[0].Record!.Id));

            // Nothing from the file's metadata is stored in the capture folder.
            string folder = Path.Combine(services.Library.CapturesDirectory, results[0].Record!.Id.ToString("D"));
            foreach (string f in Directory.GetFiles(folder))
            {
                Assert.DoesNotContain("secret-location-42", Encoding.Latin1.GetString(File.ReadAllBytes(f)), StringComparison.Ordinal);
            }

            Assert.Equal("not an image", File.ReadAllText(badPath)); // originals untouched
        }
        finally
        {
            w.AllowClose = true;
            w.Close();
            Directory.Delete(dir, recursive: true);
        }
    });

    // ------------------------------------------------------------------ startup, theme, library start-up

    [Fact]
    public void Launch_with_windows_writes_and_removes_only_the_per_user_run_value()
    {
        string old = StartupRegistration.ValueName;
        StartupRegistration.ValueName = "SnapForgeTest-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            string exe = @"C:\Portable\SnapForge.exe";
            Assert.False(StartupRegistration.IsEnabled());
            StartupRegistration.Set(true, exe);
            Assert.True(StartupRegistration.IsEnabled());
            Assert.True(StartupRegistration.PointsAt(exe));
            Assert.False(StartupRegistration.PointsAt(@"D:\Other\SnapForge.exe"));
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath)!)
            {
                Assert.Equal("\"C:\\Portable\\SnapForge.exe\" --background", key.GetValue(StartupRegistration.ValueName));
            }

            StartupRegistration.Set(false, exe);
            Assert.False(StartupRegistration.IsEnabled());
            StartupRegistration.Set(false, exe); // idempotent
        }
        finally
        {
            StartupRegistration.Set(false, "x");
            StartupRegistration.ValueName = old;
        }
    }

    [Fact]
    public void High_contrast_palette_covers_every_theme_colour_from_system_colours() => UiThread.Run(() =>
    {
        var dark = new ResourceDictionary { Source = new Uri("pack://application:,,,/SnapForge;component/Themes/Palette.Dark.xaml") };
        ResourceDictionary hc = ThemeManager.HighContrastPalette();
        Assert.Equal(dark.Keys.Cast<string>().Order(), hc.Keys.Cast<string>().Order());
        Assert.Equal(SystemColors.WindowTextColor, ((SolidColorBrush)hc["TextBrush"]).Color);
        Assert.Equal(SystemColors.WindowColor, ((SolidColorBrush)hc["BackgroundBrush"]).Color);
        Assert.Equal(SystemColors.HighlightColor, ((SolidColorBrush)hc["AccentBrush"]).Color);
        return Task.CompletedTask;
    });

    [Fact]
    public void Library_opens_instantly_and_indexes_in_the_background_without_freezing_the_ui() => UiThread.Run(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "snapforge-apptests", Guid.NewGuid().ToString("N"));
        var seed = CaptureLibrary.Open(Path.Combine(root, "library"));
        PixelBuffer img = TestImages.Gradient(64, 40);
        var tray = RecentTray.Open(seed.Root, _ => true);
        for (int i = 0; i < 400; i++)
        {
            CaptureRecord r = seed.Create(new CaptureContent(img, []), img, DateTime.UtcNow.AddMinutes(-i), $"Seed {i}");
            if (i < 3)
            {
                tray.Add(r.Id);
            }
        }

        var clock = Stopwatch.StartNew();
        AppServices services = AppServices.Create(Path.Combine(root, "settings.json"), Path.Combine(root, "library"),
            s => s with { CloseToNotificationArea = false, PrivacyNoticeShown = true }, deferIndex: true);
        double openMs = clock.Elapsed.TotalMilliseconds;
        Assert.False(services.Library.IsIndexed);
        Assert.Equal(3, services.Tray.Items.Count); // tray kept while indexing (folders exist)
        var w = new MainWindow(services) { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        try
        {
            var gaps = new List<double>();
            var tick = Stopwatch.StartNew();
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) =>
            {
                gaps.Add(tick.Elapsed.TotalMilliseconds);
                tick.Restart();
            };
            timer.Start();
            Task indexing = services.StartIndexingAsync();
            await indexing;
            timer.Stop();
            Assert.True(services.Library.IsIndexed);
            Assert.Equal(400, services.Library.Records.Count);
            w.ShowLibraryWorkspace();
            await WaitFor(() => w.LibraryPane.VisibleCount == 400);
            await WaitFor(() => w.TrayKeys.Count == 3);
            Assert.True(openMs < 1500, $"opening took {openMs} ms");
            Assert.True(gaps.Count == 0 || gaps.Max() < 1000, $"UI froze for {gaps.Max()} ms while indexing");
        }
        finally
        {
            w.AllowClose = true;
            w.Close();
        }
    });

    [Fact]
    public void Library_storage_path_is_trimmed_with_full_path_in_tooltip() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        var w = new MainWindow(services) { Width = 1000, Height = 700, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        w.Show();
        try
        {
            w.ShowLibraryWorkspace();
            await Task.Delay(300);
            TextBlock path = w.LibraryPane.StoragePathText;
            Assert.Equal(TextTrimming.CharacterEllipsis, path.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, path.TextWrapping);
            Assert.Equal(services.Library.Root, path.ToolTip);
            Assert.Equal(services.Library.Root, path.Text);
        }
        finally
        {
            w.AllowClose = true;
            w.Close();
        }
    });

    private static async Task WaitFor(Func<bool> condition, int seconds = 15, Func<string>? diagnostics = null)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(seconds))
            {
                throw new TimeoutException("Condition not met in time. " + diagnostics?.Invoke());
            }

            await Task.Delay(25);
        }
    }
}
