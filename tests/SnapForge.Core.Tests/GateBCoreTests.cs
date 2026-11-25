using SnapForge.Core.Capture;
using SnapForge.Core.Export;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>Milestone 3 (Gate B) core logic: widget docking, window positions, file names, temp cleanup, tray order, settings.</summary>
public class GateBCoreTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    // Primary 1920×1080 at 100 %; second monitor 2560×1440 at 150 % to the LEFT (negative coordinates).
    private static ScreenLayout TwoMonitors() => new(
    [
        new MonitorInfo(@"\\.\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true, 1.0),
        new MonitorInfo(@"\\.\DISPLAY2", new PixelRect(-2560, -200, 2560, 1440), new PixelRect(-2560, -200, 2560, 1400), false, 1.5),
    ]);

    [Fact]
    public void Widget_defaults_to_top_centre_of_primary_and_is_enabled_and_topmost()
    {
        var s = new WidgetSettings();
        Assert.True(s.Enabled);
        Assert.True(s.Topmost);
        Assert.Equal(WidgetExpandMode.Hover, s.ExpandMode);
        Assert.False(s.AutoHide);
        PixelRect r = WidgetDock.Place(TwoMonitors(), null, along: 200, across: 20);
        Assert.Equal(new PixelRect(860, 0, 200, 20), r);
    }

    [Theory]
    [InlineData(DockEdge.Top, 0.0, -2560, -200)] // clamped to the monitor's left edge
    [InlineData(DockEdge.Top, 1.0, -200, -200)] // clamped to the right edge (width 200)
    [InlineData(DockEdge.Left, 0.5, -2560, 420)] // vertical centre: -200 + 720 - 100
    [InlineData(DockEdge.Right, 0.5, -20, 420)]
    public void Widget_stays_on_its_monitor_and_touches_the_docked_edge(DockEdge edge, double offset, int x, int y)
    {
        PixelRect r = WidgetDock.Place(TwoMonitors(), new WidgetPlacement(@"\\.\DISPLAY2", edge, offset), along: 200, across: 20);
        Assert.Equal(x, r.X);
        Assert.Equal(y, r.Y);
        Assert.True(new PixelRect(-2560, -200, 2560, 1440).Contains(r.X, r.Y));
        Assert.True(new PixelRect(-2560, -200, 2560, 1440).Contains(r.Right - 1, r.Bottom - 1));
    }

    [Fact]
    public void Widget_on_a_removed_monitor_falls_back_to_the_primary()
    {
        var onlyPrimary = new ScreenLayout([new MonitorInfo(@"\\.\DISPLAY1", new PixelRect(0, 0, 1280, 720), new PixelRect(0, 0, 1280, 680), true, 1.25)]);
        PixelRect r = WidgetDock.Place(onlyPrimary, new WidgetPlacement(@"\\.\DISPLAY2", DockEdge.Right, 0.25), along: 200, across: 20);
        Assert.Equal(new PixelRect(1260, 80, 20, 200), r);
    }

    [Fact]
    public void Dropping_the_widget_picks_monitor_nearest_edge_and_offset()
    {
        ScreenLayout layout = TwoMonitors();
        WidgetPlacement top = WidgetDock.FromPoint(layout, 960, 30);
        Assert.Equal((@"\\.\DISPLAY1", DockEdge.Top), (top.MonitorDeviceName, top.Edge));
        Assert.Equal(0.5, top.Offset, 2);

        WidgetPlacement left = WidgetDock.FromPoint(layout, -2540, 600);
        Assert.Equal((@"\\.\DISPLAY2", DockEdge.Left), (left.MonitorDeviceName, left.Edge));

        WidgetPlacement right = WidgetDock.FromPoint(layout, 1910, 700);
        Assert.Equal(DockEdge.Right, right.Edge);

        // A point off every monitor snaps to the nearest one.
        WidgetPlacement off = WidgetDock.FromPoint(layout, 5000, 500);
        Assert.Equal((@"\\.\DISPLAY1", DockEdge.Right), (off.MonitorDeviceName, off.Edge));

        // Round trip: placing at the returned placement keeps the widget on that monitor and edge.
        PixelRect r = WidgetDock.Place(layout, left, 200, 20);
        Assert.Equal(-2560, r.X);
    }

    [Fact]
    public void Window_position_is_kept_when_visible_and_clamped_when_not()
    {
        ScreenLayout layout = TwoMonitors();
        Assert.Equal(new PixelRect(580, 395, 760, 250), WindowPosition.Clamp(layout, null, 760, 250));
        Assert.Equal(new PixelRect(100, 100, 760, 250), WindowPosition.Clamp(layout, new WindowPosition(@"\\.\DISPLAY1", 100, 100), 760, 250));
        // Off the bottom of the work area (taskbar) → moved up.
        Assert.Equal(new PixelRect(100, 790, 760, 250), WindowPosition.Clamp(layout, new WindowPosition(@"\\.\DISPLAY1", 100, 1000), 760, 250));
        // Monitor gone → onto the primary.
        var one = new ScreenLayout([new MonitorInfo(@"\\.\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true, 1.0)]);
        PixelRect moved = WindowPosition.Clamp(one, new WindowPosition(@"\\.\DISPLAY2", -2000, 300), 760, 250);
        Assert.Equal(0, moved.X);
        Assert.Equal(300, moved.Y);
    }

    [Fact]
    public void Export_file_names_are_meaningful_timestamped_safe_and_unique()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var t = new DateTime(2026, 11, 25, 13, 49, 5);
        Assert.Equal("Checkout error 2026-11-25 134905.png", ExportFileNames.For("Checkout error", t, ImageFileFormat.Png, used));
        Assert.Equal("Checkout error 2026-11-25 134905 (2).png", ExportFileNames.For("Checkout error", t, ImageFileFormat.Png, used));
        Assert.Equal("Checkout error 2026-11-25 134905 (3).png", ExportFileNames.For("checkout ERROR", t, ImageFileFormat.Png, used).Replace("checkout ERROR", "Checkout error", StringComparison.Ordinal));
        Assert.Equal("a b c d. e 2026-11-25 134905.jpg", ExportFileNames.For("a<b>c|d: e?*", t, ImageFileFormat.Jpeg, used));
        Assert.Equal("SnapForge 2026-11-25 134905.png", ExportFileNames.For("  \t ", t, ImageFileFormat.Png, used));
        // A default title that already carries the date is not stamped twice.
        Assert.Equal("Capture 2026-11-25 13.49.png", ExportFileNames.For("Capture 2026-11-25 13:49", t, ImageFileFormat.Png, used));
        // Reserved device names: the timestamp already makes "CON …" safe; a bare reserved stem is prefixed.
        Assert.Equal("CON 2026-11-25 134905.png", ExportFileNames.For("CON", t, ImageFileFormat.Png, new HashSet<string>()));
        Assert.Equal("_NUL.2026-11-25.png", ExportFileNames.For("NUL.2026-11-25", t, ImageFileFormat.Png, new HashSet<string>()));
        // Trailing dots/spaces removed; very long titles truncated.
        Assert.Equal("Report 2026-11-25 134905.png", ExportFileNames.For("Report... ", t, ImageFileFormat.Png, new HashSet<string>()));
        string longName = ExportFileNames.For(new string('x', 500), t, ImageFileFormat.Png, new HashSet<string>());
        Assert.True(Path.GetFileNameWithoutExtension(longName).Length <= ExportFileNames.MaxStemLength);
        Assert.EndsWith("2026-11-25 134905.png", longName, StringComparison.Ordinal);
        foreach (char c in Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*"))
        {
            Assert.DoesNotContain(c, ExportFileNames.For($"x{c}y", t, ImageFileFormat.Png, new HashSet<string>()));
        }
    }

    [Fact]
    public void Temp_export_sessions_are_private_folders_and_swept_after_the_grace_period()
    {
        using var dir = new TestDirectory();
        var store = new TempExportStore(dir.Sub("Drag"));
        string a = store.NewSession(Now);
        string b = store.NewSession(Now.AddMinutes(5));
        Assert.NotEqual(a, b);
        File.WriteAllText(Path.Combine(a, "x.png"), "x");
        File.WriteAllText(Path.Combine(b, "y.png"), "y");
        Directory.CreateDirectory(Path.Combine(store.Root, "not-a-session")); // unknown folders are cleaned too

        Assert.Equal(0, store.Sweep(Now.AddMinutes(1), TempExportStore.GracePeriod) - 1); // only the unknown folder
        Assert.True(Directory.Exists(a));
        Assert.Equal(1, store.Sweep(Now.AddMinutes(3), TempExportStore.GracePeriod)); // a is older than 2 min
        Assert.False(Directory.Exists(a));
        Assert.True(Directory.Exists(b));
        Assert.True(TempExportStore.DeleteSession(b));
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.Root));
        string c = store.NewSession(Now);
        Assert.Equal(1, store.DeleteAll(Now));
        Assert.False(Directory.Exists(c));
        Assert.Equal(0, new TempExportStore(dir.Sub("missing")).Sweep(Now, TimeSpan.Zero));
    }

    [Fact]
    public void Tray_reorder_keeps_sections_and_persists()
    {
        static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");
        using var dir = new TestDirectory();
        var tray = RecentTray.Open(dir.Path, _ => true);
        foreach (int i in new[] { 4, 3, 2, 1 })
        {
            tray.Add(G(i));
        }

        tray.SetPinned(G(4), true); // [4p, 1, 2, 3]
        Assert.True(tray.Move(G(3), 1)); // [4p, 3, 1, 2]
        Assert.Equal([G(4), G(3), G(1), G(2)], tray.Items.Select(i => i.Id));
        Assert.True(tray.Move(G(1), 0)); // clamped into the unpinned section: [4p, 1, 3, 2]
        Assert.Equal([G(4), G(1), G(3), G(2)], tray.Items.Select(i => i.Id));
        Assert.False(tray.Move(G(4), 3)); // pinned item cannot leave the pinned section
        Assert.False(tray.Move(Guid.NewGuid(), 0));
        Assert.Equal(tray.Items, RecentTray.Open(dir.Path, _ => true).Items);
    }

    [Fact]
    public void Old_settings_files_get_new_defaults_and_the_misleading_preset_is_renamed_only_if_unchanged()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("settings.json");
        // A milestone-2 settings file: no Widget/DragDrop keys; built-in preset with its old name; one renamed copy.
        File.WriteAllText(path, $$"""
            {
              "Version": 1,
              "Presets": [
                { "Id": "{{CapturePreset.DelayedRegionPresetId}}", "Name": "{{CapturePreset.LegacyMenuPresetName}}", "Options": { "Mode": "Region", "DelaySeconds": 3, "CaptureCursor": true } },
                { "Id": "11111111-1111-1111-1111-111111111111", "Name": "{{CapturePreset.LegacyMenuPresetName}}", "Options": { "Mode": "Region" } }
              ],
              "Capture": { "DelaySeconds": 500 }
            }
            """);
        AppSettings s = AppSettings.Load(path);
        Assert.True(s.Widget.Enabled);
        Assert.True(s.Widget.Topmost);
        Assert.Null(s.Widget.Placement);
        Assert.Equal(ImageFileFormat.Png, s.DragDrop.Format);
        Assert.False(s.LaunchAtStartup);
        Assert.Equal(CaptureOptions.MaxDelaySeconds, s.Capture.DelaySeconds);
        Assert.Equal(CapturePreset.DelayedRegionPresetName, s.Presets[0].Name);
        Assert.Equal(CapturePreset.LegacyMenuPresetName, s.Presets[1].Name); // not the built-in preset: user's own
        Assert.DoesNotContain(CapturePreset.Defaults, p => p.Name.Contains("Menu", StringComparison.OrdinalIgnoreCase));

        AppSettings changed = s with
        {
            Widget = s.Widget with { Topmost = false, ExpandMode = WidgetExpandMode.Click, AutoHide = true, Placement = new WidgetPlacement(@"\\.\DISPLAY2", DockEdge.Left, 0.25) },
            DragDrop = new DragDropSettings { Format = ImageFileFormat.Jpeg, JpegQuality = 400 },
            LaunchAtStartup = true,
            CapturePanelPosition = new WindowPosition(@"\\.\DISPLAY1", 10, 20),
        };
        changed.Save(path);
        AppSettings back = AppSettings.Load(path);
        Assert.Equal(changed.Widget, back.Widget);
        Assert.Equal(100, back.DragDrop.JpegQuality); // normalized
        Assert.Equal(ImageFileFormat.Jpeg, back.DragDrop.Format);
        Assert.True(back.LaunchAtStartup);
        Assert.Equal(changed.CapturePanelPosition, back.CapturePanelPosition);
    }

    [Fact]
    public async Task Deferred_open_indexes_in_background_and_keeps_captures_written_meanwhile()
    {
        using var dir = new TestDirectory();
        PixelBuffer img = PixelBufferTests.Gradient(20, 10);
        var seed = CaptureLibrary.Open(dir.Path);
        for (int i = 0; i < 30; i++)
        {
            seed.Create(new CaptureContent(img, []), img, Now.AddMinutes(i), $"seed {i}");
        }

        // Leftover temp file from a crash is cleaned by CompleteOpen, not by OpenDeferred.
        string leftover = Path.Combine(dir.Path, "captures", "x.sftmp");
        File.WriteAllText(leftover, "partial");

        var lib = CaptureLibrary.OpenDeferred(dir.Path);
        Assert.False(lib.IsIndexed);
        Assert.Empty(lib.Records);
        Assert.True(File.Exists(leftover));
        Assert.True(lib.ExistsOnDisk(seed.Records[0].Id));

        // A capture created before indexing finishes must survive the index replacing the record set.
        CaptureRecord early = lib.Create(new CaptureContent(img, []), img, Now, "taken during start-up");
        bool raised = false;
        lib.Indexed += (_, _) => raised = true;
        await Task.Run(lib.CompleteOpen);
        Assert.True(lib.IsIndexed);
        Assert.True(raised);
        Assert.Equal(31, lib.Records.Count);
        Assert.NotNull(lib.Find(early.Id));
        Assert.False(File.Exists(leftover));
    }
}

