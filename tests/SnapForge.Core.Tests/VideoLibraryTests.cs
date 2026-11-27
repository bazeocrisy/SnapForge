using System.Text;
using System.Text.Json.Nodes;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;
using SnapForge.Core.Video;

namespace SnapForge.Core.Tests;

/// <summary>
/// Milestone V1 Library video format and migration (docs/LIBRARY-VIDEO-FORMAT.md): videos are kept apart from images
/// so existing captures are untouched and versions without video never see, list or rewrite a video.
/// </summary>
public class VideoLibraryTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly VideoInfo Info = new(1920, 1080, 30, 65_000, HasAudio: true);

    private static CaptureRecord AddVideo(CaptureLibrary lib, string title = "Demo", int bytes = 4096)
    {
        (Guid id, string path) = lib.BeginVideo(Now);
        File.WriteAllBytes(path, new byte[bytes]);
        return lib.CompleteVideo(id, Info, PixelBufferTests.Gradient(64, 36), Now, title);
    }

    private static CaptureRecord AddImage(CaptureLibrary lib, string title = "Shot")
    {
        PixelBuffer img = PixelBufferTests.Gradient(40, 30);
        return lib.Create(new CaptureContent(img, []), img, Now, title);
    }

    [Fact]
    public void Completed_video_is_listed_with_its_facts_and_survives_reopen()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib);
        Assert.True(v.IsVideo);
        Assert.Equal((1920, 1080, 30, 65_000L, true), (v.Width, v.Height, v.FramesPerSecond, v.DurationMs, v.HasAudio));
        Assert.Null(v.Damage);
        Assert.NotNull(lib.LoadThumbnail(v.Id));

        CaptureRecord r = Assert.Single(CaptureLibrary.Open(dir.Path).Records);
        Assert.Equal(v.Id, r.Id);
        Assert.Equal(CaptureKind.Video, r.Kind);
        Assert.Equal("Demo", r.Title);
        Assert.True(File.Exists(lib.VideoPath(v.Id)));
        Assert.Empty(lib.InterruptedRecordings());
    }

    [Fact]
    public void Completing_a_fragmented_recording_stores_a_progressive_mp4_and_garbage_is_kept_as_is()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        (Guid id, string path) = lib.BeginVideo(Now);
        Assert.Equal(CaptureLibrary.RecordingFile, Path.GetFileName(path));
        File.WriteAllBytes(path, Mp4DefragmenterTests.SampleFragmentedFile());
        CaptureRecord v = lib.CompleteVideo(id, Info, null, Now);
        Assert.False(v.VideoFragmented);
        Assert.False(File.Exists(path)); // recording.mp4 replaced by video.mp4
        Mp4Structure s = Mp4Inspector.Inspect(lib.VideoPath(id));
        Assert.Equal(["ftyp", "moov", "mdat"], s.Boxes.Select(b => b.Type));
        Assert.Contains("\"layout\": \"progressive\"", File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, CaptureLibrary.VideoManifestFile)), StringComparison.Ordinal);

        // A file that is not a convertible fragmented MP4 is kept unchanged (and marked) rather than lost.
        (Guid id2, string path2) = lib.BeginVideo(Now);
        File.WriteAllBytes(path2, [1, 2, 3, 4, 5]);
        CaptureRecord v2 = lib.CompleteVideo(id2, Info, null, Now);
        Assert.True(v2.VideoFragmented);
        Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(lib.VideoPath(id2)));
    }

    [Fact]
    public void Videos_take_tags_favourites_folders_rename_trash_restore_and_search()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib);
        AddImage(lib);
        lib.Rename(v.Id, "Release walkthrough");
        lib.SetTags(v.Id, ["demo", "release"]);
        lib.SetFavorite(v.Id, true);
        lib.SetFolder(v.Id, "Product");
        CaptureRecord r = lib.Find(v.Id)!;
        Assert.Equal("Release walkthrough", r.Title);
        Assert.True(r.IsVideo && r.IsFavorite);
        Assert.Contains("Product", lib.AllFolders());

        var hits = lib.Query(new LibraryQuery { Search = "walkthrough" }, Now);
        Assert.Equal(v.Id, Assert.Single(hits).Id);
        Assert.Equal(v.Id, Assert.Single(lib.Query(new LibraryQuery { Search = "video" }, Now)).Id);
        Assert.Equal(v.Id, Assert.Single(lib.Query(new LibraryQuery { View = LibraryView.Videos }, Now)).Id);

        lib.MoveToTrash([v.Id], Now);
        Assert.True(lib.Find(v.Id)!.IsDeleted);
        lib.Restore([v.Id]);
        Assert.False(lib.Find(v.Id)!.IsDeleted);

        lib.MoveToTrash([v.Id], Now);
        Assert.Equal(1, lib.EmptyTrash());
        Assert.Null(lib.Find(v.Id));
        Assert.False(Directory.Exists(Path.GetDirectoryName(lib.VideoPath(v.Id))));
    }

    [Fact]
    public void Image_only_operations_refuse_videos_with_a_clear_message()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib);
        PixelBuffer img = PixelBufferTests.Gradient(4, 4);
        Assert.Contains("video", Assert.Throws<InvalidOperationException>(() => lib.Load(v.Id)).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => lib.SaveContent(v.Id, img, [], img, false));
        Assert.Throws<InvalidOperationException>(() => lib.Sanitize(v.Id, img, [], img));
        Assert.Throws<InvalidOperationException>(() => lib.Duplicate(v.Id, Now));
        Assert.Throws<InvalidOperationException>(() => lib.ExportPackage(v.Id, Path.Combine(dir.Path, "x.snapforge")));
        Assert.True(lib.Find(v.Id)!.IsVideo); // nothing changed
    }

    [Fact]
    public void Existing_image_library_opens_unchanged_and_videos_never_enter_the_captures_folder()
    {
        using var dir = new TestDirectory();
        // A library written before video existed: only captures/.
        var before = CaptureLibrary.Open(dir.Path);
        CaptureRecord img = AddImage(before);
        string captureDir = Path.Combine(before.CapturesDirectory, img.Id.ToString("D"));
        var hashes = Directory.EnumerateFiles(captureDir).ToDictionary(f => Path.GetFileName(f)!, f => AtomicFile.Sha256Hex(File.ReadAllBytes(f)));

        var lib = CaptureLibrary.Open(dir.Path);
        AddVideo(lib);
        AddVideo(lib, "Second");

        // captures/ holds exactly what it held before: an older version sees the same library it wrote.
        Assert.Equal([img.Id.ToString("D")], Directory.EnumerateDirectories(lib.CapturesDirectory).Select(Path.GetFileName));
        Assert.Equal(hashes, Directory.EnumerateFiles(captureDir).ToDictionary(f => Path.GetFileName(f)!, f => AtomicFile.Sha256Hex(File.ReadAllBytes(f))));
        Assert.Equal(2, Directory.EnumerateDirectories(lib.VideosDirectory).Count());
        Assert.Equal(3, CaptureLibrary.Open(dir.Path).Records.Count);
        Assert.Equal(img.Width, lib.Load(img.Id).BaseImage.Width);
    }

    [Fact]
    public void FA005_backup_includes_videos_and_still_reads_the_legacy_image_only_format()
    {
        // Was: "Older_versions_backup_format_is_unchanged_and_ignores_videos". FA-005 requires videos in backups, so
        // backups are now format 2; version 1 backups (images only) are still restored (see BackupRestoreTests).
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        AddImage(lib);
        CaptureRecord v = AddVideo(lib);
        string zip = dir.Sub("backup.zip");
        Assert.Equal(2, lib.Backup(zip));
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == $"videos/{v.Id:D}/{CaptureLibrary.VideoFile}");
        Assert.Equal(10, archive.Entries.Count); // manifest + the image's five files + the video's four files
    }

    [Fact]
    public void Video_saved_by_a_newer_format_is_listed_as_unreadable_not_misread()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib);
        string manifest = Path.Combine(Path.GetDirectoryName(lib.VideoPath(v.Id))!, CaptureLibrary.VideoManifestFile);
        var m = (JsonObject)JsonNode.Parse(File.ReadAllText(manifest))!;
        m["version"] = 99;
        File.WriteAllText(manifest, m.ToJsonString());
        CaptureRecord r = Assert.Single(CaptureLibrary.Open(dir.Path).Records);
        Assert.Contains("newer version", r.Damage, StringComparison.Ordinal);
        Assert.True(r.IsVideo);
    }

    [Fact]
    public void Modified_or_missing_video_file_is_reported_as_damaged()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord a = AddVideo(lib, "A");
        CaptureRecord b = AddVideo(lib, "B");
        File.AppendAllText(lib.VideoPath(a.Id), "tampered");
        File.Delete(lib.VideoPath(b.Id));
        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.Contains("size changed", reopened.Find(a.Id)!.Damage, StringComparison.Ordinal);
        Assert.Contains("missing", reopened.Find(b.Id)!.Damage, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_in_progress_is_not_listed_and_is_reported_after_a_crash()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        (Guid id, string path) = lib.BeginVideo(Now);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("partial"));
        Assert.Empty(lib.Records);

        // "Crash": a new process opens the library.
        var after = CaptureLibrary.Open(dir.Path);
        Assert.Empty(after.Records);
        InterruptedRecording i = Assert.Single(after.InterruptedRecordings());
        Assert.Equal(id, i.Id);
        Assert.Equal(Now, i.StartedUtc);
        Assert.Equal(7, i.Bytes);

        // Recovered: completed with the recovered flag.
        CaptureRecord r = after.CompleteVideo(id, Info with { Recovered = true }, null, i.StartedUtc);
        Assert.True(r.Recovered);
        Assert.Empty(after.InterruptedRecordings());
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, CaptureLibrary.InProgressMarker)));
    }

    [Fact]
    public void Stop_after_conversion_but_before_the_manifest_reports_the_finished_file_not_a_missing_one()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        (Guid id, string recording) = lib.BeginVideo(Now);
        string folder = Path.GetDirectoryName(recording)!;
        File.WriteAllBytes(Path.Combine(folder, CaptureLibrary.VideoFile), [9, 9, 9]); // converted, recording.mp4 already deleted
        InterruptedRecording i = Assert.Single(CaptureLibrary.Open(dir.Path).InterruptedRecordings());
        Assert.Equal(id, i.Id);
        Assert.Equal(CaptureLibrary.VideoFile, Path.GetFileName(i.VideoPath));
        Assert.Equal(3, i.Bytes);
    }

    [Fact]
    public void Cancelled_recording_leaves_nothing_and_completed_videos_cannot_be_abandoned()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        (Guid id, string path) = lib.BeginVideo(Now);
        File.WriteAllBytes(path, new byte[10]);
        lib.AbandonVideo(id);
        Assert.Empty(Directory.EnumerateDirectories(lib.VideosDirectory));

        CaptureRecord v = AddVideo(lib);
        lib.AbandonVideo(v.Id); // ignored: only unfinished recordings are discarded this way
        Assert.NotNull(CaptureLibrary.Open(dir.Path).Find(v.Id));
    }

    [Fact]
    public void Stop_between_manifest_and_marker_removal_counts_as_completed()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib);
        string marker = Path.Combine(Path.GetDirectoryName(lib.VideoPath(v.Id))!, CaptureLibrary.InProgressMarker);
        File.WriteAllText(marker, "{}");
        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.Null(reopened.Find(v.Id)!.Damage);
        Assert.Empty(reopened.InterruptedRecordings());
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void FA006_moving_the_library_copies_videos_and_in_progress_recordings_and_keeps_the_source()
    {
        // Was: "..._leaves_in_progress_recordings" and the old copy was deleted at once. FA-006/FA-007: interrupted
        // recordings are copied too, and MoveTo never deletes the source (DeleteSourceAfterVerifiedMove does, later).
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Sub("old"));
        CaptureRecord img = AddImage(lib);
        CaptureRecord v = AddVideo(lib, bytes: 3 * 1024 * 1024);
        (Guid recording, string recordingPath) = lib.BeginVideo(Now);
        File.WriteAllBytes(recordingPath, [1, 2, 3]);

        CaptureLibrary moved = lib.MoveTo(dir.Sub("new"));
        Assert.Equal(2, moved.Records.Count);
        Assert.True(moved.Find(v.Id)!.IsVideo);
        Assert.Null(moved.Find(v.Id)!.Damage);
        Assert.Equal(3 * 1024 * 1024, new FileInfo(moved.VideoPath(v.Id)).Length);
        Assert.NotNull(moved.Find(img.Id));
        Assert.Equal(recording, Assert.Single(moved.InterruptedRecordings()).Id);
        Assert.Equal(2, lib.Records.Count); // source untouched
        Assert.Equal(2, CaptureLibrary.Open(dir.Sub("old")).Records.Count);
        Assert.Throws<IOException>(() => CaptureLibrary.Open(dir.Sub("other")).MoveTo(dir.Sub("new")));
    }
}
