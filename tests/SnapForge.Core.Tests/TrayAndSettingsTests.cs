using SnapForge.Core.Annotations;
using SnapForge.Core.Capture;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;

namespace SnapForge.Core.Tests;

public class TrayAndSettingsTests
{
    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    [Fact]
    public void Tray_orders_pinned_first_then_most_recent_and_persists()
    {
        using var dir = new TestDirectory();
        var tray = RecentTray.Open(dir.Path, _ => true);
        tray.Add(G(1));
        tray.Add(G(2));
        tray.Add(G(3));
        tray.SetPinned(G(1), true);
        tray.Add(G(2)); // re-opened: moves to the front of the unpinned section
        Assert.Equal([G(1), G(2), G(3)], tray.Items.Select(i => i.Id));
        Assert.True(tray.Items[0].Pinned);

        var reopened = RecentTray.Open(dir.Path, _ => true);
        Assert.Equal(tray.Items, reopened.Items);
    }

    [Fact]
    public void Tray_close_does_not_delete_and_prune_drops_missing()
    {
        using var dir = new TestDirectory();
        var tray = RecentTray.Open(dir.Path, _ => true);
        tray.Add(G(1));
        tray.Add(G(2));
        tray.Close(G(1));
        Assert.Equal([G(2)], tray.Items.Select(i => i.Id));
        tray.Prune(id => id != G(2));
        Assert.Empty(tray.Items);
        tray.Add(G(5));
        Assert.Empty(RecentTray.Open(dir.Path, _ => false).Items); // missing captures are dropped on load
    }

    [Fact]
    public void Tray_caps_unpinned_items_but_keeps_pins()
    {
        using var dir = new TestDirectory();
        var tray = RecentTray.Open(dir.Path, _ => true);
        tray.Add(G(999));
        tray.SetPinned(G(999), true);
        for (int i = 0; i < RecentTray.MaxUnpinned + 10; i++)
        {
            tray.Add(G(i));
        }

        Assert.Equal(RecentTray.MaxUnpinned + 1, tray.Items.Count);
        Assert.Equal(G(999), tray.Items[0].Id);
        tray.CloseAllUnpinned();
        Assert.Equal([G(999)], tray.Items.Select(i => i.Id));
    }

