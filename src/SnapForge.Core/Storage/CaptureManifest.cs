using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SnapForge.Core.Storage;

/// <summary>A content file referenced by a capture manifest: exact name, SHA-256 and (format v2) size.</summary>
internal sealed record CaptureFileRef(string Name, string? Sha256, long? Bytes);

/// <summary>
/// A parsed capture manifest (format v1 or v2). In v1 the content files have fixed names and the thumbnail is not
/// hashed; in v2 the manifest names each file of the committed generation with its hash and size, so it is the single
/// commit point (FA-015).
/// </summary>
internal sealed partial class CaptureManifest
{
    private CaptureManifest(JsonObject json, int version, int generation, CaptureFileRef @base, CaptureFileRef annotations, CaptureFileRef? thumb)
    {
        Json = json;
        Version = version;
        Generation = generation;
        Base = @base;
        Annotations = annotations;
        Thumb = thumb;
    }

    public JsonObject Json { get; }

    public int Version { get; }

    /// <summary>Generation number (0 for v1 folders).</summary>
    public int Generation { get; }

    public CaptureFileRef Base { get; }

    public CaptureFileRef Annotations { get; }

    /// <summary>The thumbnail (v1: <c>thumb.sfpx</c> if present, unhashed; v2: optional, hashed).</summary>
    public CaptureFileRef? Thumb { get; }

    public DateTime CreatedUtc => CaptureFormat.ParseIso(Json["createdUtc"]?.GetValue<string>());

    public DateTime? SanitizedUtc => Json["sanitizedUtc"]?.GetValue<string>() is { } s ? CaptureFormat.ParseIso(s) : null;

    public int Width => Json["width"]?.GetValue<int>() ?? 0;

    public int Height => Json["height"]?.GetValue<int>() ?? 0;

    /// <summary>Every file name this manifest keeps alive in its folder (manifest and meta included).</summary>
    public HashSet<string> ReferencedFiles()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CaptureFormat.ManifestFile, CaptureFormat.MetaFile, Base.Name, Annotations.Name };
        if (Thumb is not null)
        {
            set.Add(Thumb.Name);
        }

        return set;
    }

    public static CaptureManifest Read(string dir, bool requireThumbFile = false)
    {
        CaptureManifest m = Parse(File.ReadAllBytes(Path.Combine(dir, CaptureFormat.ManifestFile)));
        if (m.Version == 1 && !requireThumbFile && m.Thumb is not null && !File.Exists(Path.Combine(dir, m.Thumb.Name)))
        {
            return new CaptureManifest(m.Json, m.Version, m.Generation, m.Base, m.Annotations, null);
        }

        return m;
    }

    public static CaptureManifest Parse(byte[] bytes)
    {
        JsonObject m;
        try
        {
            m = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Manifest is not an object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The manifest is damaged (not valid JSON).", ex);
        }

        CaptureFormat.CheckVersion(m, CaptureFormat.FormatId, CaptureFormat.CurrentVersion);
        int version = m["version"]!.GetValue<int>();
        if (version == 1)
        {
            return new CaptureManifest(
                m,
                1,
                0,
                new CaptureFileRef(CaptureFormat.BaseFile, m["baseSha256"]?.GetValue<string>(), null),
                new CaptureFileRef(CaptureFormat.AnnotationsFile, m["annotationsSha256"]?.GetValue<string>(), null),
                new CaptureFileRef(CaptureFormat.ThumbFile, null, null));
        }

        int generation = m["generation"]?.GetValue<int>() ?? throw new InvalidDataException("The manifest has no generation.");
        if (generation < 1)
        {
            throw new InvalidDataException($"Invalid generation {generation}.");
        }

        JsonObject files = m["files"] as JsonObject ?? throw new InvalidDataException("The manifest lists no files.");
        return new CaptureManifest(
            m,
            version,
            generation,
            ParseRef(files, "base", BasePattern()) ?? throw new InvalidDataException("The manifest has no base image."),
            ParseRef(files, "annotations", AnnotationsPattern()) ?? throw new InvalidDataException("The manifest has no annotations."),
            ParseRef(files, "thumb", ThumbPattern()));
    }

    /// <summary>Writes the "files" object of a v2 manifest.</summary>
    public static JsonObject FilesJson(CaptureFileRef @base, CaptureFileRef annotations, CaptureFileRef? thumb)
    {
        var files = new JsonObject { ["base"] = RefJson(@base), ["annotations"] = RefJson(annotations) };
        if (thumb is not null)
        {
            files["thumb"] = RefJson(thumb);
        }

        return files;
    }

    /// <summary>
    /// True for names of capture content files of any generation (and their temporary or interrupted-sanitize
    /// forms). Such files in a capture folder that the manifest does not reference are uncommitted or superseded
    /// leftovers and are securely deleted. Other (unknown) files are never touched.
    /// </summary>
    public static bool IsContentFileName(string name) =>
        BasePattern().IsMatch(name) || AnnotationsPattern().IsMatch(name) || ThumbPattern().IsMatch(name)
        || string.Equals(name, CaptureFormat.LegacySanitizedBaseFile, StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for every file name a capture folder may legitimately contain (used to validate archives).</summary>
    public static bool IsCaptureFileName(string name) =>
        BasePattern().IsMatch(name) || AnnotationsPattern().IsMatch(name) || ThumbPattern().IsMatch(name)
        || name is CaptureFormat.ManifestFile or CaptureFormat.MetaFile;

    private static CaptureFileRef? ParseRef(JsonObject files, string key, Regex pattern)
    {
        if (files[key] is not JsonObject o)
        {
            return null;
        }

        string name = o["name"]?.GetValue<string>() ?? throw new InvalidDataException($"The manifest entry '{key}' has no name.");
        if (!pattern.IsMatch(name))
        {
            throw new InvalidDataException($"The manifest entry '{key}' has an invalid file name.");
        }

        string sha = o["sha256"]?.GetValue<string>() ?? throw new InvalidDataException($"The manifest entry '{key}' has no hash.");
        if (!Sha256Pattern().IsMatch(sha))
        {
            throw new InvalidDataException($"The manifest entry '{key}' has an invalid hash.");
        }

        long bytes = o["bytes"]?.GetValue<long>() ?? throw new InvalidDataException($"The manifest entry '{key}' has no size.");
        return bytes < 0 ? throw new InvalidDataException($"The manifest entry '{key}' has an invalid size.") : new CaptureFileRef(name, sha, bytes);
    }

    private static JsonObject RefJson(CaptureFileRef r) => new() { ["name"] = r.Name, ["sha256"] = r.Sha256, ["bytes"] = r.Bytes };

    [GeneratedRegex(@"^base(\.[0-9]{1,9})?\.sfpx$", RegexOptions.CultureInvariant)]
    private static partial Regex BasePattern();

    [GeneratedRegex(@"^annotations(\.[0-9]{1,9})?\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex AnnotationsPattern();

    [GeneratedRegex(@"^thumb(\.[0-9]{1,9})?\.sfpx$", RegexOptions.CultureInvariant)]
    private static partial Regex ThumbPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    internal static partial Regex Sha256Pattern();
}
