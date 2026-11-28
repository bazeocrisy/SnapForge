using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SnapForge.App.Interop;
using SnapForge.App.Rendering;
using SnapForge.App.Services;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;

namespace SnapForge.App.Tests;

/// <summary>
/// End-to-end behaviour of the persistent Capture Library through the real main window (Editor, Library, tray),
/// each test with its own temporary library.
/// </summary>
[Collection(DesktopTestGroup.Name)]
public class LibraryWorkflowTests
{
    private static readonly string[] Titles = ["Alpha", "Beta", "Gamma"];

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

    private static async Task WaitFor(Func<bool> condition, int seconds = 15)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(seconds))
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(25);
        }
    }

    [Fact]
    public void New_capture_is_saved_automatically_and_survives_restart_with_editable_annotations() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        EditorSession session = w.LoadImage(TestImages.Gradient(320, 200), DateTime.Now);
        await WaitFor(() => session.InLibrary);
        var arrow = new ArrowAnnotation(new PointD(10, 10), new PointD(200, 150));
        session.Document.Execute(new AddAnnotationCommand(arrow));
        session.Document.Execute(new RedactCommand(new PixelRect(250, 150, 50, 40), ArgbColor.Black));
        Assert.True(session.Document.IsDirty);
        await WaitFor(() => !session.Document.IsDirty && !session.IsSaving); // debounced autosave, no explicit save
        Guid id = session.CaptureId!.Value;
        await Close(w);

        // "Restart": new services and window over the same folders.
        AppServices restarted = AppServices.Create(services.SettingsPath, services.Library.Root, s => s with { CloseToNotificationArea = false });
        CaptureRecord record = Assert.Single(restarted.Library.Records);
        Assert.Equal(id, record.Id);
        Assert.True(record.HasReversibleRedactions);
        Assert.Contains(restarted.Tray.Items, i => i.Id == id);

        MainWindow w2 = NewWindow(restarted);
        await w2.OpenCaptureAsync(id);
        EditorDocument doc = w2.Document!;
        Assert.Equal(arrow, doc.Annotations.OfType<ArrowAnnotation>().Single());
        Assert.Equal(ArgbColor.Black, doc.Image.GetPixel(260, 160));
        doc.Execute(new ReplaceAnnotationCommand(arrow, arrow.Translate(5, 5), "Move")); // still editable
        await Close(w2);
        Assert.Equal(new PointD(15, 15), restarted.Library.Load(id).Annotations.OfType<ArrowAnnotation>().Single().Start);
    });

    [Fact]
    public void Switching_between_editor_and_library_and_search() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        foreach (string title in Titles)
        {
            EditorSession s = w.LoadImage(TestImages.Gradient(64, 48), DateTime.Now);
            await WaitFor(() => s.InLibrary);
            services.Library.Rename(s.CaptureId!.Value, title);
        }

        w.ShowLibraryWorkspace();
        await Task.Delay(200);
        Assert.Equal(Visibility.Visible, w.LibraryPane.Visibility);
        Assert.Equal(Visibility.Collapsed, w.EditorWorkspace.Visibility);
        Assert.Equal(3, w.LibraryPane.VisibleCount);

        w.LibraryPane.SetSearch("gam");
        Assert.Equal(1, w.LibraryPane.VisibleCount);
        w.LibraryPane.SetSearch(string.Empty);
        Guid beta = services.Library.Records.Single(r => r.Title == "Beta").Id;
        services.Library.SetFavorite(beta, true);
        w.LibraryPane.SelectView(LibraryView.Favorites);
        Assert.Equal(1, w.LibraryPane.VisibleCount);

        await w.OpenCaptureAsync(beta);
        Assert.Equal(Visibility.Visible, w.EditorWorkspace.Visibility);
        Assert.Equal(beta, w.CurrentSession!.CaptureId);
        await Close(w);
    });

    [Fact]
    public void Tray_close_pin_and_delete_behave_as_specified() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        EditorSession a = w.LoadImage(TestImages.Gradient(50, 50), DateTime.Now);
        EditorSession b = w.LoadImage(TestImages.Gradient(60, 40), DateTime.Now);
        await WaitFor(() => a.InLibrary && b.InLibrary);
        Assert.Equal([b.CaptureId!.Value, a.CaptureId!.Value], services.Tray.Items.Select(i => i.Id));

        services.Tray.SetPinned(a.CaptureId.Value, true);
        Assert.Equal(a.CaptureId, services.Tray.Items[0].Id);

        services.Tray.Close(b.CaptureId.Value); // close != delete
        Assert.NotNull(services.Library.Find(b.CaptureId.Value));
        Assert.DoesNotContain(services.Tray.Items, i => i.Id == b.CaptureId);

        await w.DeleteCapturesAsync([a.CaptureId.Value]); // explicit delete → Trash, removed from tray
        Assert.True(services.Library.Find(a.CaptureId.Value)!.IsDeleted);
        Assert.Empty(services.Tray.Items);
        await Close(w);

        // Pin state persisted across restart.
        services.Library.Restore([a.CaptureId.Value]);
        services.Tray.Add(a.CaptureId.Value);
        services.Tray.SetPinned(a.CaptureId.Value, true);
        Assert.True(RecentTray.Open(services.Library.Root, _ => true).Items.Single().Pinned);
    });

    [Fact]
    public void Capture_without_history_is_not_written_to_the_library() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create(s => s with { Capture = s.Capture with { SaveToLibrary = false } });
        MainWindow w = NewWindow(services);
        EditorSession s = w.LoadImage(TestImages.Gradient(50, 50), DateTime.Now);
        await Task.Delay(500);
        Assert.False(s.InLibrary);
        Assert.Null(s.Creation);
        Assert.Empty(services.Library.Records);
        Assert.Empty(Directory.GetDirectories(services.Library.CapturesDirectory));
        Assert.Equal(Visibility.Visible, w.NoHistoryBanner.Visibility);
        s.Document.MarkSaved();
        await Close(w);
    });

    [Fact]
    public void Disk_full_during_autosave_keeps_edits_open_reports_and_recovers() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        EditorSession s = w.LoadImage(TestImages.Gradient(80, 60), DateTime.Now);
        await WaitFor(() => s.InLibrary && !s.IsSaving);

        AtomicFile.WriteDecorator.Value = stream => new FailingStream(stream);
        s.Document.Execute(new AddAnnotationCommand(new LineAnnotation(new PointD(0, 0), new PointD(40, 40))));
        await w.SaveSessionAsync(s);
        Assert.NotNull(s.LastSaveError);
        Assert.True(s.Document.IsDirty);
        Assert.Equal(Visibility.Visible, w.SaveErrorBanner.Visibility);
        Assert.Empty(services.Library.Load(s.CaptureId!.Value).Annotations); // previous version intact

        AtomicFile.WriteDecorator.Value = null;
        await w.SaveSessionAsync(s);
        Assert.Null(s.LastSaveError);
        Assert.Single(services.Library.Load(s.CaptureId.Value).Annotations);
        await Close(w);
    });

    [Fact]
    public void Sanitize_removes_original_pixels_from_the_library_copy() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        PixelBuffer img = TestImages.Gradient(120, 80);
        EditorSession s = w.LoadImage(img.Clone(), DateTime.Now);
        await WaitFor(() => s.InLibrary);
        s.Document.Execute(new RedactCommand(new PixelRect(10, 10, 40, 20), ArgbColor.Black));
        await w.SaveSessionAsync(s);
        Guid id = s.CaptureId!.Value;
        Assert.Equal(img.GetPixel(20, 15), services.Library.Load(id).BaseImage.GetPixel(20, 15)); // reversible copy
        Assert.Equal(Visibility.Visible, w.RedactionBanner.Visibility);

        await w.SanitizeSessionAsync(s);
        CaptureRecord r = services.Library.Find(id)!;
        Assert.False(r.HasReversibleRedactions);
        Assert.Equal(ArgbColor.Black, services.Library.Load(id).BaseImage.GetPixel(20, 15));
        Assert.Equal(ArgbColor.Black, services.Library.LoadThumbnail(id)!.GetPixel(20, 15)); // thumbnail regenerated from the sanitized image
        Assert.Equal(Visibility.Collapsed, w.RedactionBanner.Visibility);
        Assert.False(s.Document.History.CanUndo);
        await Close(w);
    });

    [Fact]
    public void FA007_storage_location_change_copies_verifies_and_deletes_old_files_only_after_confirmation() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        EditorSession s = w.LoadImage(TestImages.Gradient(40, 40), DateTime.Now);
        await WaitFor(() => s.InLibrary);
        await w.FlushAllAsync();
        string oldRoot = services.Library.Root;
        string newRoot = Path.Combine(Path.GetTempPath(), "snapforge-apptests", Guid.NewGuid().ToString("N"), "moved");
        bool asked = false;

        LibraryRelocationOutcome outcome = await services.RelocateLibraryAsync(newRoot, copy =>
        {
            asked = true;
            // When asked, the new location is already saved durably and in use.
            Assert.Equal(Path.GetFullPath(newRoot), AppSettings.Load(services.SettingsPath).LibraryPath);
            return true;
        });
        Assert.True(asked);
        Assert.Null(outcome.SettingsError);
        Assert.Equal(Path.GetFullPath(newRoot), services.Library.Root);
        Assert.Single(services.Library.Records);
        Assert.NotNull(outcome.Cleanup);
        string oldCaptures = Path.Combine(oldRoot, "captures");
        Assert.True(!Directory.Exists(oldCaptures) || Directory.GetFiles(oldCaptures, "*", SearchOption.AllDirectories).Length == 0);
        services.Library.Load(s.CaptureId!.Value);
        Assert.Contains(services.Tray.Items, i => i.Id == s.CaptureId);
        await Close(w);
    });

    [Fact]
    public void FA007_declining_cleanup_keeps_the_complete_old_library() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        MainWindow w = NewWindow(services);
        EditorSession s = w.LoadImage(TestImages.Gradient(40, 40), DateTime.Now);
        await WaitFor(() => s.InLibrary);
        await w.FlushAllAsync();
        string oldRoot = services.Library.Root;
        int oldFiles = Directory.GetFiles(oldRoot, "*", SearchOption.AllDirectories).Length;
        string newRoot = Path.Combine(Path.GetTempPath(), "snapforge-apptests", Guid.NewGuid().ToString("N"), "moved");

        LibraryRelocationOutcome outcome = await services.RelocateLibraryAsync(newRoot, _ => false);
        Assert.Null(outcome.Cleanup);
        Assert.Equal(Path.GetFullPath(newRoot), services.Library.Root);
        Assert.Equal(oldFiles, Directory.GetFiles(oldRoot, "*", SearchOption.AllDirectories).Length);
        Assert.NotNull(Core.Library.CaptureLibrary.Open(oldRoot).Find(s.CaptureId!.Value));
        await Close(w);
    });

    [Fact]
    public void Large_library_view_loads_lazily() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        PixelBuffer tiny = TestImages.Gradient(32, 20);
        await Task.Run(() =>
        {
            AtomicFile.SkipDiskFlush.Value = true; // seeding only
            for (int i = 0; i < 600; i++)
            {
                services.Library.Create(new CaptureContent(tiny, []), tiny, DateTime.UtcNow.AddMinutes(-i), $"Capture {i}");
            }
        });

        MainWindow w = NewWindow(services);
        var sw = Stopwatch.StartNew();
        w.ShowLibraryWorkspace();
        await Task.Delay(800);
        long ms = sw.ElapsedMilliseconds;
        Assert.Equal(600, w.LibraryPane.VisibleCount);
        Assert.True(services.Thumbnails.Count < 300, $"{services.Thumbnails.Count} thumbnails loaded; expected only visible ones");
        Assert.True(ms < 10000, $"Library view took {ms} ms");
        await Close(w);
    }, timeoutSeconds: 300);

    [Fact]
    public void Global_hotkey_conflicts_are_reported_not_silently_ignored() => UiThread.Run(() =>
    {
        using var first = new ShellIntegration();
        using var second = new ShellIntegration();
        var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "F11");
        IReadOnlyList<HotkeyRegistration> a = first.RegisterHotkeys([("Capture", gesture, () => { })]);
        Assert.True(a.Single().Succeeded, a.Single().Error);

        IReadOnlyList<HotkeyRegistration> b = second.RegisterHotkeys([("Capture", gesture, () => { })]);
        Assert.False(b.Single().Succeeded);
        Assert.False(string.IsNullOrEmpty(b.Single().Error));

        IReadOnlyList<HotkeyRegistration> c = first.RegisterHotkeys(
        [
            ("Capture", gesture, () => { }),
            ("Preset: A", gesture, () => { }),
            ("Preset: B", new HotkeyGesture(HotkeyModifiers.None, "A"), () => { }),
        ]);
        Assert.True(c[0].Succeeded);
        Assert.Contains("another SnapForge action", c[1].Error, StringComparison.Ordinal);
        Assert.False(c[2].Succeeded);
        return Task.CompletedTask;
    });

    private sealed class FailingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("There is not enough space on the disk.");
    }
}