public class ImageImportTests
{
    [Fact]
    public void Import_flattens_transparency_onto_white_and_keeps_opaque_pixels_exact()
    {
        byte[] px =
        [
            10, 20, 30, 255, // opaque: unchanged
            0, 0, 0, 0, // fully transparent: white
            0, 0, 255, 128, // half red over white
            200, 100, 50, 255,
        ];
        PixelBuffer img = ImageImport.FromStraightBgra(2, 2, px);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 255, 255, 255, 255, 127, 127, 255, 255, 200, 100, 50, 255 }, img.Pixels);
    }

    [Fact]
    public void Import_rejects_bad_sizes_and_types()
    {
        Assert.NotNull(ImageImport.CheckSize(0, 10));
        Assert.NotNull(ImageImport.CheckSize(ImageImport.MaxSide + 1, 10));
        Assert.NotNull(ImageImport.CheckSize(12000, 12000)); // 144 MP
        Assert.Null(ImageImport.CheckSize(1920, 1080));
        Assert.Throws<ArgumentException>(() => ImageImport.FromStraightBgra(2, 2, new byte[3]));
        Assert.True(ImageImport.IsSupportedPath(@"C:\x\Shot.PNG"));
        Assert.True(ImageImport.IsSupportedPath("a.jpeg"));
        Assert.False(ImageImport.IsSupportedPath("a.sfcap"));
        Assert.False(ImageImport.IsSupportedPath("a.gif"));
        Assert.Equal("Shot", ImageImport.TitleFor(Path.Combine("x", "Shot.PNG")));
        Assert.Equal("Imported image", ImageImport.TitleFor(".png"));
    }
}