    [Fact]
    public void Damaged_tray_file_is_ignored()
    {
        using var dir = new TestDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "tray.json"), "garbage{");
        Assert.Empty(RecentTray.Open(dir.Path, _ => true).Items);
    }

    [Theory]
    [InlineData("Ctrl+Shift+PrintScreen", HotkeyModifiers.Control | HotkeyModifiers.Shift, "PrintScreen")]
    [InlineData("PrtSc", HotkeyModifiers.None, "PrintScreen")]
    [InlineData("alt + f9", HotkeyModifiers.Alt, "f9")]
    [InlineData("Win+Shift+S", HotkeyModifiers.Windows | HotkeyModifiers.Shift, "S")]
    public void Hotkey_parsing(string text, HotkeyModifiers mods, string key)
    {
        HotkeyGesture g = HotkeyGesture.Parse(text)!;
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
        Assert.Equal(g, HotkeyGesture.Parse(g.ToString()) is { } back ? back with { Key = g.Key } : null);
    }

    [Fact]
    public void Hotkey_validation()
    {
        Assert.True(HotkeyGesture.Parse("PrintScreen")!.IsValid);
        Assert.True(HotkeyGesture.Parse("F9")!.IsValid);
        Assert.False(HotkeyGesture.Parse("A")!.IsValid);
        Assert.False(HotkeyGesture.Parse("Shift+A")!.IsValid);
        Assert.True(HotkeyGesture.Parse("Ctrl+Shift+A")!.IsValid);
        Assert.False(new HotkeyGesture(HotkeyModifiers.Control, "LeftShift").IsValid);
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse("Ctrl+A+B"));
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse("Ctrl+Shift"));
        Assert.Null(HotkeyGesture.Parse(" "));
        Assert.True(new HotkeyGesture(HotkeyModifiers.None, "Snapshot").SameKeysAs(HotkeyGesture.Parse("PrintScreen")));
    }

    [Fact]
    public void Hotkey_conflicts_are_detected_within_snapforge()
    {
        var settings = new AppSettings
        {
            CaptureHotkey = HotkeyGesture.Parse("Ctrl+Shift+1"),
            Presets = [new CapturePreset { Name = "A", Hotkey = HotkeyGesture.Parse("Ctrl+Shift+1") }, new CapturePreset { Name = "B", Hotkey = HotkeyGesture.Parse("Ctrl+Shift+2") }],
        };
        IReadOnlyList<IReadOnlyList<string>> conflicts = HotkeyGesture.FindConflicts(settings.HotkeyAssignments());
        Assert.Equal(["Capture", "Preset: A"], Assert.Single(conflicts));
    }

    [Fact]
    public void Settings_round_trip_and_defaults()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "settings.json");
        Assert.Equal(new AppSettings().Capture, AppSettings.Load(path).Capture);
        var s = new AppSettings
        {
            LibraryPath = @"D:\Shots",
            Capture = new CaptureOptions { Mode = CaptureMode.Window, DelaySeconds = 3, CaptureCursor = true, SaveToLibrary = false },
            Theme = ThemePreference.Light,
            CaptureHotkey = HotkeyGesture.Parse("Ctrl+Alt+P"),
            UserQuickStyles = [new QuickStyle { Name = "Mine", Color = ArgbColor.Parse("#FF00AA00"), StrokeWidth = 3 }],
            LastTool = AnnotationTool.Highlight,
        };
        s.Save(path);
        AppSettings back = AppSettings.Load(path);
        Assert.Equal(s.LibraryPath, back.LibraryPath);
        Assert.Equal(s.Capture, back.Capture);
        Assert.Equal(ThemePreference.Light, back.Theme);
        Assert.Equal("Ctrl+Alt+P", back.CaptureHotkey!.ToString());
        Assert.Equal(s.UserQuickStyles[0], back.UserQuickStyles[0]);
        Assert.Equal(AnnotationTool.Highlight, back.LastTool);
        Assert.Equal(CapturePreset.Defaults.Count, back.Presets.Count);
    }

    [Fact]
    public void Damaged_settings_fall_back_to_defaults_and_keep_the_bad_file()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ \"Version\": 1, \"Theme\": \"Neon\" }");
        AppSettings s = AppSettings.Load(path);
        Assert.Equal(ThemePreference.System, s.Theme);
        Assert.True(File.Exists(path + ".damaged"));
    }

    [Fact]
    public void Defaults_save_to_library_and_built_in_styles_are_unique()
    {
        var s = new AppSettings();
        Assert.True(s.Capture.SaveToLibrary);
        Assert.True(s.Capture.PreviewInEditor);
        Assert.Equal(QuickStyle.BuiltIns.Count, QuickStyle.BuiltIns.Select(q => q.Id).Distinct().Count());
        Assert.Contains(CapturePreset.Defaults, p => !p.Options.SaveToLibrary);
    }

    [Fact]
    public void Crop_cuts_images_shifts_annotations_clips_masks_and_undoes()
    {
        var doc = new EditorDocument(PixelBufferTests.Gradient(100, 80));
        PixelBuffer before = doc.Image.Clone();
        var arrow = new ArrowAnnotation(new PointD(30, 30), new PointD(60, 50));
        doc.Execute(new AddAnnotationCommand(arrow));
        doc.Execute(new RedactCommand(new PixelRect(0, 0, 25, 25), ArgbColor.Black));
        doc.Execute(new RedactCommand(new PixelRect(90, 70, 10, 10), ArgbColor.Black)); // will be cropped away
        doc.Execute(new CropCommand(new PixelRect(20, 20, 50, 40)));

        Assert.Equal((50, 40), (doc.Image.Width, doc.BaseImage.Width == 50 ? doc.Image.Height : -1));
        Assert.Equal(new PointD(10, 10), ((ArrowAnnotation)doc.FindById(arrow.Id)!).Start);
        RedactionMaskAnnotation mask = Assert.Single(doc.Annotations.OfType<RedactionMaskAnnotation>());
        Assert.Equal(new PixelRect(0, 0, 5, 5), mask.Region);
        Assert.Equal(ArgbColor.Black, doc.Image.GetPixel(2, 2));
        Assert.Equal(before.GetPixel(20 + 10, 20 + 10), doc.Image.GetPixel(10, 10));

        doc.Undo();
        Assert.Equal(100, doc.Image.Width);
        Assert.Equal(new PointD(30, 30), ((ArrowAnnotation)doc.FindById(arrow.Id)!).Start);
        Assert.Equal(2, doc.Annotations.OfType<RedactionMaskAnnotation>().Count());
    }

    [Fact]
    public void Highlight_annotation_hit_testing_and_degenerate()
    {
        var h = new HighlightAnnotation(new RectD(10, 10, 20, 5));
        Assert.True(h.HitTest(new PointD(15, 12), 0));
        Assert.False(h.HitTest(new PointD(50, 50), 2));
        Assert.True(new HighlightAnnotation(new RectD(0, 0, 1, 10)).IsDegenerate);
        Assert.Equal(HighlightAnnotation.DefaultOpacity, h.Opacity);
    }

    [Fact]
    public void Loading_document_reapplies_masks_and_undo_of_reloaded_redaction_is_not_possible_without_history()
    {
        PixelBuffer img = PixelBufferTests.Gradient(20, 20);
        var mask = RedactionMaskAnnotation.For(new PixelRect(2, 2, 4, 4), ArgbColor.Black);
        var doc = new EditorDocument(img.Clone(), [mask]);
        Assert.Equal(ArgbColor.Black, doc.Image.GetPixel(3, 3));
        Assert.Equal(img.GetPixel(3, 3), doc.BaseImage.GetPixel(3, 3));
        Assert.False(doc.History.CanUndo);
        Assert.True(doc.HasReversibleRedactions);
        Assert.False(doc.IsDirty);
    }
}
