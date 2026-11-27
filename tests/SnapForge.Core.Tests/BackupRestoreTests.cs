using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>FA-005, FA-017, FA-023: versioned, complete, verified, streaming, crash-safe backup and restore.</summary>
public class BackupRestoreTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 11, 0, 0, DateTimeKind.Utc);

    /// <summary>A library with every kind of item: organised image, trashed image, video, damaged image, interrupted recording, unknown file.</summary>
    internal sealed class RichLibrary
    {
        public required CaptureLibrary Library { get; init; }
        public Guid Organised { get; init; }
        public Guid Trashed { get; init; }
        public Guid Video { get; init; }
        public Guid TrashedVideo { get; init; }
        public Guid Damaged { get; init; }
        public Guid Recording { get; init; }

        public static RichLibrary Create(string root)
        {
            var lib = CaptureLibrary.Open(root);
            PixelBuffer img = PixelBufferTests.Gradient(48, 32);
            CaptureRecord organised = lib.Create(new CaptureContent(img, [RedactionMaskAnnotation()]), img, Now, "Organised");
            lib.SetTags(organised.Id, ["bug", "ui"]);
            lib.SetFolder(organised.Id, "Project X");
            lib.SetFavorite(organised.Id, true);
            CaptureRecord trashed = lib.Create(new CaptureContent(img, []), img, Now, "Trashed");
            lib.MoveToTrash([trashed.Id], Now);
            CaptureRecord video = VideoIntegrityTests.AddVideo(lib, 200_000, "Walkthrough");
            lib.SetTags(video.Id, ["demo"]);
            CaptureRecord trashedVideo = VideoIntegrityTests.AddVideo(lib, 5_000, "Old clip");
            lib.MoveToTrash([trashedVideo.Id], Now);
            CaptureRecord damaged = lib.Create(new CaptureContent(img, []), img, Now, "Damaged");
            File.WriteAllText(Path.Combine(lib.CapturesDirectory, damaged.Id.ToString("D"), CaptureFormat.ManifestFile), "{ truncated");
            (Guid recording, string path) = lib.BeginVideo(Now);
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("partial recording data"));
            File.WriteAllText(Path.Combine(root, "tray.json"), "{}");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "user file");
            return new RichLibrary
            {
                Library = CaptureLibrary.Open(root),
                Organised = organised.Id,
                Trashed = trashed.Id,
                Video = video.Id,
                TrashedVideo = trashedVideo.Id,
                Damaged = damaged.Id,
                Recording = recording,
            };
        }

        private static Core.Annotations.RedactionMaskAnnotation RedactionMaskAnnotation() =>
            Core.Annotations.RedactionMaskAnnotation.For(new PixelRect(2, 2, 6, 6), ArgbColor.Black);
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(root, f), f => AtomicFile.Sha256Hex(File.ReadAllBytes(f)))
            : [];

    private static void AssertNothingRestored(CaptureLibrary lib)
    {
        Assert.Empty(lib.Records);
        Assert.Empty(Directory.GetDirectories(lib.CapturesDirectory));
        Assert.Empty(Directory.GetDirectories(lib.VideosDirectory));
        Assert.Empty(Directory.GetDirectories(lib.Root, CaptureLibrary.RestoreStagingPrefix + "*"));
        Assert.False(File.Exists(Path.Combine(lib.Root, CaptureLibrary.RestoreJournalFile)));
    }

    // ───────────────────────────── Backup contents and round trip ─────────────────────────────

    [Fact]
    public void FA005_backup_v2_round_trips_images_videos_trash_metadata_damaged_and_incomplete_items()
    {
        using var dir = new TestDirectory();
        RichLibrary rich = RichLibrary.Create(dir.Sub("lib"));
        string zip = dir.Sub("backup.zip");
        BackupResult b = rich.Library.CreateBackup(zip);
        Assert.Equal(6, b.Items.Count); // two images, two videos, the damaged image and the interrupted recording
        Assert.Equal(2, b.ImageCount);
        Assert.Equal(2, b.VideoCount);
        Assert.Equal(2, b.TrashCount);
        Assert.Equal(2, b.DamagedOrIncompleteCount);
        Assert.Contains(b.Items, i => i.Id == rich.Damaged && i.State == BackupItemState.Damaged && i.Reason is not null);
        Assert.Contains(b.Items, i => i.Id == rich.Recording && i.State == BackupItemState.Incomplete);
        Assert.Contains(b.Excluded, e => e.Path == "tray.json" && e.Reason.Length > 0);
        Assert.Contains(b.Excluded, e => e.Path == "notes.txt" && e.Reason.Length > 0);

        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            JsonObject manifest = (JsonObject)JsonNode.Parse(archive.GetEntry(CaptureLibrary.BackupManifestEntry)!.Open())!;
            Assert.Equal(2, manifest["version"]!.GetValue<int>());
            var listed = ((JsonArray)manifest["entries"]!).Select(e => e!["name"]!.GetValue<string>()).ToHashSet();
            Assert.Equal(archive.Entries.Count - 1, listed.Count); // every entry is listed with size and hash
            Assert.All(archive.Entries.Where(e => e.FullName != CaptureLibrary.BackupManifestEntry), e => Assert.Contains(e.FullName, listed));
            Assert.Contains($"raw/captures/{rich.Damaged:D}/{CaptureFormat.ManifestFile}", listed);
            Assert.Contains($"raw/videos/{rich.Recording:D}/{CaptureLibrary.RecordingFile}", listed);
            ZipArchiveEntry mp4 = archive.GetEntry($"videos/{rich.Video:D}/{CaptureLibrary.VideoFile}")!;
            Assert.Equal(200_000, mp4.Length);
            Assert.Equal(mp4.Length, mp4.CompressedLength); // stored, not compressed
            Assert.Equal(2, ((JsonArray)manifest["excluded"]!).Count);
        }

        var fresh = CaptureLibrary.Open(dir.Sub("restored"));
        RestoreResult r = fresh.RestoreFromBackup(zip);
        Assert.Equal(2, r.BackupVersion);
        Assert.False(r.IsLegacyImageOnlyBackup);
        Assert.Equal(6, r.Restored.Count);
        Assert.Empty(r.SkippedExisting);
        Assert.Equal(2, r.ExcludedAtBackup.Count);

        var reopened = CaptureLibrary.Open(dir.Sub("restored"));
        CaptureRecord organised = reopened.Find(rich.Organised)!;
        Assert.Equal(["bug", "ui"], organised.Meta.Tags);
        Assert.Equal("Project X", organised.Meta.Folder);
        Assert.True(organised.IsFavorite);
        Assert.True(organised.HasReversibleRedactions);
        Assert.True(reopened.Find(rich.Trashed)!.IsDeleted);
        Assert.True(reopened.Find(rich.TrashedVideo)!.IsDeleted);
        Assert.Equal(["demo"], reopened.Find(rich.Video)!.Meta.Tags);
        Assert.Equal(File.ReadAllBytes(rich.Library.VideoPath(rich.Video)), File.ReadAllBytes(reopened.VideoPath(rich.Video)));
        Assert.Equal(IntegrityStatus.Verified, reopened.VerifyIntegrity(rich.Video).Status);
        Assert.Equal(rich.Library.Load(rich.Organised).BaseImage.Pixels, reopened.Load(rich.Organised).BaseImage.Pixels);
        Assert.NotNull(reopened.LoadThumbnail(rich.Organised));
        Assert.NotNull(reopened.Find(rich.Damaged)!.Damage); // restored as it was, still listed as damaged
        Assert.Equal(rich.Recording, Assert.Single(reopened.InterruptedRecordings()).Id); // still recoverable
        Assert.Equal(5, reopened.Records.Count); // the interrupted recording is not a record
    }

    [Fact]
    public void FA005_existing_items_are_never_overwritten_and_are_reported_as_skipped()
    {
        using var dir = new TestDirectory();
        RichLibrary rich = RichLibrary.Create(dir.Sub("lib"));
        string zip = dir.Sub("backup.zip");
        rich.Library.CreateBackup(zip);
        rich.Library.Rename(rich.Organised, "Changed after the backup");
        Dictionary<string, string> before = Snapshot(dir.Sub("lib"));

        RestoreResult r = rich.Library.RestoreFromBackup(zip);
        Assert.Empty(r.Restored);
        Assert.Equal(6, r.SkippedExisting.Count);
        Assert.Equal(before, Snapshot(dir.Sub("lib")));
        Assert.Equal("Changed after the backup", CaptureLibrary.Open(dir.Sub("lib")).Find(rich.Organised)!.Title);
        Assert.Equal(0, rich.Library.RestoreBackup(zip)); // the int-returning API the App uses
    }

    [Fact]
    public void FA005_legacy_v1_backup_fixture_is_restored_and_labelled_image_only()
    {
        using var dir = new TestDirectory();
        var index = LegacyFixtures.CopyTo(dir.Sub("fixture"));
        var lib = CaptureLibrary.Open(dir.Sub("lib"));
        RestoreResult r = lib.RestoreFromBackup(Path.Combine(dir.Sub("fixture"), "backup-v1.zip"));
        Assert.Equal(1, r.BackupVersion);
        Assert.True(r.IsLegacyImageOnlyBackup);
        Assert.Equal(index["backupV1Count"]!.GetValue<int>(), r.Restored.Count);
        Assert.All(r.Restored, i => Assert.Equal(CaptureKind.Image, i.Kind));
        var reopened = CaptureLibrary.Open(dir.Sub("lib"));
        Assert.Equal(["bug", "ui"], reopened.Find(LegacyFixtures.Id(index, "organised"))!.Meta.Tags);
        Assert.True(reopened.Find(LegacyFixtures.Id(index, "trashed"))!.IsDeleted);
        foreach (RestoredItem i in r.Restored)
        {
            Assert.Equal(IntegrityStatus.Verified, reopened.VerifyIntegrity(i.Id).Status);
        }
    }

    [Fact]
    public void FA005_backup_is_atomic_cancellable_reports_progress_and_checks_free_space()
    {
        using var dir = new TestDirectory();
        RichLibrary rich = RichLibrary.Create(dir.Sub("lib"));
        string zip = dir.Sub("backup.zip");
        File.WriteAllText(zip, "previous backup");

        // Cancellation partway: no new file, no temp file, the previous file is intact.
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<BackupProgress>(p =>
        {
            if (p.BytesDone > 0)
            {
                cts.Cancel();
            }
        });
        Assert.ThrowsAny<OperationCanceledException>(() => rich.Library.CreateBackup(zip, progress, cts.Token));
        Assert.Equal("previous backup", File.ReadAllText(zip));
        Assert.Empty(Directory.GetFiles(dir.Path, "*" + AtomicFile.TempSuffix));

        // A crash while writing: same.
        Assert.NotNull(Crash.At(3, () => rich.Library.CreateBackup(zip)));
        Assert.Equal("previous backup", File.ReadAllText(zip));

        // Not enough free space: refused before writing anything.
        CaptureLibrary.FreeSpaceOverride.Value = _ => 1024;
        try
        {
            Assert.Contains("free space", Assert.Throws<IOException>(() => rich.Library.CreateBackup(dir.Sub("other.zip"))).Message, StringComparison.Ordinal);
        }
        finally
        {
            CaptureLibrary.FreeSpaceOverride.Value = null;
        }

        Assert.False(File.Exists(dir.Sub("other.zip")));

        var seen = new List<BackupProgress>();
        BackupResult ok = rich.Library.CreateBackup(zip, new SyncProgress<BackupProgress>(seen.Add));
        Assert.Equal(ok.TotalBytes, seen[^1].BytesDone);
        Assert.True(seen.Count > 10);
    }

    [Fact]
    public void FA008_backup_detects_a_same_length_tampered_video_and_records_legacy_hashes()
    {
        using var dir = new TestDirectory();
        RichLibrary rich = RichLibrary.Create(dir.Sub("lib"));
        VideoIntegrityTests.FlipOneByte(rich.Library.VideoPath(rich.Video));
        BackupResult b = rich.Library.CreateBackup(dir.Sub("b.zip"));
        BackupItem video = Assert.Single(b.Items, i => i.Id == rich.Video);
        Assert.Equal(BackupItemState.Damaged, video.State);
        Assert.Contains(b.Warnings, w => w.Contains(rich.Video.ToString(), StringComparison.Ordinal));

        // Legacy video (no recorded hash): the backup records it and says so.
        var index = LegacyFixtures.CopyTo(dir.Sub("legacy"));
        var legacy = CaptureLibrary.Open(Path.Combine(dir.Sub("legacy"), "library"));
        BackupResult lb = legacy.CreateBackup(dir.Sub("legacy.zip"));
        Assert.Contains(lb.Warnings, w => w.Contains("not previously verified", StringComparison.Ordinal));
        Assert.Equal(IntegrityStatus.Verified, legacy.VerifyIntegrity(LegacyFixtures.Id(index, "video")).Status);
        Assert.Equal(BackupItemState.Damaged, Assert.Single(lb.Items, i => i.Id == LegacyFixtures.Id(index, "damaged")).State);
    }

    // ───────────────────────────── Restore rejections ─────────────────────────────

    /// <summary>Writes a v2-style backup with the given entries (name → bytes) and a manifest built from them.</summary>
    private static string CraftBackup(string path, IEnumerable<(string Name, byte[] Data)> entries, Action<JsonObject>? editManifest = null, bool addManifest = true)
    {
        var list = entries.ToList();
        // Items for the well-formed prefixes only (one per id), so a malformed entry name reaches the entry checks.
        var items = new Dictionary<string, JsonObject>();
        var ids = new HashSet<string>();
        foreach ((string name, _) in list)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(name, "^(?<raw>raw/)?(?<area>captures|videos)/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/");
            if (!m.Success || (!items.ContainsKey(m.Value) && !ids.Add(m.Groups["id"].Value)))
            {
                continue;
            }

            bool raw = m.Groups["raw"].Success;
            items.TryAdd(m.Value, new JsonObject { ["id"] = m.Groups["id"].Value, ["kind"] = m.Groups["area"].Value == "videos" ? "video" : "image", ["state"] = raw ? "damaged" : "ok", ["prefix"] = m.Value, ["inTrash"] = false });
        }

        var manifest = new JsonObject
        {
            ["format"] = "snapforge.backup",
            ["version"] = 2,
            ["items"] = new JsonArray([.. items.Values]),
            ["entries"] = new JsonArray([.. list.Select(e => (JsonNode)new JsonObject { ["name"] = e.Name, ["bytes"] = e.Data.Length, ["sha256"] = AtomicFile.Sha256Hex(e.Data) })]),
            ["excluded"] = new JsonArray(),
        };
        editManifest?.Invoke(manifest);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, byte[] data) in list)
        {
            using Stream s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
            s.Write(data);
        }

        if (addManifest)
        {
            using Stream m = zip.CreateEntry(CaptureLibrary.BackupManifestEntry).Open();
            m.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }

        return path;
    }

    /// <summary>The entries of a genuine one-image backup.</summary>
    private static List<(string Name, byte[] Data)> GenuineEntries(TestDirectory dir, out Guid id)
    {
        var lib = CaptureLibrary.Open(dir.Sub("src-" + Guid.NewGuid().ToString("N")));
        PixelBuffer img = PixelBufferTests.Gradient(16, 16);
        id = lib.Create(new CaptureContent(img, []), img, Now, "x").Id;
        string zip = dir.Sub("genuine-" + Guid.NewGuid().ToString("N") + ".zip");
        lib.CreateBackup(zip);
        using ZipArchive archive = ZipFile.OpenRead(zip);
        return [.. archive.Entries.Where(e => e.FullName != CaptureLibrary.BackupManifestEntry).Select(e =>
        {
            using var ms = new MemoryStream();
            using (Stream s = e.Open())
            {
                s.CopyTo(ms);
            }

            return (e.FullName, ms.ToArray());
        })];
    }

    private static void AssertRejected(TestDirectory dir, string zip, string expectedMessagePart)
    {
        var lib = CaptureLibrary.Open(dir.Sub("target-" + Guid.NewGuid().ToString("N")));
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => lib.RestoreFromBackup(zip));
        Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertNothingRestored(lib);
        AssertNothingRestored(CaptureLibrary.Open(lib.Root));
    }

    [Fact]
    public void FA017_genuine_crafted_backup_is_accepted_so_the_rejections_below_are_meaningful()
    {
        using var dir = new TestDirectory();
        string zip = CraftBackup(dir.Sub("ok.zip"), GenuineEntries(dir, out Guid id));
        var lib = CaptureLibrary.Open(dir.Sub("t"));
        Assert.Equal(id, Assert.Single(lib.RestoreFromBackup(zip).Restored).Id);
    }

    [Theory]
    [InlineData(3, "newer version")]
    [InlineData(99, "newer version")]
    [InlineData(0, "Unknown backup format version")]
    public void FA017_unknown_or_future_backup_versions_are_rejected(int version, string message)
    {
        using var dir = new TestDirectory();
        AssertRejected(dir, CraftBackup(dir.Sub("v.zip"), GenuineEntries(dir, out _), m => m["version"] = version), message);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{"format":"snapforge.backup","version":"two"}""")]
    [InlineData("""{"format":"something.else","version":2}""")]
    [InlineData("""{"format":"snapforge.backup","version":2,"items":[],"entries":{}}""")]
    public void FA017_malformed_manifests_are_rejected(string manifest)
    {
        using var dir = new TestDirectory();
        string zip = dir.Sub("m.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using Stream s = z.CreateEntry(CaptureLibrary.BackupManifestEntry).Open();
            s.Write(Encoding.UTF8.GetBytes(manifest));
        }

        var lib = CaptureLibrary.Open(dir.Sub("t"));
        Assert.Throws<InvalidDataException>(() => lib.RestoreFromBackup(zip));
        AssertNothingRestored(lib);
    }

    [Fact]
    public void FA017_not_a_zip_and_no_manifest_are_rejected()
    {
        using var dir = new TestDirectory();
        File.WriteAllText(dir.Sub("text.zip"), "definitely not a zip");
        AssertRejected(dir, dir.Sub("text.zip"), "not a valid ZIP");
        AssertRejected(dir, CraftBackup(dir.Sub("nomanifest.zip"), GenuineEntries(dir, out _), addManifest: false), "not a SnapForge library backup");
    }

    [Fact]
    public void FA017_duplicate_entry_names_ignoring_case_are_rejected()
    {
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out Guid id);
        (string name, byte[] data) = entries.First(e => e.Name.EndsWith(CaptureFormat.MetaFile, StringComparison.Ordinal));
        entries.Add((name.Replace("meta.json", "META.json", StringComparison.Ordinal), data));
        AssertRejected(dir, CraftBackup(dir.Sub("dup.zip"), entries), "more than once");

        // Listed twice in the manifest (one zip entry).
        AssertRejected(dir, CraftBackup(dir.Sub("dup2.zip"), GenuineEntries(dir, out _), m => ((JsonArray)m["entries"]!).Add(((JsonArray)m["entries"]!)[0]!.DeepClone())), "more than once");
    }

    [Theory]
    [InlineData("captures/{id}/evil.exe")]
    [InlineData("captures/{id}/../../evil.txt")]
    [InlineData("captures/{ID}/meta.json")] // upper-case id: not the exact pattern
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("captures\\{id}\\meta.json")]
    [InlineData("videos/{id}/recording.mp4")]
    [InlineData("raw/captures/{new}/.hidden")]
    [InlineData("raw/captures/{new}/sub/file.txt")]
    [InlineData("captures/{id}/")]
    [InlineData("other/{id}/meta.json")]
    public void FA017_entry_names_that_are_not_exactly_the_expected_patterns_are_rejected(string pattern)
    {
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out Guid id);
        entries.Add((pattern.Replace("{id}", id.ToString("D"), StringComparison.Ordinal).Replace("{ID}", id.ToString("D").ToUpperInvariant(), StringComparison.Ordinal).Replace("{new}", Guid.NewGuid().ToString("D"), StringComparison.Ordinal), [1, 2, 3]));
        AssertRejected(dir, CraftBackup(dir.Sub("n.zip"), entries), "entry");
        Assert.False(File.Exists(Path.Combine(dir.Path, "evil.txt")));
    }

    [Fact]
    public void FA017_entries_not_listed_or_listed_but_missing_are_rejected()
    {
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out Guid id);
        AssertRejected(dir, CraftBackup(dir.Sub("unlisted.zip"), entries, m => ((JsonArray)m["entries"]!).RemoveAt(0)), "not listed");
        AssertRejected(dir, CraftBackup(dir.Sub("missing.zip"), entries, m => ((JsonArray)m["entries"]!).Add(new JsonObject { ["name"] = $"captures/{id:D}/thumb.7.sfpx", ["bytes"] = 1, ["sha256"] = new string('0', 64) })), "missing");
    }

    [Fact]
    public void FA017_entry_count_beyond_the_limit_is_rejected()
    {
        using var dir = new TestDirectory();
        CaptureLibrary.MaxEntriesOverride.Value = 3;
        try
        {
            AssertRejected(dir, CraftBackup(dir.Sub("many.zip"), GenuineEntries(dir, out _)), "more than 3 entries");
        }
        finally
        {
            CaptureLibrary.MaxEntriesOverride.Value = null;
        }

        Assert.Equal(200_000, BackupLimits.MaxEntries);
    }

    [Fact]
    public void FA017_declared_sizes_beyond_limits_or_free_space_are_rejected_before_extraction()
    {
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out _);
        int meta = entries.FindIndex(e => e.Name.EndsWith(CaptureFormat.MetaFile, StringComparison.Ordinal));
        AssertRejected(dir, CraftBackup(dir.Sub("big.zip"), entries, m => ((JsonArray)m["entries"]!)[meta]!["bytes"] = BackupLimits.MaxSmallFileBytes + 1), "too large");
        AssertRejected(dir, CraftBackup(dir.Sub("neg.zip"), entries, m => ((JsonArray)m["entries"]!)[meta]!["bytes"] = -1), "too large");

        CaptureLibrary.FreeSpaceOverride.Value = _ => BackupLimits.FreeSpaceReserveBytes + 10; // total fits the limits but not the disk
        try
        {
            AssertRejected(dir, CraftBackup(dir.Sub("space.zip"), entries), "free space");
        }
        finally
        {
            CaptureLibrary.FreeSpaceOverride.Value = null;
        }
    }

    [Fact]
    public void FA017_entry_whose_hash_differs_from_the_manifest_is_rejected()
    {
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out _);
        AssertRejected(dir, CraftBackup(dir.Sub("hash.zip"), entries, m => ((JsonArray)m["entries"]!)[0]!["sha256"] = new string('a', 64)), "hash");
        AssertRejected(dir, CraftBackup(dir.Sub("size.zip"), entries, m => ((JsonArray)m["entries"]!)[0]!["bytes"] = entries[0].Data.Length - 1), "size");
    }

    [Fact]
    public void FA023_zip_header_that_disagrees_with_the_manifest_is_rejected()
    {
        // The headers say 1 MiB while the manifest declares 10 bytes: rejected before anything is extracted.
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out _);
        entries.Add(($"raw/captures/{Guid.NewGuid():D}/blob.bin", new byte[1 << 20]));
        AssertRejected(dir, CraftBackup(dir.Sub("lie.zip"), entries, m => ((JsonArray)m["entries"]!)[^1]!["bytes"] = 10), "declared size");
    }

    [Fact]
    public void FA023_lying_zip_header_never_expands_an_entry_beyond_its_declared_size()
    {
        // A zip-bomb-like entry: 1 MiB of zeros deflated to ~1 KB, whose headers claim 10 bytes (as does the manifest).
        // Reading stops at the declared size + 1 byte, so at most 10 bytes ever reach the disk; a runtime that would
        // return more makes the restore fail instead ("longer than declared").
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out Guid id);
        string bombName = $"raw/captures/{Guid.NewGuid():D}/blob.bin";
        string zip = dir.Sub("bomb.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach ((string name, byte[] data) in entries)
            {
                using Stream s = z.CreateEntry(name, CompressionLevel.NoCompression).Open();
                s.Write(data);
            }

            using (Stream s = z.CreateEntry(bombName, CompressionLevel.SmallestSize).Open())
            {
                s.Write(new byte[1 << 20]);
            }

            var manifest = new JsonObject
            {
                ["format"] = "snapforge.backup",
                ["version"] = 2,
                ["items"] = new JsonArray(
                    new JsonObject { ["id"] = id.ToString("D"), ["kind"] = "image", ["state"] = "ok", ["prefix"] = $"captures/{id:D}/" },
                    new JsonObject { ["id"] = bombName.Split('/')[2], ["kind"] = "image", ["state"] = "damaged", ["prefix"] = $"raw/captures/{bombName.Split('/')[2]}/" }),
                ["entries"] = new JsonArray([.. entries.Select(e => (JsonNode)new JsonObject { ["name"] = e.Name, ["bytes"] = e.Data.Length, ["sha256"] = AtomicFile.Sha256Hex(e.Data) }),
                    new JsonObject { ["name"] = bombName, ["bytes"] = 10, ["sha256"] = AtomicFile.Sha256Hex(new byte[10]) }]),
            };
            using Stream m = z.CreateEntry(CaptureLibrary.BackupManifestEntry).Open();
            m.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }

        PatchUncompressedSize(zip, bombName, 10);
        using (ZipArchive check = ZipFile.OpenRead(zip))
        {
            Assert.Equal(10, check.GetEntry(bombName)!.Length); // the header now lies
        }

        var lib = CaptureLibrary.Open(dir.Sub("t"));
        try
        {
            lib.RestoreFromBackup(zip);
            string blob = Path.Combine(lib.CapturesDirectory, bombName.Split('/')[2], "blob.bin");
            Assert.Equal(10, new FileInfo(blob).Length);
        }
        catch (InvalidDataException)
        {
            AssertNothingRestored(lib);
        }

        Assert.True(Directory.GetFiles(lib.Root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) < 100_000);
    }

    [Fact]
    public void FA008_same_length_tampered_video_inside_a_backup_is_rejected_on_restore()
    {
        using var dir = new TestDirectory();
        RichLibrary rich = RichLibrary.Create(dir.Sub("lib"));
        string zip = dir.Sub("b.zip");
        rich.Library.CreateBackup(zip);
        byte[] original = File.ReadAllBytes(rich.Library.VideoPath(rich.Video));
        byte[] archive = File.ReadAllBytes(zip);
        int at = archive.AsSpan().IndexOf(original.AsSpan(1000, 64)); // stored: the MP4 bytes appear verbatim
        Assert.True(at > 0);
        archive[at + 10] ^= 0xFF;
        File.WriteAllBytes(zip, archive);
        var lib = CaptureLibrary.Open(dir.Sub("t"));
        Assert.Contains("hash", Assert.Throws<InvalidDataException>(() => lib.RestoreFromBackup(zip)).Message, StringComparison.Ordinal);
        AssertNothingRestored(lib);
    }

    [Fact]
    public void FA017_item_that_does_not_validate_rejects_the_whole_restore()
    {
        // Self-consistent archive (hashes match the manifest) whose capture manifest references a file it lacks.
        using var dir = new TestDirectory();
        List<(string Name, byte[] Data)> entries = GenuineEntries(dir, out _);
        entries.RemoveAll(e => e.Name.Contains("/base.", StringComparison.Ordinal));
        AssertRejected(dir, CraftBackup(dir.Sub("item.zip"), entries), "does not validate");
    }

    /// <summary>Sets the uncompressed size of an entry in both its local header and central directory record.</summary>
    private static void PatchUncompressedSize(string zipPath, string entryName, uint size)
    {
        byte[] zip = File.ReadAllBytes(zipPath);
        byte[] name = Encoding.UTF8.GetBytes(entryName);
        int patched = 0;
        for (int i = 0; i + 46 < zip.Length; i++)
        {
            uint sig = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i));
            if (sig == 0x04034b50 && zip.AsSpan(i + 30, Math.Min(name.Length, zip.Length - i - 30)).SequenceEqual(name))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(i + 22), size);
                patched++;
            }
            else if (sig == 0x02014b50 && zip.AsSpan(i + 46, Math.Min(name.Length, zip.Length - i - 46)).SequenceEqual(name))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(i + 24), size);
                patched++;
            }
        }

        Assert.True(patched >= 2, "entry headers not found");
        File.WriteAllBytes(zipPath, zip);
    }

    // ───────────────────────────── Interruption and cancellation ─────────────────────────────

    [Fact]
    public void FA005_interruption_at_every_restore_phase_is_completed_or_cleaned_up_on_next_open()
    {
        using var source = new TestDirectory();
        RichLibrary rich = RichLibrary.Create(source.Sub("lib"));
        string zip = source.Sub("b.zip");
        rich.Library.CreateBackup(zip);

        int total;
        using (var probe = new TestDirectory())
        {
            var lib = CaptureLibrary.Open(probe.Path);
            List<string> points = Crash.Checkpoints(() => lib.RestoreFromBackup(zip));
            total = points.Count;
            Assert.Contains("restore.journal-written", points);
            Assert.Contains(points, p => p.StartsWith("restore.extracted:", StringComparison.Ordinal));
            Assert.Contains(points, p => p.StartsWith("restore.adopted:", StringComparison.Ordinal));
        }

        for (int n = 0; n < total; n++)
        {
            using var dir = new TestDirectory();
            var lib = CaptureLibrary.Open(dir.Path);
            string? point = Crash.At(n, () => lib.RestoreFromBackup(zip));
            Assert.NotNull(point);

            var reopened = CaptureLibrary.Open(dir.Path); // completes (after the journal) or cleans up (before it)
            Assert.Empty(Directory.GetDirectories(dir.Path, CaptureLibrary.RestoreStagingPrefix + "*"));
            Assert.False(File.Exists(Path.Combine(dir.Path, CaptureLibrary.RestoreJournalFile)));
            if (reopened.Records.Count == 0)
            {
                Assert.Null(reopened.RecoveredRestore);
                Assert.Empty(Directory.GetDirectories(reopened.CapturesDirectory));
            }
            else
            {
                // After the journal was removed the restore had finished; before that, opening resumed it.
                Assert.True(point == "restore.completed" || reopened.RecoveredRestore?.Resumed == true, $"crash at {point}: items present but no resumed restore");
                Assert.Equal(5, reopened.Records.Count); // all of it, never a part
                Assert.Single(reopened.InterruptedRecordings());
            }

            foreach (CaptureRecord r in reopened.Records.Where(r => r.Damage is null))
            {
                Assert.NotEqual(IntegrityStatus.Damaged, reopened.VerifyIntegrity(r.Id).Status);
            }

            // Restoring again converges on the complete state.
            reopened.RestoreFromBackup(zip);
            Assert.Equal(5, CaptureLibrary.Open(dir.Path).Records.Count);
        }
    }

    [Fact]
    public void FA005_cancelled_restore_adds_nothing_and_leaves_no_staging()
    {
        using var dir = new TestDirectory();
        RichLibrary rich = RichLibrary.Create(dir.Sub("lib"));
        string zip = dir.Sub("b.zip");
        rich.Library.CreateBackup(zip);
        var lib = CaptureLibrary.Open(dir.Sub("t"));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<BackupProgress>(p =>
        {
            if (p.BytesDone > 50_000)
            {
                cts.Cancel();
            }
        });
        Assert.ThrowsAny<OperationCanceledException>(() => lib.RestoreFromBackup(zip, progress, cts.Token));
        AssertNothingRestored(lib);
    }
}
