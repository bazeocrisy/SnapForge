using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>
/// RC3: two processes (SnapForge Capture and SnapForge Editor) share one Library folder (docs/LIBRARY-COORDINATION.md).
/// Two <see cref="CaptureLibrary"/> instances opened on the same root stand in for the two processes: each has its own
/// in-process monitor and index, and they share only the disk and the named write-gate mutex.
/// </summary>
public class LibraryCoordinationTests
{
    private static readonly DateTime Now = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private static CaptureRecord AddImage(CaptureLibrary lib, string title = "Shot")
    {
        PixelBuffer img = PixelBufferTests.Gradient(40, 30);
        return lib.Create(new CaptureContent(img, []), img, Now, title);
    }

    /// <summary>Starts <paramref name="lib"/>.Create on its own thread and pauses it inside the write gate, just before the commit rename.</summary>
    private static PausedCreate StartPausedCreate(CaptureLibrary lib)
    {
        var paused = new PausedCreate();
        paused.Thread = new Thread(() =>
        {
            CaptureLibrary.CreateCommitHook.Value = staging =>
            {
                paused.Staging = staging;
                paused.Entered.Set();
                paused.Release.Wait();
            };
            try
            {
                paused.Result = AddImage(lib, "made by A");
            }
            catch (Exception ex)
            {
                paused.Error = ex;
                paused.Entered.Set();
            }
            finally
            {
                CaptureLibrary.CreateCommitHook.Value = null;
            }
        });
        paused.Thread.Start();
        Assert.True(paused.Entered.Wait(WaitLimit), "Create did not reach its commit point");
        Assert.Null(paused.Error);
        return paused;
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        DateTime limit = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, $"Timed out waiting until {what}");
            Thread.Sleep(25);
        }
    }

    [Fact]
    public void RC3_LIB_open_by_another_program_during_a_create_commit_keeps_the_staging_folder_and_the_capture_completes()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        CaptureRecord existing = AddImage(a, "existing");
        PausedCreate create = StartPausedCreate(a);
        string staging = create.Staging!;
        Assert.True(Directory.Exists(staging));
        Assert.StartsWith(".creating-", Path.GetFileName(staging), StringComparison.Ordinal);

        // B opens (start-up clean-up and index) while A is inside its commit: B must wait at the gate.
        CaptureLibrary? b = null;
        Exception? bError = null;
        var opener = new Thread(() =>
        {
            try
            {
                b = CaptureLibrary.Open(dir.Path);
            }
            catch (Exception ex)
            {
                bError = ex;
            }
        });
        opener.Start();
        Assert.False(opener.Join(TimeSpan.FromMilliseconds(500)), "B's open did not wait for A's commit");
        Assert.True(Directory.Exists(staging), "B deleted A's staging folder");
        Assert.NotEmpty(Directory.GetFiles(staging));

        create.Release.Set();
        Assert.True(create.Thread!.Join(WaitLimit));
        Assert.True(opener.Join(WaitLimit));
        Assert.Null(create.Error);
        Assert.Null(bError);
        CaptureRecord made = create.Result!;

        // A's capture is complete: listed by A, loadable, and seen by B and by a fresh open.
        Assert.Null(a.Find(made.Id)!.Damage);
        Assert.Equal(40, a.Load(made.Id).BaseImage.Width);
        Assert.False(Directory.Exists(staging));
        Assert.NotNull(b!.Find(existing.Id));
        Assert.Null(b.RefreshFromDisk(made.Id)!.Damage);
        Assert.Equal(40, b.Load(made.Id).BaseImage.Width);
        var c = CaptureLibrary.Open(dir.Path);
        Assert.Equal(2, c.Records.Count);
        Assert.All(c.Records, r => Assert.Null(r.Damage));
    }

    [Fact]
    public void RC3_LIB_reindex_by_another_program_during_a_create_commit_waits_and_deletes_nothing()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        var b = CaptureLibrary.Open(dir.Path);
        PausedCreate create = StartPausedCreate(a);
        var reindex = new Thread(b.Reindex);
        reindex.Start();
        Assert.False(reindex.Join(TimeSpan.FromMilliseconds(400)), "B's reindex did not wait for A's commit");
        Assert.True(Directory.Exists(create.Staging));

        create.Release.Set();
        Assert.True(create.Thread!.Join(WaitLimit));
        Assert.True(reindex.Join(WaitLimit));
        Assert.Null(create.Error);
        Assert.Null(CaptureLibrary.Open(dir.Path).Find(create.Result!.Id)!.Damage);

        // Another reindex now certainly sees it.
        b.Reindex();
        Assert.NotNull(b.Find(create.Result.Id));
    }

    [Fact]
    public void RC3_LIB_temporary_files_younger_than_the_safe_age_or_in_use_are_not_deleted_by_another_programs_open()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        CaptureRecord rec = AddImage(a);
        string folder = Path.Combine(a.CapturesDirectory, rec.Id.ToString("D"));
        (Guid recording, string recordingPath) = a.BeginVideo(Now);
        File.WriteAllBytes(recordingPath, new byte[1000]);

        // Outside the item folders (e.g. a backup A is writing into the library folder): kept while young.
        string youngAtRoot = Path.Combine(dir.Path, $".backup.zip.{Guid.NewGuid():N}{AtomicFile.TempSuffix}");
        File.WriteAllText(youngAtRoot, "being written by A");

        // The same, but older than the safe age and closed: a leftover, removed.
        string oldAtRoot = Path.Combine(dir.Path, $".old.zip.{Guid.NewGuid():N}{AtomicFile.TempSuffix}");
        File.WriteAllText(oldAtRoot, "left by a crash long ago");
        File.SetLastWriteTimeUtc(oldAtRoot, DateTime.UtcNow - CaptureLibrary.TemporaryFileSafeAge - TimeSpan.FromMinutes(5));

        // In the folder of a recording A is making: kept while young.
        string youngInRecording = Path.Combine(Path.GetDirectoryName(recordingPath)!, $".x.{Guid.NewGuid():N}{AtomicFile.TempSuffix}");
        File.WriteAllText(youngInRecording, "A's");

        // In a capture folder, still open by a writer (a write in progress): kept whatever its age.
        string openInCapture = Path.Combine(folder, $".meta.json.{Guid.NewGuid():N}{AtomicFile.TempSuffix}");
        string openOld = Path.Combine(dir.Path, $".open-old.{Guid.NewGuid():N}{AtomicFile.TempSuffix}");
        File.WriteAllText(openOld, "x");
        File.SetLastWriteTimeUtc(openOld, DateTime.UtcNow - TimeSpan.FromHours(2));

        // In a capture folder, closed: every write there happens inside the gate, so it is a leftover (as before).
        string closedInCapture = Path.Combine(folder, $".annotations.9.json.{Guid.NewGuid():N}{AtomicFile.TempSuffix}");
        File.WriteAllText(closedInCapture, "left by a crash");

        using (var writer = new FileStream(openInCapture, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var holder = new FileStream(openOld, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            writer.Write([1, 2, 3]);
            var b = CaptureLibrary.Open(dir.Path);
            Assert.True(b.IsIndexed);
            Assert.True(File.Exists(openInCapture), "B deleted a temporary file that is being written");
            Assert.True(File.Exists(openOld), "B deleted an open temporary file");
        }

        Assert.True(File.Exists(youngAtRoot), "B deleted a young temporary file outside the item folders");
        Assert.True(File.Exists(youngInRecording), "B deleted a young temporary file of a recording in progress");
        Assert.False(File.Exists(oldAtRoot));
        Assert.False(File.Exists(closedInCapture));

        // A's recording and capture are untouched and complete normally.
        Assert.True(File.Exists(recordingPath));
        Assert.Equal(1000, new FileInfo(recordingPath).Length);
        Assert.Null(CaptureLibrary.Open(dir.Path).Find(rec.Id)!.Damage);
        File.Delete(youngInRecording);
        Assert.Equal(recording, a.CompleteVideo(recording, new VideoInfo(64, 36, 30, 1000, false), null, Now).Id);
    }

    [Fact]
    public void RC3_LIB_a_recording_in_progress_in_another_program_is_never_touched_by_open_or_reindex()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        (Guid id, string path) = a.BeginVideo(Now);
        File.WriteAllBytes(path, new byte[5000]);
        string folder = Path.GetDirectoryName(path)!;
        string[] before = [.. Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

        var b = CaptureLibrary.Open(dir.Path);
        b.Reindex();
        Assert.Null(b.Find(id));
        Assert.Null(b.RefreshFromDisk(id));
        Assert.Equal(before, Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(folder, CaptureLibrary.InProgressMarker)));

        CaptureRecord v = a.CompleteVideo(id, new VideoInfo(64, 36, 30, 1000, false), PixelBufferTests.Gradient(64, 36), Now, "clip");
        Assert.Null(v.Damage);
        Assert.Equal("clip", b.RefreshFromDisk(id)!.Title);
    }

    [Fact]
    public void RC3_LIB_RefreshFromDisk_picks_up_another_programs_new_capture_rename_edit_and_deletion()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        var b = CaptureLibrary.Open(dir.Path);
        var changed = new List<Guid>();
        b.Changed += (_, e) =>
        {
            lock (changed)
            {
                changed.AddRange(e.Ids);
            }
        };

        CaptureRecord made = AddImage(a, "new in A");
        Assert.Null(b.Find(made.Id));
        Assert.Equal("new in A", b.RefreshFromDisk(made.Id)!.Title);
        Assert.Equal("new in A", b.Find(made.Id)!.Title);
        Assert.Contains(made.Id, changed);

        a.Rename(made.Id, "renamed in A");
        a.SetTags(made.Id, ["x"]);
        Assert.Equal("new in A", b.Find(made.Id)!.Title); // B's index is its own until refreshed
        CaptureRecord renamed = b.RefreshFromDisk(made.Id)!;
        Assert.Equal("renamed in A", renamed.Title);
        Assert.Equal(["x"], renamed.Meta.Tags);

        PixelBuffer img = a.Load(made.Id).BaseImage;
        a.SaveContent(made.Id, null, [new LineAnnotation(new PointD(0, 0), new PointD(5, 5))], img, false);
        Assert.Equal(1, b.RefreshFromDisk(made.Id)!.AnnotationCount);
        Assert.Single(b.Load(made.Id).Annotations);

        // B edits too; A sees it.
        b.SetFavorite(made.Id, true);
        Assert.True(a.RefreshFromDisk(made.Id)!.IsFavorite);

        a.DeletePermanently([made.Id]);
        changed.Clear();
        Assert.Null(b.RefreshFromDisk(made.Id));
        Assert.Null(b.Find(made.Id));
        Assert.Contains(made.Id, changed);
        Assert.Null(b.RefreshFromDisk(Guid.NewGuid())); // unknown: nothing, no error
    }

    [Fact]
    public void RC3_LIB_LibraryWatcher_picks_up_another_programs_new_capture_rename_edit_video_and_deletion()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        var b = CaptureLibrary.Open(dir.Path);
        using var watcher = new LibraryWatcher(b);

        CaptureRecord made = AddImage(a, "new in A");
        WaitUntil(() => b.Find(made.Id) is { Damage: null }, "B lists A's new capture");

        a.Rename(made.Id, "renamed in A");
        WaitUntil(() => b.Find(made.Id)?.Title == "renamed in A", "B sees A's rename");

        a.MoveToTrash([made.Id], Now);
        WaitUntil(() => b.Find(made.Id)?.IsDeleted == true, "B sees A's move to the Trash");

        PixelBuffer img = a.Load(made.Id).BaseImage;
        a.SaveContent(made.Id, null, [new LineAnnotation(new PointD(0, 0), new PointD(5, 5))], img, false);
        WaitUntil(() => b.Find(made.Id)?.AnnotationCount == 1, "B sees A's edit");

        (Guid video, string path) = a.BeginVideo(Now);
        File.WriteAllBytes(path, new byte[2048]);
        a.CompleteVideo(video, new VideoInfo(64, 36, 30, 1000, false), null, Now, "clip");
        WaitUntil(() => b.Find(video) is { IsVideo: true, Title: "clip" }, "B lists A's new video");

        a.DeletePermanently([made.Id, video]);
        WaitUntil(() => b.Find(made.Id) is null && b.Find(video) is null, "B drops A's deleted items");
    }

    [Fact]
    public void RC3_LIB_LibraryWatcher_falls_back_to_a_full_reindex_when_events_are_lost()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        var b = CaptureLibrary.Open(dir.Path);
        CaptureRecord before = AddImage(a, "made before the watcher started"); // no event will ever arrive for it
        using var watcher = new LibraryWatcher(b, TimeSpan.FromMilliseconds(50));
        Assert.Null(b.Find(before.Id));
        watcher.Overflow();
        WaitUntil(() => b.Find(before.Id) is not null, "the fallback reindex lists the item");
    }

    [Fact]
    public void RC3_LIB_LibraryWatcher_maps_only_item_folders_and_their_manifest_and_metadata_files()
    {
        string captures = Path.Combine(Path.GetTempPath(), "lib", "captures");
        Guid id = Guid.NewGuid();
        string item = Path.Combine(captures, id.ToString("D"));
        Assert.Equal(id, LibraryWatcher.ItemIdOf(captures, item));
        Assert.Equal(id, LibraryWatcher.ItemIdOf(captures, Path.Combine(item, CaptureFormat.ManifestFile)));
        Assert.Equal(id, LibraryWatcher.ItemIdOf(captures, Path.Combine(item, CaptureFormat.MetaFile)));
        Assert.Equal(id, LibraryWatcher.ItemIdOf(captures, Path.Combine(item, CaptureLibrary.VideoManifestFile)));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, Path.Combine(item, "base.3.sfpx")));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, Path.Combine(item, $".manifest.json.{Guid.NewGuid():N}{AtomicFile.TempSuffix}")));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, Path.Combine(captures, ".creating-" + id.ToString("N"))));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, Path.Combine(captures, ".creating-" + id.ToString("N"), CaptureFormat.ManifestFile)));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, Path.Combine(captures, "not-an-id")));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, Path.Combine(item, "sub", CaptureFormat.ManifestFile)));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, Path.Combine(Path.GetTempPath(), "elsewhere", id.ToString("D"))));
        Assert.Null(LibraryWatcher.ItemIdOf(captures, null));
    }

    [Fact]
    public void RC3_LIB_write_gate_times_out_with_a_clear_message_while_another_program_holds_it()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        var b = CaptureLibrary.Open(dir.Path);
        CaptureRecord existing = AddImage(b, "existing");
        b.WriteGateTimeout = TimeSpan.FromMilliseconds(300);
        PausedCreate create = StartPausedCreate(a);
        try
        {
            IOException ex = Assert.Throws<IOException>(() => b.Rename(existing.Id, "blocked"));
            Assert.Contains("busy in another SnapForge program", ex.Message, StringComparison.Ordinal);
            Assert.Throws<IOException>(() => AddImage(b, "blocked too"));
            Assert.Throws<IOException>(b.Reindex);
        }
        finally
        {
            create.Release.Set();
            Assert.True(create.Thread!.Join(WaitLimit));
        }

        // Once A is done, B writes normally, and nothing B attempted was half-done.
        Assert.Equal("after", b.Rename(existing.Id, "after").Title);
        var c = CaptureLibrary.Open(dir.Path);
        Assert.Equal(["after", "made by A"], c.Records.Select(r => r.Title).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetDirectories(c.CapturesDirectory, ".creating-*"));
    }

    [Fact]
    public void RC3_LIB_write_gate_is_reentrant_and_a_mutex_abandoned_by_a_crashed_holder_counts_as_acquired()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord rec = AddImage(lib);

        // Re-entrant on one thread, also through a second gate object for the same root (as relocation clean-up does).
        var gate = new LibraryWriteGate(dir.Path);
        using (gate.Enter())
        using (gate.Enter())
        using (new LibraryWriteGate(dir.Path + Path.DirectorySeparatorChar).Enter())
        {
            Assert.True(gate.IsHeldByCurrentThread);
            Assert.Equal("inside", lib.Rename(rec.Id, "inside").Title);
        }

        Assert.False(gate.IsHeldByCurrentThread);

        // A holder that "crashes" (its thread ends while owning the mutex) does not block the library forever.
        var crashed = new Thread(() => new LibraryWriteGate(dir.Path).Enter());
        crashed.Start();
        Assert.True(crashed.Join(WaitLimit));
        lib.WriteGateTimeout = TimeSpan.FromSeconds(5);
        Assert.Equal("after crash", lib.Rename(rec.Id, "after crash").Title);
    }

    [Fact]
    public void RC3_LIB_write_gate_mutex_name_is_derived_from_the_normalised_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "SnapForge", "Library");
        string name = LibraryWriteGate.MutexNameFor(root);
        Assert.Matches(@"^Local\\SnapForge-Library-[0-9A-F]{32}$", name);
        Assert.Equal(name, LibraryWriteGate.MutexNameFor(root + Path.DirectorySeparatorChar));
        Assert.Equal(name, LibraryWriteGate.MutexNameFor(Path.Combine(root, "..", "Library")));
        Assert.Equal(name, LibraryWriteGate.MutexNameFor(root.ToLowerInvariant()));
        Assert.NotEqual(name, LibraryWriteGate.MutexNameFor(root + "2"));
    }

    [Fact]
    public void RC3_LIB_concurrent_writers_in_two_programs_lose_no_metadata_update_and_leave_no_debris()
    {
        using var dir = new TestDirectory();
        var a = CaptureLibrary.Open(dir.Path);
        var b = CaptureLibrary.Open(dir.Path);
        CaptureRecord rec = AddImage(a);
        var made = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        void Work(CaptureLibrary lib, string who)
        {
            for (int i = 0; i < 10; i++)
            {
                made.Add(AddImage(lib, $"{who} {i}").Id);
                lib.UpdateMetadata(rec.Id, m => m with { Tags = [.. m.Tags, $"{who}{i}"] });
                lib.Reindex();
            }
        }

        var ta = new Thread(() => Work(a, "a"));
        var tb = new Thread(() => Work(b, "b"));
        ta.Start();
        tb.Start();
        Assert.True(ta.Join(TimeSpan.FromSeconds(60)));
        Assert.True(tb.Join(TimeSpan.FromSeconds(60)));

        var c = CaptureLibrary.Open(dir.Path);
        Assert.Equal(21, c.Records.Count);
        Assert.All(c.Records, r => Assert.Null(r.Damage));
        Assert.Equal(20, c.Find(rec.Id)!.Meta.Tags.Count); // no read-modify-write lost
        Assert.All(made, id => Assert.NotNull(c.Find(id)));
        Assert.Empty(Directory.GetDirectories(c.CapturesDirectory, ".creating-*"));
    }

    private sealed class PausedCreate
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public Thread? Thread { get; set; }

        public string? Staging { get; set; }

        public CaptureRecord? Result { get; set; }

        public Exception? Error { get; set; }
    }
}
