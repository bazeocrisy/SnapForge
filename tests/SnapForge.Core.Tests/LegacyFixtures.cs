using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>
/// The committed legacy (capture format v1, backup v1, settings v1) fixtures under <c>tests/fixtures/legacy-v1-library</c>.
/// They were produced by <see cref="LegacyFixtureGenerator"/> running against release/v1.0-candidate@2faecf8, before the
/// formats changed. Tests always work on a copy (opening a library may upgrade or clean it).
/// </summary>
internal static class LegacyFixtures
{
    /// <summary>The "secret" pixels hidden under the redaction of the redacted fixture capture (B, G, R order on disk).</summary>
    internal static readonly ArgbColor[] SecretPattern =
    [
        ArgbColor.FromRgb(0x12, 0xFE, 0x34),
        ArgbColor.FromRgb(0x56, 0x07, 0x9A),
        ArgbColor.FromRgb(0xBC, 0xDE, 0x0F),
    ];

    internal static string RepositoryRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "SnapForge.sln")))
            {
                return d.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root (SnapForge.sln) not found.");
    }

    internal static string FixtureRoot => Path.Combine(RepositoryRoot(), "tests", "fixtures", "legacy-v1-library");

    /// <summary>Copies the fixture set into <paramref name="target"/> and returns the fixture index (roles → ids).</summary>
    internal static JsonObject CopyTo(string target)
    {
        CopyDirectory(FixtureRoot, target);
        return (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(target, "fixture-index.json")))!;
    }

    internal static Guid Id(JsonObject index, string role) => Guid.Parse(index[role]!.GetValue<string>());

    internal static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    /// <summary>A 64×48 image with <see cref="SecretPattern"/> repeated over the block (8,8)–(40,24).</summary>
    internal static PixelBuffer SecretImage()
    {
        PixelBuffer img = PixelBufferTests.Gradient(64, 48);
        for (int y = 8; y < 24; y++)
        {
            for (int x = 8; x < 40; x++)
            {
                img.SetPixel(x, y, SecretPattern[(x - 8) % SecretPattern.Length]);
            }
        }

        return img;
    }

    internal static PixelRect SecretRegion => new(6, 6, 38, 20);
}

/// <summary>
/// STEP 0 of the data-integrity fixes: writes the legacy fixtures with the code as it was (capture format v1).
/// It only runs when SNAPFORGE_GENERATE_LEGACY_FIXTURES=1 and the code still writes capture format v1, so it can never
/// regenerate the committed fixtures with a newer format.
/// </summary>
public class LegacyFixtureGenerator
{
    [Fact]
    public void Generate_legacy_v1_fixtures_when_requested()
    {
        if (Environment.GetEnvironmentVariable("SNAPFORGE_GENERATE_LEGACY_FIXTURES") != "1" || CaptureFormat.CurrentVersion != 1)
        {
            return;
        }

        string root = LegacyFixtures.FixtureRoot;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        string libRoot = Path.Combine(root, "library");
        var created = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);
        var lib = CaptureLibrary.Open(libRoot);
        var index = new JsonObject { ["generatedFrom"] = "release/v1.0-candidate@2faecf8 (capture format v1, backup v1, settings v1)" };

        // 1. Redacted capture: secret pixels under a redaction, a text annotation hidden under it, a visible arrow.
        var doc = new EditorDocument(LegacyFixtures.SecretImage());
        doc.Execute(new AddAnnotationCommand(new TextAnnotation(new PointD(10, 10), "hunter2-secret", 10) { MeasuredSize = new PointD(28, 10) }));
        doc.Execute(new RedactCommand(LegacyFixtures.SecretRegion, ArgbColor.Black));
        doc.Execute(new AddAnnotationCommand(new ArrowAnnotation(new PointD(45, 30), new PointD(60, 44))));
        CaptureRecord redacted = lib.Create(new CaptureContent(doc.BaseImage, doc.Annotations), doc.Image, created, "Redacted login");
        index["redacted"] = redacted.Id.ToString("D");

        // 2. Organised capture: tags, folder, favourite, all annotation kinds.
        PixelBuffer img2 = PixelBufferTests.Gradient(48, 32);
        CaptureRecord organised = lib.Create(new CaptureContent(img2, CaptureFormatTests.AllKinds()), img2, created.AddMinutes(1), "Organised");
        lib.SetTags(organised.Id, ["bug", "ui"]);
        lib.SetFolder(organised.Id, "Project X");
        lib.SetFavorite(organised.Id, true);
        index["organised"] = organised.Id.ToString("D");

        // 3. Capture in the Trash.
        PixelBuffer img3 = PixelBufferTests.Gradient(32, 24);
        CaptureRecord trashed = lib.Create(new CaptureContent(img3, [new LineAnnotation(new PointD(1, 1), new PointD(20, 20))]), img3, created.AddMinutes(2), "Trashed");
        lib.MoveToTrash([trashed.Id], created.AddDays(1));
        index["trashed"] = trashed.Id.ToString("D");

        // 4. Video item (existing video-folder format) with a tiny MP4.
        (Guid vid, string recording) = lib.BeginVideo(created.AddMinutes(3));
        File.WriteAllBytes(recording, Mp4DefragmenterTests.SampleFragmentedFile());
        lib.CompleteVideo(vid, new VideoInfo(64, 36, 30, 1000, HasAudio: false), PixelBufferTests.Gradient(64, 36), created.AddMinutes(3), "Legacy video");
        lib.SetTags(vid, ["demo"]);
        index["video"] = vid.ToString("D");

        // 5. Damaged capture: its manifest was truncated.
        PixelBuffer img5 = PixelBufferTests.Gradient(16, 16);
        CaptureRecord damaged = lib.Create(new CaptureContent(img5, []), img5, created.AddMinutes(4), "Damaged");
        string manifest = Path.Combine(lib.CapturesDirectory, damaged.Id.ToString("D"), CaptureFormat.ManifestFile);
        byte[] bytes = File.ReadAllBytes(manifest);
        File.WriteAllBytes(manifest, bytes[..(bytes.Length / 2)]);
        index["damaged"] = damaged.Id.ToString("D");

        // v1 backup (images only; the damaged capture is skipped by the v1 code because it is listed as damaged).
        lib = CaptureLibrary.Open(libRoot);
        int backedUp = lib.Backup(Path.Combine(root, "backup-v1.zip"));
        index["backupV1Count"] = backedUp;

        // Legacy settings file (version 1) with non-default values.
        new AppSettings
        {
            LibraryPath = @"D:\Shots",
            Theme = ThemePreference.Dark,
            CaptureHotkey = HotkeyGesture.Parse("Ctrl+Shift+S"),
            LastTool = AnnotationTool.Highlight,
            LibraryThumbnailSize = 240,
        }.Save(Path.Combine(root, "settings.json"));

        File.WriteAllText(Path.Combine(root, "fixture-index.json"), index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
