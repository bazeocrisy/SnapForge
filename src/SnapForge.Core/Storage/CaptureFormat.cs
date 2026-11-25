using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Storage;

/// <summary>Content of an editable capture: unredacted base pixels plus all annotations (incl. redaction masks).</summary>
public sealed record CaptureContent(PixelBuffer BaseImage, IReadOnlyList<Annotation> Annotations);

/// <summary>
/// The editable capture format, version 1 (documented in docs/CAPTURE-FORMAT.md).
/// A capture is a folder <c>captures/&lt;id&gt;/</c> containing:
/// <list type="bullet">
/// <item><c>manifest.json</c> — format id/version, dimensions, timestamps, SHA-256 of the other content files.</item>
/// <item><c>base.sfpx</c> — the base pixels (<see cref="PixelCodec"/>).</item>
/// <item><c>annotations.json</c> — annotations (<see cref="AnnotationSerializer"/>).</item>
/// <item><c>meta.json</c> — user metadata (title, tags, folder, favorite, trash state).</item>
/// <item><c>thumb.sfpx</c> — cached thumbnail of the flattened image (regenerable).</item>
/// </list>
/// A portable single-file package (<c>.sfcap</c>) is a ZIP of the same files.
/// </summary>
public static class CaptureFormat
{
    public const string FormatId = "snapforge.capture";
    public const int CurrentVersion = 1;
    public const string ManifestFile = "manifest.json";
    public const string BaseFile = "base.sfpx";
    public const string AnnotationsFile = "annotations.json";
    public const string MetaFile = "meta.json";
    public const string ThumbFile = "thumb.sfpx";
    public const string PackageExtension = ".sfcap";

    public static readonly string[] ContentFiles = [ManifestFile, BaseFile, AnnotationsFile, MetaFile, ThumbFile];

    public static byte[] SerializeAnnotations(IEnumerable<Annotation> annotations) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new JsonObject { ["format"] = FormatId + ".annotations", ["version"] = CurrentVersion, ["annotations"] = AnnotationSerializer.ToJson(annotations) },
            AnnotationSerializer.Indented);

    public static List<Annotation> DeserializeAnnotations(byte[] json)
    {
        JsonObject root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("annotations.json is not an object.");
        CheckVersion(root, FormatId + ".annotations");
        return AnnotationSerializer.FromJson(root["annotations"] as JsonArray ?? throw new InvalidDataException("Missing annotations."));
    }

    public static void CheckVersion(JsonObject root, string expectedFormat)
    {
        string? format = root["format"]?.GetValue<string>();
        int version = root["version"]?.GetValue<int>() ?? 0;
        if (format != expectedFormat)
        {
            throw new InvalidDataException($"Not a {expectedFormat} file (format '{format}').");
        }

        if (version < 1 || version > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported {expectedFormat} version {version} (this SnapForge reads up to {CurrentVersion}).");
        }
    }

    /// <summary>Packs a capture folder into a single .sfcap file (atomic write).</summary>
    public static void WritePackage(string captureFolder, string packagePath)
    {
        AtomicFile.Write(packagePath, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach (string name in ContentFiles)
            {
                string file = Path.Combine(captureFolder, name);
                if (File.Exists(file))
                {
                    zip.CreateEntryFromFile(file, name, CompressionLevel.NoCompression);
                }
            }
        });
    }

    /// <summary>
    /// Extracts a .sfcap package into <paramref name="targetFolder"/>. Only the known file names are accepted
    /// (no paths), protecting against "zip slip" and unexpected content.
    /// </summary>
    public static void ExtractPackage(string packagePath, string targetFolder)
    {
        using ZipArchive zip = ZipFile.OpenRead(packagePath);
        Directory.CreateDirectory(targetFolder);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!ContentFiles.Contains(entry.FullName, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unexpected entry '{entry.FullName}' in capture package.");
            }

            if (entry.Length > 4L * 1024 * 1024 * 1024)
            {
                throw new InvalidDataException($"Entry '{entry.FullName}' is too large.");
            }

            using Stream source = entry.Open();
            AtomicFile.Write(Path.Combine(targetFolder, entry.FullName), source.CopyTo);
        }
    }

    internal static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    internal static DateTime ParseIso(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.MinValue;
}
