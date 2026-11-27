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
/// The editable capture format (documented in docs/CAPTURE-FORMAT.md). A capture is a folder
/// <c>captures/&lt;id&gt;/</c> containing:
/// <list type="bullet">
/// <item><c>manifest.json</c> — format id/version, dimensions, timestamps and, in version 2, the exact file name,
/// SHA-256 and size of each content file of the committed generation. The manifest is the single commit point.</item>
/// <item><c>base.&lt;gen&gt;.sfpx</c> — the base pixels (<see cref="PixelCodec"/>).</item>
/// <item><c>annotations.&lt;gen&gt;.json</c> — annotations (<see cref="AnnotationSerializer"/>).</item>
/// <item><c>thumb.&lt;gen&gt;.sfpx</c> — cached thumbnail of the flattened image.</item>
/// <item><c>meta.json</c> — user metadata (title, tags, folder, favorite, trash state), replaced atomically on its own.</item>
/// </list>
/// Version 2 (FA-015) writes every save as a new generation of uniquely named files, flushes them, then atomically
/// replaces the manifest; files the manifest does not reference are uncommitted or superseded and are overwritten
/// and deleted. Version 1 folders (fixed names <see cref="BaseFile"/>, <see cref="AnnotationsFile"/>,
/// <see cref="ThumbFile"/>) are still read and are upgraded on their next save; an unchanged v1 base may stay
/// referenced under its old name. A portable package (<c>.sfcap</c>) is a ZIP in the v1 layout, so it opens in every
/// SnapForge version.
/// </summary>
public static class CaptureFormat
{
    public const string FormatId = "snapforge.capture";

    /// <summary>Capture manifest version written by this build (2 = generation-based commits).</summary>
    public const int CurrentVersion = 2;

    /// <summary>Version of the annotations file (unchanged by the generation-based layout).</summary>
    public const int AnnotationsVersion = 1;

    public const string ManifestFile = "manifest.json";

    /// <summary>Fixed content file names of format version 1 (and of .sfcap packages).</summary>
    public const string BaseFile = "base.sfpx";
    public const string AnnotationsFile = "annotations.json";
    public const string ThumbFile = "thumb.sfpx";
    public const string MetaFile = "meta.json";
    public const string PackageExtension = ".sfcap";

    /// <summary>Left by an interrupted sanitize of SnapForge 1.0 RC builds (format v1); recovered on open.</summary>
    internal const string LegacySanitizedBaseFile = "base.sanitized.sfpx";

    /// <summary>The files of a version 1 capture folder and of a .sfcap package.</summary>
    public static readonly string[] ContentFiles = [ManifestFile, BaseFile, AnnotationsFile, MetaFile, ThumbFile];

    /// <summary>File name of a generation's base image, e.g. <c>base.3.sfpx</c>.</summary>
    public static string GenerationBaseFile(int generation) => $"base.{generation.ToString(CultureInfo.InvariantCulture)}.sfpx";

    /// <summary>File name of a generation's annotations, e.g. <c>annotations.3.json</c>.</summary>
    public static string GenerationAnnotationsFile(int generation) => $"annotations.{generation.ToString(CultureInfo.InvariantCulture)}.json";

    /// <summary>File name of a generation's thumbnail, e.g. <c>thumb.3.sfpx</c>.</summary>
    public static string GenerationThumbFile(int generation) => $"thumb.{generation.ToString(CultureInfo.InvariantCulture)}.sfpx";

