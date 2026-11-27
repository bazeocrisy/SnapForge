using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>FA-006/FA-007: relocation copies everything, verifies it, and deletes the source only in a separate, explicit step.</summary>
public class RelocationTests
{
    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(root, f), f => AtomicFile.Sha256Hex(File.ReadAllBytes(f)));

    [Fact]
    public void FA006_copy_includes_interrupted_recordings_damaged_items_unknown_files_and_legacy_items()
    {
        using var dir = new TestDirectory();
        BackupRestoreTests.RichLibrary rich = BackupRestoreTests.RichLibrary.Create(dir.Sub("old"));
        string unknownDir = Path.Combine(dir.Sub("old"), "captures", "not-an-id");
        Directory.CreateDirectory(unknownDir);
        File.WriteAllText(Path.Combine(unknownDir, "mystery.bin"), "unknown");
        File.WriteAllText(Path.Combine(dir.Sub("old"), ".half" + AtomicFile.TempSuffix), "temp");
        Dictionary<string, string> before = Snapshot(dir.Sub("old"));

        RelocationResult r = rich.Library.CopyTo(dir.Sub("new"));
        Assert.Equal(5, r.ItemCount);
        Assert.Equal(1, r.DamagedCount);
        Assert.Equal(1, r.InterruptedRecordingCount);
        Assert.Contains(".half" + AtomicFile.TempSuffix, r.SkippedTemporary);
        Assert.Equal(before, Snapshot(dir.Sub("old"))); // the source is untouched

        Dictionary<string, string> after = Snapshot(dir.Sub("new"));
        foreach ((string rel, string hash) in before.Where(p => !p.Key.EndsWith(AtomicFile.TempSuffix, StringComparison.Ordinal)))
        {
            Assert.Equal(hash, after[rel]); // byte-for-byte, including notes.txt, tray.json, captures/not-an-id/mystery.bin
        }

        Assert.True(File.Exists(r.JournalPath));
        var moved = CaptureLibrary.Open(dir.Sub("new"));
        Assert.NotNull(moved.Find(rich.Damaged)!.Damage);
        Assert.Equal(rich.Recording, Assert.Single(moved.InterruptedRecordings()).Id);
        Assert.Equal(IntegrityStatus.Verified, moved.VerifyIntegrity(rich.Video).Status);
        Assert.Equal(["bug", "ui"], moved.Find(rich.Organised)!.Meta.Tags);

        // The legacy v1 fixture moves the same way, without being upgraded.
        var index = LegacyFixtures.CopyTo(dir.Sub("legacy"));
        RelocationResult lr = CaptureLibrary.Open(Path.Combine(dir.Sub("legacy"), "library")).CopyTo(dir.Sub("legacy-new"));
        Assert.Equal(5, lr.ItemCount);
        Assert.Equal(1, CaptureLibrary.Open(dir.Sub("legacy-new")).ReadCaptureManifest(LegacyFixtures.Id(index, "organised")).Version);
    }

    [Fact]
    public void FA006_a_crash_at_any_point_of_copy_or_verify_leaves_the_source_untouched()
    {
        using var dir = new TestDirectory();
        BackupRestoreTests.RichLibrary rich = BackupRestoreTests.RichLibrary.Create(dir.Sub("old"));
        Dictionary<string, string> before = Snapshot(dir.Sub("old"));
        List<string> points = Crash.Checkpoints(() => rich.Library.CopyTo(dir.Sub("probe")));
        Assert.Contains(points, p => p.StartsWith("relocate.copied:", StringComparison.Ordinal));
        Assert.Contains(points, p => p.StartsWith("relocate.verified:", StringComparison.Ordinal));
        Assert.DoesNotContain(points, p => p.StartsWith("delete.", StringComparison.Ordinal)); // the copy phase never deletes

        for (int n = 0; n < points.Count; n += 3)
        {
            string target = dir.Sub($"new-{n}");
            Assert.NotNull(Crash.At(n, () => rich.Library.CopyTo(target)));
            Assert.Equal(before, Snapshot(dir.Sub("old")));
            Assert.Equal(5, CaptureLibrary.Open(dir.Sub("old")).Records.Count);
        }
    }

    [Fact]
    public void FA006_a_copy_that_fails_verification_is_reported_and_the_source_is_untouched()
    {
        using var dir = new TestDirectory();
        BackupRestoreTests.RichLibrary rich = BackupRestoreTests.RichLibrary.Create(dir.Sub("old"));
        Dictionary<string, string> before = Snapshot(dir.Sub("old"));
        string videoRel = $"videos/{rich.Video:D}/{CaptureLibrary.VideoFile}";
        DurabilityFaults.Hook.Value = p =>
        {
            if (p == "relocate.copied:" + videoRel)
            {
                VideoIntegrityTests.FlipOneByte(Path.Combine(dir.Sub("new"), videoRel.Replace('/', Path.DirectorySeparatorChar)));
            }
        };
        try
        {
            RelocationException ex = Assert.Throws<RelocationException>(() => rich.Library.CopyTo(dir.Sub("new")));
            Assert.Contains("does not match", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DurabilityFaults.Hook.Value = null;
        }

        Assert.Equal(before, Snapshot(dir.Sub("old")));
        Assert.False(File.Exists(Path.Combine(dir.Sub("new"), CaptureLibrary.RelocationJournalFile))); // nothing to clean up the source with
    }

    [Fact]
    public void FA007_source_cleanup_deletes_only_verified_unchanged_files_and_reports_leftovers()
    {
        using var dir = new TestDirectory();
        BackupRestoreTests.RichLibrary rich = BackupRestoreTests.RichLibrary.Create(dir.Sub("old"));
        RelocationResult r = rich.Library.CopyTo(dir.Sub("new"));

        // After the copy: a new unknown file appears and a copied file changes in the old location.
        File.WriteAllText(Path.Combine(dir.Sub("old"), "written-later.txt"), "new");
        File.WriteAllText(Path.Combine(dir.Sub("old"), "notes.txt"), "edited after the copy");

        SourceCleanupResult c = rich.Library.DeleteSourceAfterVerifiedMove(r);
        Assert.Contains("notes.txt", c.Changed);
        Assert.Contains("written-later.txt", c.LeftInPlace);
        Assert.Contains("notes.txt", c.LeftInPlace);
        Assert.Equal(r.Files.Count - 1, c.Deleted.Count);
        Assert.Empty(c.Missing);
        Assert.Equal(["notes.txt", "written-later.txt"], Directory.GetFiles(dir.Sub("old"), "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(rich.Library.Records);

        // The new location is complete and unaffected.
        var moved = CaptureLibrary.Open(dir.Sub("new"));
        Assert.Equal(5, moved.Records.Count);
        Assert.Equal("user file", File.ReadAllText(Path.Combine(dir.Sub("new"), "notes.txt")));

        // Running it again is harmless: everything listed is already gone.
        SourceCleanupResult again = CaptureLibrary.DeleteSourceAfterVerifiedMove(r.JournalPath);
        Assert.Empty(again.Deleted);
        Assert.Equal(r.Files.Count - 1, again.Missing.Count);
    }

    [Fact]
    public void FA007_source_files_whose_copy_disappeared_are_kept()
    {
        using var dir = new TestDirectory();
        BackupRestoreTests.RichLibrary rich = BackupRestoreTests.RichLibrary.Create(dir.Sub("old"));
        RelocationResult r = rich.Library.CopyTo(dir.Sub("new"));
        File.Delete(Path.Combine(dir.Sub("new"), "tray.json"));
        SourceCleanupResult c = CaptureLibrary.DeleteSourceAfterVerifiedMove(r.JournalPath);
        Assert.Contains("tray.json", c.Changed);
        Assert.True(File.Exists(Path.Combine(dir.Sub("old"), "tray.json")));
    }

    [Fact]
    public void FA006_refuses_before_copying_unsafe_targets_and_lack_of_space()
    {
        using var dir = new TestDirectory();
        BackupRestoreTests.RichLibrary rich = BackupRestoreTests.RichLibrary.Create(dir.Sub("old"));
        Assert.Throws<ArgumentException>(() => rich.Library.CopyTo(dir.Sub("old")));
        Assert.Throws<ArgumentException>(() => rich.Library.CopyTo(Path.Combine(dir.Sub("old"), "inside")));
        Assert.Throws<ArgumentException>(() => rich.Library.CopyTo(dir.Path));

        Directory.CreateDirectory(dir.Sub("collide"));
        File.WriteAllText(Path.Combine(dir.Sub("collide"), "notes.txt"), "someone else's file");
        Assert.Throws<RelocationException>(() => rich.Library.CopyTo(dir.Sub("collide")));
        Assert.Equal("someone else's file", File.ReadAllText(Path.Combine(dir.Sub("collide"), "notes.txt")));
        Assert.Single(Directory.GetFileSystemEntries(dir.Sub("collide")));

        CaptureLibrary.FreeSpaceOverride.Value = _ => 1000;
        try
        {
            Assert.Contains("free space", Assert.Throws<RelocationException>(() => rich.Library.CopyTo(dir.Sub("small"))).Message, StringComparison.Ordinal);
        }
        finally
        {
            CaptureLibrary.FreeSpaceOverride.Value = null;
        }

        Assert.Empty(Directory.GetFiles(dir.Sub("small"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void FA006_unreadable_source_file_refuses_before_copying_where_the_platform_can_lock_it()
    {
        using var dir = new TestDirectory();
        BackupRestoreTests.RichLibrary rich = BackupRestoreTests.RichLibrary.Create(dir.Sub("old"));
        string locked = Path.Combine(dir.Sub("old"), "notes.txt");
        using var holder = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            using var probe = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
            return; // this platform does not enforce the lock; nothing to observe
        }
        catch (IOException)
        {
        }

        RelocationException ex = Assert.Throws<RelocationException>(() => rich.Library.CopyTo(dir.Sub("new")));
        Assert.Contains("notes.txt", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir.Sub("new")) && Directory.EnumerateFiles(dir.Sub("new"), "*", SearchOption.AllDirectories).Any());
    }
}
