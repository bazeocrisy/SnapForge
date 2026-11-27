using System.Diagnostics;
using System.IO.Compression;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

public class CaptureLibraryTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    private static CaptureRecord Add(CaptureLibrary lib, string title, DateTime? created = null, int w = 40, int h = 30, IReadOnlyList<Annotation>? annotations = null)
    {
        PixelBuffer img = PixelBufferTests.Gradient(w, h);
        return lib.Create(new CaptureContent(img, annotations ?? []), img, created ?? Now, title);
    }

    [Fact]
    public void Created_capture_survives_reopen_with_pixels_annotations_and_metadata()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        List<Annotation> anns = CaptureFormatTests.AllKinds();
        PixelBuffer img = PixelBufferTests.Gradient(64, 48);
        CaptureRecord rec = lib.Create(new CaptureContent(img, anns), img, Now, "Bug report");
        lib.SetTags(rec.Id, ["jira", "UI", "jira "]);
        lib.SetFavorite(rec.Id, true);
        lib.SetFolder(rec.Id, "Project X");

        // "Restart": a brand-new library instance over the same folder.
        var reopened = CaptureLibrary.Open(dir.Path);
        CaptureRecord r = Assert.Single(reopened.Records);
        Assert.Equal(rec.Id, r.Id);
        Assert.Equal("Bug report", r.Title);
        Assert.Equal(["jira", "UI"], r.Meta.Tags);
        Assert.True(r.IsFavorite);
        Assert.Equal("Project X", r.Meta.Folder);
        Assert.Equal((64, 48), (r.Width, r.Height));
        Assert.True(r.HasReversibleRedactions);
        Assert.Null(r.Damage);

        CaptureContent c = reopened.Load(rec.Id);
        Assert.Equal(img.Pixels, c.BaseImage.Pixels);
        Assert.Equal(anns, c.Annotations);
        Assert.NotNull(reopened.LoadThumbnail(rec.Id));
    }

    [Fact]
    public void Reopened_annotations_are_editable_and_redactions_reapplied()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        var doc = new EditorDocument(PixelBufferTests.Gradient(50, 40));
        byte[] original = (byte[])doc.Image.Pixels.Clone();
        doc.Execute(new AddAnnotationCommand(new ArrowAnnotation(new PointD(1, 1), new PointD(30, 30))));
        doc.Execute(new RedactCommand(new PixelRect(5, 5, 10, 10), ArgbColor.Black));
        CaptureRecord rec = lib.Create(new CaptureContent(doc.BaseImage, doc.Annotations), doc.Image, Now);

        CaptureContent c = CaptureLibrary.Open(dir.Path).Load(rec.Id);
        var reopened = new EditorDocument(c.BaseImage, c.Annotations);
        Assert.Equal(ArgbColor.Black, reopened.Image.GetPixel(7, 7));
        Assert.Equal(original, reopened.BaseImage.Pixels); // editable copy still holds the originals (documented)
        Assert.True(reopened.HasReversibleRedactions);

        // Editable after reload: move the arrow, then delete the redaction mask via a new document state.
        Annotation arrow = reopened.Annotations.OfType<ArrowAnnotation>().Single();
        reopened.Execute(new ReplaceAnnotationCommand(arrow, arrow.Translate(5, 0), "Move"));
        Assert.Equal(new PointD(6, 1), ((ArrowAnnotation)reopened.FindById(arrow.Id)!).Start);
    }

    [Fact]
    public void Save_without_new_base_rewrites_only_small_files()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord rec = Add(lib, "a");
        string baseFile = Path.Combine(lib.CapturesDirectory, rec.Id.ToString("D"), CaptureFormat.BaseFile);
        DateTime stamp = File.GetLastWriteTimeUtc(baseFile);
        Thread.Sleep(20);
        PixelBuffer img = lib.Load(rec.Id).BaseImage;
        lib.SaveContent(rec.Id, null, [new LineAnnotation(new PointD(0, 0), new PointD(5, 5))], img, false);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(baseFile));
        Assert.Single(lib.Load(rec.Id).Annotations);
        Assert.Equal(1, lib.Find(rec.Id)!.AnnotationCount);
    }

    [Fact]
    public void Interrupted_save_keeps_previous_version_intact()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord rec = Add(lib, "a");
        PixelBuffer img = lib.Load(rec.Id).BaseImage;
        AtomicFile.WriteDecorator.Value = s => new FailingStream(s, 10, () => new IOException("There is not enough space on the disk."));
        try
        {
            Assert.Throws<IOException>(() => lib.SaveContent(rec.Id, PixelBufferTests.Gradient(10, 10), [new LineAnnotation(new PointD(0, 0), new PointD(5, 5))], img, false));
        }
        finally
        {
            AtomicFile.WriteDecorator.Value = null;
        }

        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.Null(reopened.Find(rec.Id)!.Damage);
        Assert.Equal(img.Pixels, reopened.Load(rec.Id).BaseImage.Pixels);
        Assert.Empty(Directory.GetFiles(dir.Path, "*" + AtomicFile.TempSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void Crash_after_base_but_before_manifest_is_detected_as_damage_not_silently_loaded()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord rec = Add(lib, "a");
        // Simulate: new base written, then crash before the manifest (commit point) was updated.
        File.WriteAllBytes(Path.Combine(lib.CapturesDirectory, rec.Id.ToString("D"), CaptureFormat.BaseFile), PixelCodec.Encode(PixelBufferTests.Gradient(5, 5)));
        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.Throws<InvalidDataException>(() => reopened.Load(rec.Id));
    }

    [Fact]
    public void Corrupt_manifest_is_listed_as_damaged_and_other_captures_still_load()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord good = Add(lib, "good");
        CaptureRecord bad = Add(lib, "bad");
        File.WriteAllText(Path.Combine(lib.CapturesDirectory, bad.Id.ToString("D"), CaptureFormat.ManifestFile), "{ not json");
        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.NotNull(reopened.Find(bad.Id)!.Damage);
        Assert.Null(reopened.Find(good.Id)!.Damage);
        reopened.Load(good.Id);
    }

    [Fact]
    public void Trash_restore_and_permanent_delete()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord a = Add(lib, "a"), b = Add(lib, "b");
        lib.MoveToTrash([a.Id], Now);
        Assert.Equal([b.Id], lib.Query(new LibraryQuery(), Now).Select(r => r.Id));
        Assert.Equal([a.Id], lib.Query(new LibraryQuery { View = LibraryView.Trash }, Now).Select(r => r.Id));
        lib.Restore([a.Id]);
        Assert.Equal(2, lib.Query(new LibraryQuery(), Now).Count);
        lib.MoveToTrash([a.Id], Now);
        Assert.Equal(1, lib.EmptyTrash());
        Assert.False(Directory.Exists(Path.Combine(lib.CapturesDirectory, a.Id.ToString("D"))));
        Assert.Null(CaptureLibrary.Open(dir.Path).Find(a.Id));
    }

    [Fact]
    public void Duplicate_creates_independent_copy()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord a = Add(lib, "Shot", annotations: [new LineAnnotation(new PointD(0, 0), new PointD(3, 3))]);
        lib.SetTags(a.Id, ["x"]);
        CaptureRecord copy = lib.Duplicate(a.Id, Now);
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal("Shot (copy)", copy.Title);
        Assert.Equal(["x"], copy.Meta.Tags);
        Assert.NotEqual(lib.Load(a.Id).Annotations[0].Id, lib.Load(copy.Id).Annotations[0].Id);
        lib.DeletePermanently([a.Id]);
        lib.Load(copy.Id);
    }

    [Fact]
    public void Views_search_and_sort()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord old = Add(lib, "Login page", Now.AddDays(-30), 100, 100);
        CaptureRecord mid = Add(lib, "Settings dialog", Now.AddDays(-3), 10, 10);
        CaptureRecord fresh = Add(lib, "Error toast", Now.AddHours(-1), 50, 50);
        lib.SetFavorite(mid.Id, true);
        lib.SetTags(old.Id, ["auth"]);
        lib.SetFolder(fresh.Id, "Bugs");

        Assert.Equal([fresh.Id, mid.Id, old.Id], lib.Query(new LibraryQuery(), Now).Select(r => r.Id));
        Assert.Equal([old.Id, mid.Id, fresh.Id], lib.Query(new LibraryQuery { Sort = LibrarySort.OldestFirst }, Now).Select(r => r.Id));
        Assert.Equal([fresh.Id, old.Id, mid.Id], lib.Query(new LibraryQuery { Sort = LibrarySort.TitleAscending }, Now).Select(r => r.Id));
        Assert.Equal([old.Id, fresh.Id, mid.Id], lib.Query(new LibraryQuery { Sort = LibrarySort.LargestFirst }, Now).Select(r => r.Id));
        Assert.Equal([fresh.Id, mid.Id], lib.Query(new LibraryQuery { View = LibraryView.Recent }, Now).Select(r => r.Id));
        Assert.Equal([mid.Id], lib.Query(new LibraryQuery { View = LibraryView.Favorites }, Now).Select(r => r.Id));
        Assert.Equal([old.Id], lib.Query(new LibraryQuery { View = LibraryView.Tag, Key = "AUTH" }, Now).Select(r => r.Id));
        Assert.Equal([fresh.Id], lib.Query(new LibraryQuery { View = LibraryView.Folder, Key = "bugs" }, Now).Select(r => r.Id));
        Assert.Equal([mid.Id], lib.Query(new LibraryQuery { Search = "settings" }, Now).Select(r => r.Id));
        Assert.Equal([old.Id], lib.Query(new LibraryQuery { Search = "auth login" }, Now).Select(r => r.Id));
        Assert.Equal([fresh.Id], lib.Query(new LibraryQuery { Search = "bugs" }, Now).Select(r => r.Id));
        Assert.Empty(lib.Query(new LibraryQuery { Search = "nothing-matches" }, Now));
        Assert.Equal(["auth"], lib.AllTags());
        Assert.Equal(["Bugs"], lib.AllFolders());
    }

    [Fact]
    public void Date_grouping_labels()
    {
        var now = new DateTime(2026, 11, 25, 15, 0, 0); // Wednesday
        Assert.Equal("Today", DateGrouping.GroupFor(now.AddHours(-3), now));
        Assert.Equal("Yesterday", DateGrouping.GroupFor(now.AddDays(-1), now));
        Assert.Equal("October 2026", DateGrouping.GroupFor(new DateTime(2026, 10, 2), now));
        Assert.Equal("November 2025", DateGrouping.GroupFor(new DateTime(2025, 11, 30), now));
    }

    [Fact]
    public void Usage_and_retention()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord old = Add(lib, "old", Now.AddDays(-400));
        CaptureRecord oldFav = Add(lib, "old fav", Now.AddDays(-400));
        CaptureRecord trashed = Add(lib, "trashed");
        lib.SetFavorite(oldFav.Id, true);
        lib.MoveToTrash([trashed.Id], Now.AddDays(-40));

        StorageUsage u = lib.GetUsage();
        Assert.Equal((2, 1), (u.CaptureCount, u.TrashCount));
        Assert.True(u.TotalBytes > u.TrashBytes && u.TrashBytes > 0);

        (int t, int d) = lib.ApplyRetention(new RetentionPolicy { EmptyTrashAfterDays = 30, MoveToTrashAfterDays = 365 }, Now);
        Assert.Equal((1, 1), (t, d));
        Assert.True(lib.Find(old.Id)!.IsDeleted);
        Assert.False(lib.Find(oldFav.Id)!.IsDeleted);
        Assert.Null(lib.Find(trashed.Id));

        // Default policy never trashes active captures.
        Assert.Equal(0, lib.ApplyRetention(new RetentionPolicy(), Now.AddYears(5)).Trashed);
    }

    [Fact]
    public void Backup_and_restore_round_trip_without_overwriting()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Sub("lib"));
        CaptureRecord a = Add(lib, "a"), b = Add(lib, "b");
        string zip = dir.Sub("backup.zip");
        Assert.Equal(2, lib.Backup(zip));

        var fresh = CaptureLibrary.Open(dir.Sub("other"));
        Assert.Equal(2, fresh.RestoreBackup(zip));
        Assert.Equal(lib.Load(a.Id).BaseImage.Pixels, fresh.Load(a.Id).BaseImage.Pixels);
        Assert.Equal("b", fresh.Find(b.Id)!.Title);
        Assert.Equal(0, fresh.RestoreBackup(zip)); // existing ids are skipped, never overwritten
    }

    [Fact]
    public void Restore_rejects_path_traversal_and_foreign_zips()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Sub("lib"));
        string evil = dir.Sub("evil.zip");
        using (var z = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            z.CreateEntry("snapforge-backup.json");
            z.CreateEntry($"captures/{Guid.NewGuid():D}/../../../evil.txt");
        }

        Assert.Throws<InvalidDataException>(() => lib.RestoreBackup(evil));
        string foreign = dir.Sub("foreign.zip");
        using (var z = ZipFile.Open(foreign, ZipArchiveMode.Create))
        {
            z.CreateEntry("readme.txt");
        }

        Assert.Throws<InvalidDataException>(() => lib.RestoreBackup(foreign));
        Assert.False(File.Exists(dir.Sub("evil.txt")));
    }

    [Fact]
    public void Package_export_import_gets_new_id_and_rejects_unknown_entries()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Sub("lib"));
        CaptureRecord a = Add(lib, "a", annotations: [new LineAnnotation(new PointD(0, 0), new PointD(2, 2))]);
        string pkg = dir.Sub("a" + CaptureFormat.PackageExtension);
        lib.ExportPackage(a.Id, pkg);
        CaptureRecord imported = lib.ImportPackage(pkg);
        Assert.NotEqual(a.Id, imported.Id);
        Assert.Equal(lib.Load(a.Id).Annotations, lib.Load(imported.Id).Annotations);

        string bad = dir.Sub("bad.sfcap");
        using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
        {
            z.CreateEntry("../x.json");
        }

        Assert.Throws<InvalidDataException>(() => lib.ImportPackage(bad));
    }

    [Fact]
    public void Move_library_to_new_location_and_reject_non_empty_target()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Sub("old"));
        CaptureRecord a = Add(lib, "a");
        CaptureLibrary moved = lib.MoveTo(dir.Sub("new"));
        Assert.Equal(a.Id, Assert.Single(moved.Records).Id);
        moved.Load(a.Id);
        Assert.Empty(Directory.GetDirectories(Path.Combine(dir.Sub("old"), "captures")));

        var other = CaptureLibrary.Open(dir.Sub("other"));
        Add(other, "x");
        Assert.Throws<IOException>(() => moved.MoveTo(dir.Sub("other")));
        Assert.Throws<ArgumentException>(() => moved.MoveTo(Path.Combine(dir.Sub("new"), "inside")));
    }

    [Fact]
    public void Redacted_secret_colour_appears_in_no_thumbnail_or_flattened_output()
    {
        // Adversarial: a block of a distinctive "secret" colour is redacted; neither the stored thumbnail nor the
        // flattened image may contain that colour (not even blended at the edges).
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        var img = new PixelBuffer(400, 300);
        img.FillRect(img.Bounds, ArgbColor.White);
        var secret = ArgbColor.FromRgb(0x12, 0xFE, 0x34);
        img.FillRect(new PixelRect(100, 80, 120, 60), secret);
        var doc = new EditorDocument(img);
        doc.Execute(new RedactCommand(new PixelRect(96, 76, 128, 68), ArgbColor.Black));
        CaptureRecord rec = lib.Create(new CaptureContent(doc.BaseImage, doc.Annotations), doc.Image, Now);
        static bool Greenish(ArgbColor c) => c.G > 150 && c.R < 120 && c.B < 120;
        PixelBuffer thumb = lib.LoadThumbnail(rec.Id)!;
        for (int y = 0; y < thumb.Height; y++)
        {
            for (int x = 0; x < thumb.Width; x++)
            {
                Assert.False(Greenish(thumb.GetPixel(x, y)), $"secret colour in thumbnail at {x},{y}");
            }
        }

        for (int y = 0; y < doc.Image.Height; y++)
        {
            for (int x = 0; x < doc.Image.Width; x++)
            {
                Assert.False(Greenish(doc.Image.GetPixel(x, y)), $"secret colour in flattened image at {x},{y}");
            }
        }
    }

    [Fact]
    public void Sanitize_removes_original_pixels_from_storage_and_hidden_annotations()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        var doc = new EditorDocument(PixelBufferTests.Gradient(60, 40));
        var secretText = new TextAnnotation(new PointD(12, 12), "password123", 12) { MeasuredSize = new PointD(30, 10) };
        var visibleArrow = new ArrowAnnotation(new PointD(40, 30), new PointD(55, 35));
        doc.Execute(new AddAnnotationCommand(secretText));
        doc.Execute(new RedactCommand(new PixelRect(5, 5, 40, 20), ArgbColor.Black));
        doc.Execute(new AddAnnotationCommand(visibleArrow));
        CaptureRecord rec = lib.Create(new CaptureContent(doc.BaseImage, doc.Annotations), doc.Image, Now);
        Assert.True(rec.HasReversibleRedactions);

        int rasterizerCalls = 0;
        int flattened = doc.Sanitize((img, list) =>
        {
            rasterizerCalls++;
            Assert.Equal([secretText], list);
            return img.Clone();
        });
        Assert.Equal(1, flattened);
        Assert.Equal(1, rasterizerCalls);
        Assert.False(doc.HasReversibleRedactions);
        Assert.False(doc.History.CanUndo);
        Assert.DoesNotContain(doc.Annotations, a => a is TextAnnotation);
        Assert.Contains(doc.Annotations, a => a.Id == visibleArrow.Id);

        lib.Sanitize(rec.Id, doc.BaseImage, doc.Annotations, doc.Image);
        var reopened = CaptureLibrary.Open(dir.Path);
        CaptureRecord r = reopened.Find(rec.Id)!;
        Assert.False(r.HasReversibleRedactions);
        Assert.NotNull(r.SanitizedUtc);
        CaptureContent c = reopened.Load(rec.Id);
        for (int y = 5; y < 25; y++)
        {
            for (int x = 5; x < 45; x++)
            {
                Assert.Equal(ArgbColor.Black, c.BaseImage.GetPixel(x, y));
            }
        }

        string annotationsJson = File.ReadAllText(Path.Combine(reopened.CapturesDirectory, rec.Id.ToString("D"), CaptureFormat.AnnotationsFile));
        Assert.DoesNotContain("password123", annotationsJson, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(reopened.CapturesDirectory, rec.Id.ToString("D")), "*" + AtomicFile.TempSuffix));
    }

    [Fact]
    public void Adding_a_redaction_after_sanitizing_marks_capture_unsanitized_again()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        var doc = new EditorDocument(PixelBufferTests.Gradient(30, 30));
        doc.Execute(new RedactCommand(new PixelRect(0, 0, 5, 5), ArgbColor.Black));
        CaptureRecord rec = lib.Create(new CaptureContent(doc.BaseImage, doc.Annotations), doc.Image, Now);
        doc.Sanitize();
        lib.Sanitize(rec.Id, doc.BaseImage, doc.Annotations, doc.Image);
        doc.Execute(new RedactCommand(new PixelRect(10, 10, 5, 5), ArgbColor.Black));
        CaptureRecord after = lib.SaveContent(rec.Id, null, doc.Annotations, doc.Image, doc.HasReversibleRedactions);
        Assert.True(after.HasReversibleRedactions);
        Assert.Null(after.SanitizedUtc);
    }

    [Fact]
    public void Interrupted_sanitize_is_completed_on_next_open()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord rec = Add(lib, "a");
        string folder = Path.Combine(lib.CapturesDirectory, rec.Id.ToString("D"));
        // State after "new base written" + "old base deleted" but before the rename:
        byte[] sanitized = File.ReadAllBytes(Path.Combine(folder, CaptureFormat.BaseFile));
        File.Delete(Path.Combine(folder, CaptureFormat.BaseFile));
        File.WriteAllBytes(Path.Combine(folder, "base.sanitized.sfpx"), sanitized);
        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.Null(reopened.Find(rec.Id)!.Damage);
        reopened.Load(rec.Id);
    }

    [Fact]
    public void Large_library_indexes_and_queries_quickly()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        PixelBuffer tiny = PixelBufferTests.Gradient(8, 8);
        AtomicFile.SkipDiskFlush.Value = true; // seeding only; durability is tested elsewhere
        try
        {
            for (int i = 0; i < 1500; i++)
            {
                lib.Create(new CaptureContent(tiny, []), tiny, Now.AddMinutes(-i), $"Capture {i:D4}");
            }
        }
        finally
        {
            AtomicFile.SkipDiskFlush.Value = false;
        }

        var sw = Stopwatch.StartNew();
        var reopened = CaptureLibrary.Open(dir.Path);
        long openMs = sw.ElapsedMilliseconds;
        Assert.Equal(1500, reopened.Records.Count);
        sw.Restart();
        for (int i = 0; i < 20; i++)
        {
            Assert.Single(reopened.Query(new LibraryQuery { Search = "1234" }, Now));
        }

        long queryMs = sw.ElapsedMilliseconds / 20;
        Assert.True(openMs < 15000, $"Indexing 1500 captures took {openMs} ms");
        Assert.True(queryMs < 200, $"Search took {queryMs} ms");
    }
}
