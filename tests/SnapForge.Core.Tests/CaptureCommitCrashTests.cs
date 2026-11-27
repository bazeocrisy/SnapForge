using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>
/// FA-015 (generation-based multi-file commits) and FA-016 (crash-safe sanitize): a crash at every durable boundary
/// of Create, SaveContent (with and without a new base) and Sanitize leaves, after reopening, either the complete
/// previous state or the complete new state — never a mix, never an unloadable capture, and (sanitize) never the
/// secret pixels anywhere in the capture folder once the capture says it is sanitized.
/// </summary>
public class CaptureCommitCrashTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);

    private static readonly LineAnnotation First = new(new PointD(1, 1), new PointD(9, 9));
    private static readonly ArrowAnnotation Second = new(new PointD(2, 20), new PointD(30, 4));

    private static CaptureRecord Seed(CaptureLibrary lib) =>
        lib.Create(new CaptureContent(PixelBufferTests.Gradient(40, 30), [First]), PixelBufferTests.Gradient(40, 30), Now, "seed");

    private static void ForEveryCheckpoint(Func<TestDirectory, Action> arrange, Action<TestDirectory, string?> verify)
    {
        int total;
        using (var probe = new TestDirectory())
        {
            total = Crash.Checkpoints(arrange(probe)).Count;
        }

        Assert.True(total >= 8, $"only {total} checkpoints");
        for (int n = 0; n <= total; n++) // n == total: no crash (the operation completes)
        {
            using var dir = new TestDirectory();
            Action operation = arrange(dir);
            string? point = Crash.At(n, operation);
            Assert.True(n == total ? point is null : point is not null);
            verify(dir, point);
        }
    }

    [Fact]
    public void FA015_crash_at_every_boundary_of_Create_leaves_nothing_or_the_complete_capture()
    {
        PixelBuffer img = PixelBufferTests.Gradient(40, 30);
        ForEveryCheckpoint(
            dir =>
            {
                var lib = CaptureLibrary.Open(dir.Path);
                return () => lib.Create(new CaptureContent(img, [First, Second]), img, Now, "new");
            },
            (dir, point) =>
            {
                var reopened = CaptureLibrary.Open(dir.Path);
                Assert.Empty(Directory.GetFiles(dir.Path, "*" + AtomicFile.TempSuffix, SearchOption.AllDirectories));
                if (reopened.Records.Count == 0)
                {
                    Assert.NotNull(point); // only an interrupted create may leave nothing
                    Assert.Empty(Directory.GetDirectories(reopened.CapturesDirectory)); // staging folder removed
                    return;
                }

                CaptureRecord r = Assert.Single(reopened.Records);
                Coherence.AssertCoherent(reopened, r.Id);
                CaptureContent c = reopened.Load(r.Id);
                Assert.Equal(img.Pixels, c.BaseImage.Pixels);
                Assert.Equal([First, Second], c.Annotations);
                Assert.Equal("new", r.Title);
            });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FA015_crash_at_every_boundary_of_SaveContent_leaves_previous_or_new_generation(bool withNewBase)
    {
        PixelBuffer oldBase = PixelBufferTests.Gradient(40, 30);
        PixelBuffer newBase = PixelBufferTests.Gradient(20, 10);
        Guid id = Guid.Empty;
        ForEveryCheckpoint(
            dir =>
            {
                var lib = CaptureLibrary.Open(dir.Path);
                id = Seed(lib).Id;
                PixelBuffer flat = withNewBase ? newBase : oldBase;
                return () => lib.SaveContent(id, withNewBase ? newBase : null, [First, Second], flat, hasReversibleRedactions: false);
            },
            (dir, _) =>
            {
                var reopened = CaptureLibrary.Open(dir.Path);
                Coherence.AssertCoherent(reopened, id);
                CaptureContent c = reopened.Load(id);
                bool isNew = c.Annotations.Count == 2;
                Assert.Equal(isNew ? [First, Second] : [First], c.Annotations);
                Assert.Equal((isNew && withNewBase ? newBase : oldBase).Pixels, c.BaseImage.Pixels); // never new annotations on the old base
                Assert.Equal(isNew && withNewBase ? (20, 10) : (40, 30), (reopened.Find(id)!.Width, reopened.Find(id)!.Height));
                Assert.Equal(isNew ? 2 : 1, reopened.ReadCaptureManifest(id).Generation);
            });
    }

    [Fact]
    public void FA015_crash_at_every_boundary_of_the_lazy_v1_upgrade_leaves_v1_or_v2()
    {
        Guid id = Guid.Empty;
        CaptureContent? before = null;
        ForEveryCheckpoint(
            dir =>
            {
                var index = LegacyFixtures.CopyTo(dir.Path);
                id = LegacyFixtures.Id(index, "organised");
                var lib = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
                before = lib.Load(id);
                return () => lib.SaveContent(id, null, [.. before.Annotations, Second], before.BaseImage, hasReversibleRedactions: true);
            },
            (dir, _) =>
            {
                var reopened = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
                Coherence.AssertCoherent(reopened, id);
                CaptureContent c = reopened.Load(id);
                Assert.Equal(before!.BaseImage.Pixels, c.BaseImage.Pixels);
                CaptureManifest m = reopened.ReadCaptureManifest(id);
                if (m.Version == 1)
                {
                    Assert.Equal(before.Annotations, c.Annotations);
                }
                else
                {
                    Assert.Equal(2, m.Version);
                    Assert.Equal(CaptureFormat.BaseFile, m.Base.Name); // the unchanged legacy base stays referenced, not rewritten
                    Assert.Equal([.. before.Annotations, Second], c.Annotations);
                }

                CaptureRecord r = reopened.Find(id)!;
                Assert.Equal(["bug", "ui"], r.Meta.Tags); // metadata is never lost
                Assert.True(r.IsFavorite);
            });
    }

    public static TheoryData<bool> Sources => new() { true, false };

    [Theory]
    [MemberData(nameof(Sources))]
    public void FA016_crash_at_every_boundary_of_Sanitize_leaves_complete_unsanitized_or_complete_sanitized_state(bool legacyV1Folder)
    {
        Guid id = Guid.Empty;
        string folder = string.Empty;
        CaptureContent? original = null;
        ForEveryCheckpoint(
            dir =>
            {
                CaptureLibrary lib;
                if (legacyV1Folder)
                {
                    var index = LegacyFixtures.CopyTo(dir.Path);
                    id = LegacyFixtures.Id(index, "redacted");
                    lib = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
                }
                else
                {
                    lib = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
                    var seedDoc = new EditorDocument(LegacyFixtures.SecretImage());
                    seedDoc.Execute(new AddAnnotationCommand(new TextAnnotation(new PointD(10, 10), "hunter2-secret", 10) { MeasuredSize = new PointD(28, 10) }));
                    seedDoc.Execute(new RedactCommand(LegacyFixtures.SecretRegion, ArgbColor.Black));
                    seedDoc.Execute(new AddAnnotationCommand(Second));
                    id = lib.Create(new CaptureContent(seedDoc.BaseImage, seedDoc.Annotations), seedDoc.Image, Now, "secret").Id;
                }

                folder = Path.Combine(lib.CapturesDirectory, id.ToString("D"));
                Assert.NotEmpty(SecretScan.FilesContainingSecret(folder)); // the scanner does find the secret before
                original = lib.Load(id);

                // What the App does (MainWindow.SanitizeRecordsAsync): EditorDocument.Sanitize, then Library.Sanitize.
                var doc = new EditorDocument(original.BaseImage.Clone(), original.Annotations);
                doc.Sanitize();
                Annotation[] annotations = [.. doc.Annotations];
                return () => lib.Sanitize(id, doc.BaseImage, annotations, doc.Image);
            },
            (dir, point) =>
            {
                var reopened = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
                Coherence.AssertCoherent(reopened, id);
                CaptureRecord r = reopened.Find(id)!;
                CaptureContent c = reopened.Load(id);
                if (r.SanitizedUtc is null)
                {
                    // Complete previous state, still flagged as holding reversible redactions.
                    Assert.NotNull(point);
                    Assert.True(r.HasReversibleRedactions);
                    Assert.Equal(original!.BaseImage.Pixels, c.BaseImage.Pixels);
                    Assert.Equal(original.Annotations, c.Annotations);
                }
                else
                {
                    Assert.False(r.HasReversibleRedactions);
                    Assert.DoesNotContain(c.Annotations, a => a is RedactionMaskAnnotation or TextAnnotation);
                    Assert.Contains(c.Annotations, a => a.Id == Second.Id || a is ArrowAnnotation);
                    for (int y = LegacyFixtures.SecretRegion.Y; y < LegacyFixtures.SecretRegion.Bottom; y++)
                    {
                        for (int x = LegacyFixtures.SecretRegion.X; x < LegacyFixtures.SecretRegion.Right; x++)
                        {
                            Assert.Equal(ArgbColor.Black, c.BaseImage.GetPixel(x, y));
                        }
                    }

                    // No byte of any file in the capture folder (base, annotations, thumbnail, manifest, meta)
                    // holds the secret pixels or the hidden text.
                    Assert.Empty(SecretScan.FilesContainingSecret(folder));
                }
            });
    }

    [Fact]
    public void FA015_checkpoints_cover_each_file_write_the_commit_and_each_secure_delete()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        Guid id = Seed(lib).Id;
        PixelBuffer img = PixelBufferTests.Gradient(8, 8);
        List<string> points = Crash.Checkpoints(() => lib.SaveContent(id, img, [Second], img, false));
        Assert.Equal(
        [
            "atomic.flushed:base.2.sfpx", "atomic.renamed:base.2.sfpx",
            "atomic.flushed:annotations.2.json", "atomic.renamed:annotations.2.json",
            "atomic.flushed:thumb.2.sfpx", "atomic.renamed:thumb.2.sfpx",
            "capture.generation-written",
            "atomic.flushed:manifest.json", "atomic.renamed:manifest.json",
            "capture.manifest-committed",
        ],
            points.Take(10));
        Assert.Equal(["annotations.1.json", "base.1.sfpx", "thumb.1.sfpx"], points.Skip(10).Select(p => p.Split(':')[1]).Order(StringComparer.Ordinal));
        Assert.All(points.Skip(10), p => Assert.StartsWith("delete.overwritten:", p, StringComparison.Ordinal));
    }

    [Fact]
    public void FA016_sanitize_enforces_mask_burn_in_and_hidden_annotation_removal_whatever_the_caller_passes()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        var doc = new EditorDocument(LegacyFixtures.SecretImage());
        var hidden = new TextAnnotation(new PointD(10, 10), "hunter2-secret", 10) { MeasuredSize = new PointD(28, 10) };
        doc.Execute(new AddAnnotationCommand(hidden));
        doc.Execute(new RedactCommand(LegacyFixtures.SecretRegion, ArgbColor.Black));
        doc.Execute(new AddAnnotationCommand(Second));
        CaptureRecord rec = lib.Create(new CaptureContent(doc.BaseImage, doc.Annotations), doc.Image, Now);

        // A careless caller passes the unsanitized base and the full annotation list.
        lib.Sanitize(rec.Id, doc.BaseImage, doc.Annotations, doc.Image);
        string folder = Path.Combine(lib.CapturesDirectory, rec.Id.ToString("D"));
        Assert.Empty(SecretScan.FilesContainingSecret(folder));
        CaptureContent c = CaptureLibrary.Open(dir.Path).Load(rec.Id);
        Assert.Equal([Second], c.Annotations);
        Assert.Equal(ArgbColor.Black, c.BaseImage.GetPixel(10, 10));
    }

    [Fact]
    public void FA016_sanitize_reports_failure_if_old_files_cannot_be_removed_and_reopen_finishes_the_job()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Path);
        var doc = new EditorDocument(LegacyFixtures.SecretImage());
        doc.Execute(new RedactCommand(LegacyFixtures.SecretRegion, ArgbColor.Black));
        CaptureRecord rec = lib.Create(new CaptureContent(doc.BaseImage, doc.Annotations), doc.Image, Now);
        doc.Sanitize();
        DurabilityFaults.Hook.Value = p =>
        {
            if (p.StartsWith("delete.overwritten:base.1", StringComparison.Ordinal))
            {
                throw new IOException("The file is in use by another process.");
            }
        };
        try
        {
            Assert.Throws<IOException>(() => lib.Sanitize(rec.Id, doc.BaseImage, doc.Annotations, doc.Image));
        }
        finally
        {
            DurabilityFaults.Hook.Value = null;
        }

        string folder = Path.Combine(lib.CapturesDirectory, rec.Id.ToString("D"));
        var reopened = CaptureLibrary.Open(dir.Path);
        Assert.NotNull(reopened.Find(rec.Id)!.SanitizedUtc);
        Assert.Empty(SecretScan.FilesContainingSecret(folder));
        Coherence.AssertCoherent(reopened, rec.Id);
    }
}
