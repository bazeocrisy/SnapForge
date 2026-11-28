using System.Text.Json.Nodes;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>FA-008: videos carry a streamed SHA-256; verification detects same-length modification.</summary>
public class VideoIntegrityTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);

    internal static CaptureRecord AddVideo(CaptureLibrary lib, int bytes = 64 * 1024, string title = "Clip")
    {
        (Guid id, string path) = lib.BeginVideo(Now);
        var data = new byte[bytes];
        new Random(bytes).NextBytes(data);
        File.WriteAllBytes(path, data);
        return lib.CompleteVideo(id, new VideoInfo(64, 36, 30, 1000, HasAudio: false), PixelBufferTests.Gradient(16, 9), Now, title);
    }

    private static JsonObject Manifest(CaptureLibrary lib, Guid id) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(lib.VideoPath(id))!, CaptureLibrary.VideoManifestFile)))!;

    internal static void FlipOneByte(string file, long offset = 100)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite);
        fs.Position = offset;
        int b = fs.ReadByte();
        fs.Position = offset;
        fs.WriteByte((byte)(b ^ 0x5A));
    }

    [Fact]
    public void FA008_completed_video_records_the_streamed_sha256_of_the_mp4()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib);
        JsonObject m = Manifest(lib, v.Id);
        Assert.Equal(AtomicFile.Sha256Hex(File.ReadAllBytes(lib.VideoPath(v.Id))), m[CaptureLibrary.VideoHashKey]!.GetValue<string>());
        Assert.Equal(1, m["version"]!.GetValue<int>()); // additive field: older versions still read the video
        ItemIntegrityReport r = lib.VerifyIntegrity(v.Id);
        Assert.Equal(IntegrityStatus.Verified, r.Status);
        Assert.Equal(64 * 1024, r.Bytes);
    }

    [Fact]
    public void FA008_same_length_modification_is_detected_by_verification_and_blocks_export()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib);
        FlipOneByte(lib.VideoPath(v.Id));

        // Start-up indexing deliberately checks only the length (no hashing of every video at start).
        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.Null(reopened.Find(v.Id)!.Damage);

        ItemIntegrityReport r = reopened.VerifyIntegrity(v.Id);
        Assert.Equal(IntegrityStatus.Damaged, r.Status);
        Assert.Contains("recorded hash", r.Message, StringComparison.Ordinal);

        string target = dir.Sub("export.mp4");
        Assert.Throws<InvalidDataException>(() => reopened.ExportVideo(v.Id, target));
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(dir.Path, "*" + AtomicFile.TempSuffix));
        Assert.Equal(1, reopened.VerifyLibrary().DamagedCount);
    }

    [Fact]
    public void FA008_export_streams_a_verified_copy()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        CaptureRecord v = AddVideo(lib, bytes: 3 * 1024 * 1024 + 17);
        var progress = new List<long>();
        VideoExportResult r = lib.ExportVideo(v.Id, dir.Sub("out/clip.mp4"), new SyncProgress<long>(progress.Add));
        Assert.Equal(IntegrityStatus.Verified, r.Status);
        Assert.Equal(File.ReadAllBytes(lib.VideoPath(v.Id)), File.ReadAllBytes(dir.Sub("out/clip.mp4")));
        Assert.Equal(3 * 1024 * 1024 + 17, progress[^1]);
        Assert.True(progress.Count >= 4); // streamed in chunks
    }

    [Fact]
    public void FA008_legacy_video_without_hash_records_it_on_first_verification()
    {
        using var dir = new TestDirectory();
        var index = LegacyFixtures.CopyTo(dir.Path);
        var lib = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
        Guid id = LegacyFixtures.Id(index, "video");
        Assert.Null(Manifest(lib, id)[CaptureLibrary.VideoHashKey]);

        ItemIntegrityReport first = lib.VerifyIntegrity(id);
        Assert.Equal(IntegrityStatus.HashRecordedNow, first.Status);
        Assert.Contains("not previously verified", first.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AtomicFile.Sha256Hex(File.ReadAllBytes(lib.VideoPath(id))), Manifest(lib, id)[CaptureLibrary.VideoHashKey]!.GetValue<string>());
        Assert.NotNull(Manifest(lib, id)["videoHashRecordedUtc"]);

        Assert.Equal(IntegrityStatus.Verified, CaptureLibrary.Open(Path.Combine(dir.Path, "library")).VerifyIntegrity(id).Status);
    }

    [Fact]
    public void FA008_library_scan_reports_every_item_including_damaged_ones()
    {
        using var dir = new TestDirectory();
        var index = LegacyFixtures.CopyTo(dir.Path);
        var lib = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
        var seen = new List<LibraryProgress>();
        LibraryIntegrityReport report = lib.VerifyLibrary(new SyncProgress<LibraryProgress>(seen.Add));
        Assert.Equal(5, report.Items.Count);
        Assert.Equal(3, report.VerifiedCount);
        Assert.Equal(1, report.HashRecordedNowCount);
        Assert.Equal(1, report.DamagedCount);
        Assert.Equal(LegacyFixtures.Id(index, "damaged"), Assert.Single(report.Items, i => i.Status == IntegrityStatus.Damaged).Id);
        Assert.Equal(5, seen[^1].ItemsDone);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => lib.VerifyLibrary(null, cts.Token));
    }

    [Fact]
    public void FA008_tampered_image_is_reported_by_the_scan()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        var img = PixelBufferTests.Gradient(20, 20);
        CaptureRecord rec = lib.Create(new CaptureContent(img, []), img, Now);
        string thumb = Path.Combine(lib.CapturesDirectory, rec.Id.ToString("D"), lib.ReadCaptureManifest(rec.Id).Thumb!.Name);
        FlipOneByte(thumb, 20);
        ItemIntegrityReport r = lib.VerifyIntegrity(rec.Id);
        Assert.Equal(IntegrityStatus.Damaged, r.Status);
        Assert.Contains("thumb", r.Message, StringComparison.Ordinal);
    }
}
