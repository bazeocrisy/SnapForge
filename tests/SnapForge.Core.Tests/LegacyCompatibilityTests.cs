using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>The committed legacy (v1) fixtures open unchanged and upgrade lazily (FA-015).</summary>
public class LegacyCompatibilityTests
{
    private static Dictionary<string, string> Hashes(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(root, f), f => AtomicFile.Sha256Hex(File.ReadAllBytes(f)));

    [Fact]
    public void FA015_legacy_v1_library_opens_every_item_without_modifying_it()
    {
        using var dir = new TestDirectory();
        var index = LegacyFixtures.CopyTo(dir.Path);
        string root = Path.Combine(dir.Path, "library");
        Dictionary<string, string> before = Hashes(root);

        var lib = CaptureLibrary.Open(root);
        Assert.Equal(5, lib.Records.Count);
        Assert.NotNull(lib.Find(LegacyFixtures.Id(index, "damaged"))!.Damage);
        CaptureRecord organised = lib.Find(LegacyFixtures.Id(index, "organised"))!;
        Assert.Equal(["bug", "ui"], organised.Meta.Tags);
        Assert.Equal("Project X", organised.Meta.Folder);
        Assert.True(organised.IsFavorite);
        Assert.True(lib.Find(LegacyFixtures.Id(index, "trashed"))!.IsDeleted);
        CaptureRecord video = lib.Find(LegacyFixtures.Id(index, "video"))!;
        Assert.True(video.IsVideo);
        Assert.Null(video.Damage);
        CaptureRecord redacted = lib.Find(LegacyFixtures.Id(index, "redacted"))!;
        Assert.True(redacted.HasReversibleRedactions);
        foreach (string role in new[] { "redacted", "organised", "trashed" })
        {
            Guid id = LegacyFixtures.Id(index, role);
            Assert.Null(lib.Find(id)!.Damage);
            lib.Load(id);
            Assert.NotNull(lib.LoadThumbnail(id));
            Assert.Equal(1, lib.ReadCaptureManifest(id).Version);
        }

        Assert.Equal(before, Hashes(root)); // opening never rewrites legacy folders
    }

    [Fact]
    public void FA015_legacy_v1_capture_upgrades_on_save_and_removes_the_fixed_name_files()
    {
        using var dir = new TestDirectory();
        var index = LegacyFixtures.CopyTo(dir.Path);
        var lib = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
        Guid id = LegacyFixtures.Id(index, "trashed");
        string folder = Path.Combine(lib.CapturesDirectory, id.ToString("D"));
        CaptureContent c = lib.Load(id);

        // Small save: annotations and thumbnail become generation 1; the unchanged base keeps its v1 name.
        var extra = new LineAnnotation(new PointD(0, 0), new PointD(3, 3));
        lib.SaveContent(id, null, [.. c.Annotations, extra], c.BaseImage, false);
        CaptureManifest m = lib.ReadCaptureManifest(id);
        Assert.Equal((2, 1), (m.Version, m.Generation));
        Assert.Equal(["annotations.1.json", "base.sfpx", "manifest.json", "meta.json", "thumb.1.sfpx"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // New base: the legacy base file is superseded and removed.
        lib.SaveContent(id, PixelBufferTests.Gradient(10, 10), [extra], PixelBufferTests.Gradient(10, 10), false);
        Assert.Equal(["annotations.2.json", "base.2.sfpx", "manifest.json", "meta.json", "thumb.2.sfpx"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var reopened = CaptureLibrary.Open(Path.Combine(dir.Path, "library"));
        Assert.Equal([extra], reopened.Load(id).Annotations);
        Assert.True(reopened.Find(id)!.IsDeleted); // metadata kept through the upgrade
    }

    [Fact]
    public void FA015_package_of_a_v2_capture_uses_the_v1_layout_that_every_version_imports()
    {
        using var dir = new TestDirectory();
        var lib = CaptureLibrary.Open(dir.Sub("lib"));
        var img = PixelBufferTests.Gradient(12, 12);
        CaptureRecord rec = lib.Create(new CaptureContent(img, [RedactionMaskAnnotation.For(new PixelRect(1, 1, 3, 3), Imaging.ArgbColor.Black)]), img, DateTime.UtcNow);
        string pkg = dir.Sub("x.sfcap");
        lib.ExportPackage(rec.Id, pkg);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(pkg))
        {
            Assert.Equal(CaptureFormat.ContentFiles.Order(StringComparer.Ordinal), zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
            using var s = zip.GetEntry(CaptureFormat.ManifestFile)!.Open();
            Assert.Equal(1, System.Text.Json.Nodes.JsonNode.Parse(s)!["version"]!.GetValue<int>());
        }

        CaptureRecord imported = lib.ImportPackage(pkg);
        Assert.Equal(img.Pixels, lib.Load(imported.Id).BaseImage.Pixels);
    }
}