    public static byte[] SerializeAnnotations(IEnumerable<Annotation> annotations) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new JsonObject { ["format"] = FormatId + ".annotations", ["version"] = AnnotationsVersion, ["annotations"] = AnnotationSerializer.ToJson(annotations) },
            AnnotationSerializer.Indented);

    public static List<Annotation> DeserializeAnnotations(byte[] json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("annotations.json is not an object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The annotations file is damaged (not valid JSON).", ex);
        }

        CheckVersion(root, FormatId + ".annotations", AnnotationsVersion);
        return AnnotationSerializer.FromJson(root["annotations"] as JsonArray ?? throw new InvalidDataException("Missing annotations."));
    }

    /// <summary>Checks format id and version (capture manifests up to <see cref="CurrentVersion"/>, other files up to 1).</summary>
    public static void CheckVersion(JsonObject root, string expectedFormat) =>
        CheckVersion(root, expectedFormat, expectedFormat == FormatId ? CurrentVersion : AnnotationsVersion);

    public static void CheckVersion(JsonObject root, string expectedFormat, int maxVersion)
    {
        ArgumentNullException.ThrowIfNull(root);
        string? format;
        int version;
        try
        {
            format = root["format"]?.GetValue<string>();
            version = root["version"]?.GetValue<int>() ?? 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The format header is malformed.", ex);
        }

        if (format != expectedFormat)
        {
            throw new InvalidDataException($"Not a {expectedFormat} file (format '{format}').");
        }

        if (version < 1 || version > maxVersion)
        {
            throw new InvalidDataException($"Unsupported {expectedFormat} version {version} (this SnapForge reads up to {maxVersion}).");
        }
    }

    /// <summary>
    /// Packs a capture folder (format v1 or v2) into a single .sfcap file (atomic write) in the version 1 layout, so
    /// any SnapForge version can import it. The files are verified against the manifest first.
    /// </summary>
    public static void WritePackage(string captureFolder, string packagePath)
    {
        IReadOnlyList<(string Name, byte[] Data)> files = LegacyLayout(captureFolder);
        AtomicFile.Write(packagePath, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach ((string name, byte[] data) in files)
            {
                using Stream s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                s.Write(data);
            }
        });
    }

    /// <summary>
    /// The files of a capture folder in the version 1 layout (fixed names, v1 manifest), verified against the
    /// folder's manifest. Used for .sfcap packages and legacy (v1) backups.
    /// </summary>
    internal static IReadOnlyList<(string Name, byte[] Data)> LegacyLayout(string captureFolder)
    {
        CaptureManifest m = CaptureManifest.Read(captureFolder);
        byte[] baseBytes = ReadVerified(captureFolder, m.Base);
        byte[] annBytes = ReadVerified(captureFolder, m.Annotations);
        var manifest = (JsonObject)m.Json.DeepClone();
        manifest.Remove("generation");
        manifest.Remove("files");
        manifest["version"] = 1;
        manifest["baseSha256"] = AtomicFile.Sha256Hex(baseBytes);
        manifest["annotationsSha256"] = AtomicFile.Sha256Hex(annBytes);
        var list = new List<(string, byte[])>
        {
            (ManifestFile, JsonSerializer.SerializeToUtf8Bytes(manifest, AnnotationSerializer.Indented)),
            (BaseFile, baseBytes),
            (AnnotationsFile, annBytes),
        };
        string meta = Path.Combine(captureFolder, MetaFile);
        if (File.Exists(meta))
        {
            list.Add((MetaFile, File.ReadAllBytes(meta)));
        }

        if (m.Thumb is not null && File.Exists(Path.Combine(captureFolder, m.Thumb.Name)))
        {
            list.Add((ThumbFile, File.ReadAllBytes(Path.Combine(captureFolder, m.Thumb.Name))));
        }

        return list;
    }

    /// <summary>Reads a referenced content file, checking its size (v2) before reading and its SHA-256 after.</summary>
    internal static byte[] ReadVerified(string dir, CaptureFileRef file)
    {
        string path = Path.Combine(dir, file.Name);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{file.Name} is missing.");
        }

        if (file.Bytes is long expected && new FileInfo(path).Length != expected)
        {
            throw new InvalidDataException($"{file.Name} failed its integrity check (its size changed: the file is damaged or was modified).");
        }

        byte[] data = File.ReadAllBytes(path);
        if (file.Sha256 is null || !string.Equals(file.Sha256, AtomicFile.Sha256Hex(data), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{file.Name} failed its integrity check (the file is damaged or was modified).");
        }

        return data;
    }

    /// <summary>
    /// Extracts a .sfcap package into <paramref name="targetFolder"/>. Only capture file names are accepted
    /// (no paths), protecting against "zip slip" and unexpected content; each entry is read with a size cap.
    /// </summary>
    public static void ExtractPackage(string packagePath, string targetFolder)
    {
        const long maxEntryBytes = 2L * 1024 * 1024 * 1024;
        using ZipArchive zip = ZipFile.OpenRead(packagePath);
        Directory.CreateDirectory(targetFolder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!CaptureManifest.IsCaptureFileName(entry.FullName) || !seen.Add(entry.FullName))
            {
                throw new InvalidDataException($"Unexpected entry '{entry.FullName}' in capture package.");
            }

            if (entry.Length > maxEntryBytes)
            {
                throw new InvalidDataException($"Entry '{entry.FullName}' is too large.");
            }

            using Stream source = entry.Open();
            AtomicFile.Write(Path.Combine(targetFolder, entry.FullName), target =>
            {
                if (AtomicFile.CopyBounded(source, target, null, maxEntryBytes, null, CancellationToken.None) > maxEntryBytes)
                {
                    throw new InvalidDataException($"Entry '{entry.FullName}' is too large.");
                }
            });
        }
    }

    internal static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    internal static DateTime ParseIso(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.MinValue;
}
