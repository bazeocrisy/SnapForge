using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Imaging;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Library;

public sealed record StorageUsage(int CaptureCount, int TrashCount, long TotalBytes, long TrashBytes, string Root);

public sealed record RetentionPolicy
{
    /// <summary>Captures in the Trash longer than this are deleted permanently (null = never).</summary>
    public int? EmptyTrashAfterDays { get; init; } = 30;

    /// <summary>Non-favorite captures older than this are moved to the Trash (null = keep forever, the default).</summary>
    public int? MoveToTrashAfterDays { get; init; }
}

public sealed class LibraryChangedEventArgs(IReadOnlyCollection<Guid> ids) : EventArgs
{
    public IReadOnlyCollection<Guid> Ids { get; } = ids;
}

/// <summary>
/// The local, persistent Capture Library. Each capture is a folder of small files (see <see cref="CaptureFormat"/>);
/// every write is atomic, so a crash or full disk never corrupts an existing capture. Nothing here uses the network.
/// Thread-safe: saves may run on background threads.
/// </summary>
public sealed partial class CaptureLibrary
{
    public const int ThumbnailSize = 320;
    private const string SanitizedBaseFile = "base.sanitized.sfpx";
    private readonly object _gate = new();
    private readonly Dictionary<Guid, CaptureRecord> _records = [];

    private CaptureLibrary(string root)
    {
        Root = Path.GetFullPath(root);
        CapturesDirectory = Path.Combine(Root, "captures");
        VideosDirectory = Path.Combine(Root, VideosFolderName);
    }

    public event EventHandler<LibraryChangedEventArgs>? Changed;

    public string Root { get; }

    public string CapturesDirectory { get; }

    public IReadOnlyList<CaptureRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records.Values];
            }
        }
    }

    /// <summary>Opens (creating if necessary) the library at <paramref name="root"/> and indexes it.</summary>
    public static CaptureLibrary Open(string root)
    {
        CaptureLibrary library = OpenDeferred(root);
        library.CompleteOpen();
        return library;
    }

    /// <summary>
    /// Opens the library without reading any capture yet, so a UI can appear immediately. Call
    /// <see cref="CompleteOpen"/> on a background thread; until it finishes <see cref="IsIndexed"/> is false and
    /// <see cref="Records"/> may be incomplete. Writes made meanwhile are kept (see <see cref="Reindex"/>).
    /// </summary>
    public static CaptureLibrary OpenDeferred(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var library = new CaptureLibrary(root);
        Directory.CreateDirectory(library.CapturesDirectory);
        Directory.CreateDirectory(library.VideosDirectory);
        return library;
    }

    /// <summary>True once the first full index has finished.</summary>
    public bool IsIndexed { get; private set; }

    /// <summary>Raised once, after the first full index (on the indexing thread).</summary>
    public event EventHandler? Indexed;

    /// <summary>Removes leftovers of interrupted writes and builds the index. Safe to call from a background thread.</summary>
    public void CompleteOpen()
    {
        AtomicFile.CleanupTemporaryFiles(Root);
        Reindex();
        IsIndexed = true;
        Indexed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True if a capture folder with a manifest exists on disk (usable before indexing finishes).</summary>
    public bool ExistsOnDisk(Guid id) =>
        File.Exists(Path.Combine(CaptureFolder(id), CaptureFormat.ManifestFile)) || File.Exists(Path.Combine(VideoFolder(id), VideoManifestFile));

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "Library");

    public CaptureRecord? Find(Guid id)
    {
        lock (_gate)
        {
            return _records.GetValueOrDefault(id);
        }
    }

    /// <summary>Re-reads every capture folder (used at start-up and after restore/import).</summary>
    public void Reindex()
    {
        var found = new Dictionary<Guid, CaptureRecord>();
        foreach (string dir in Directory.EnumerateDirectories(CapturesDirectory))
        {
            if (Guid.TryParse(Path.GetFileName(dir), out Guid id))
            {
                CompleteInterruptedSanitize(dir);
                if (File.Exists(Path.Combine(dir, CaptureFormat.ManifestFile)))
                {
                    found[id] = ReadRecord(id, dir);
                }
            }
        }

        IndexVideos(found);

        lock (_gate)
        {
            // Keep records written while this scan was running (their folders may have been created after the
            // directory enumeration passed them); drop only records whose folders are really gone.
            foreach (var pair in _records)
            {
                if (!found.ContainsKey(pair.Key) && Directory.Exists(FolderOf(pair.Key)))
                {
                    found[pair.Key] = pair.Value;
                }
            }

            _records.Clear();
            foreach (var pair in found)
            {
                _records[pair.Key] = pair.Value;
            }
        }

        RaiseChanged([.. found.Keys]);
    }

    /// <summary>Stores a new capture. <paramref name="flattened"/> is the rendered image used for the thumbnail.</summary>
    public CaptureRecord Create(CaptureContent content, PixelBuffer flattened, DateTime createdUtc, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Guid id = Guid.NewGuid();
        string dir = FolderOf(id);
        Directory.CreateDirectory(dir);
        var meta = new CaptureMetadata { Title = title ?? DefaultTitle(createdUtc) };
        WriteMeta(dir, meta);
        bool reversible = content.Annotations.OfType<RedactionMaskAnnotation>().Any();
        WriteContent(id, dir, content.BaseImage, content.Annotations, flattened, createdUtc, sanitizedUtc: null, reversible);
        return Refresh(id);
    }

    /// <summary>
    /// Saves edits. Pass <paramref name="newBase"/> only when the base pixels changed (crop, sanitize); otherwise the
    /// stored base file is kept and only the small files are rewritten.
    /// </summary>
    /// <param name="hasReversibleRedactions">Whether the stored base still holds original pixels under a redaction
    /// (<see cref="Editing.EditorDocument.HasReversibleRedactions"/>).</param>
    public CaptureRecord SaveContent(Guid id, PixelBuffer? newBase, IReadOnlyList<Annotation> annotations, PixelBuffer flattened, bool hasReversibleRedactions)
    {
        RequireImage(id);
        string dir = ExistingFolder(id);
        JsonObject manifest = ReadManifest(dir);
        DateTime created = CaptureFormat.ParseIso(manifest["createdUtc"]?.GetValue<string>());
        DateTime? sanitized = manifest["sanitizedUtc"]?.GetValue<string>() is { } s ? CaptureFormat.ParseIso(s) : null;
        WriteContent(id, dir, newBase, annotations, flattened, created, hasReversibleRedactions ? null : sanitized, hasReversibleRedactions, manifest);
        return Refresh(id);
    }

    /// <summary>
    /// Destructive sanitization of the stored copy: replaces the base image with <paramref name="sanitizedBase"/>
    /// (redactions burned in), overwriting the previous base and thumbnail files with zeros before removing them.
    /// Crash-safe: an interrupted sanitize is completed on the next start.
    /// </summary>
    public CaptureRecord Sanitize(Guid id, PixelBuffer sanitizedBase, IReadOnlyList<Annotation> annotations, PixelBuffer flattened)
    {
        RequireImage(id);
        ArgumentNullException.ThrowIfNull(sanitizedBase);
        string dir = ExistingFolder(id);
        JsonObject manifest = ReadManifest(dir);
        byte[] baseBytes = PixelCodec.Encode(sanitizedBase);
        AtomicFile.WriteAllBytes(Path.Combine(dir, SanitizedBaseFile), baseBytes);
        AtomicFile.OverwriteAndDelete(Path.Combine(dir, CaptureFormat.ThumbFile));
        AtomicFile.OverwriteAndDelete(Path.Combine(dir, CaptureFormat.BaseFile));
        File.Move(Path.Combine(dir, SanitizedBaseFile), Path.Combine(dir, CaptureFormat.BaseFile));
        DateTime created = CaptureFormat.ParseIso(manifest["createdUtc"]?.GetValue<string>());
        manifest["baseSha256"] = AtomicFile.Sha256Hex(baseBytes);
        WriteContent(id, dir, newBase: null, annotations, flattened, created, DateTime.UtcNow, reversibleRedactions: false, manifest);
        AtomicFile.CleanupTemporaryFiles(dir);
        return Refresh(id);
    }

    public CaptureContent Load(Guid id)
    {
        RequireImage(id);
        string dir = ExistingFolder(id);
        JsonObject manifest = ReadManifest(dir);
        byte[] baseBytes = File.ReadAllBytes(Path.Combine(dir, CaptureFormat.BaseFile));
        byte[] annBytes = File.ReadAllBytes(Path.Combine(dir, CaptureFormat.AnnotationsFile));
        Verify(manifest, "baseSha256", baseBytes, CaptureFormat.BaseFile);
        Verify(manifest, "annotationsSha256", annBytes, CaptureFormat.AnnotationsFile);
        return new CaptureContent(PixelCodec.Decode(baseBytes), CaptureFormat.DeserializeAnnotations(annBytes));
    }

    public PixelBuffer? LoadThumbnail(Guid id)
    {
        string file = Path.Combine(FolderOf(id), CaptureFormat.ThumbFile);
        try
        {
            return File.Exists(file) ? PixelCodec.Decode(File.ReadAllBytes(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public CaptureRecord UpdateMetadata(Guid id, Func<CaptureMetadata, CaptureMetadata> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        string dir = ExistingFolder(id);
        CaptureMetadata meta = update(ReadMeta(dir));
        WriteMeta(dir, meta with { Tags = NormalizeTags(meta.Tags), Folder = meta.Folder.Trim(), Title = meta.Title.Trim() });
        return Refresh(id);
    }

    public CaptureRecord Rename(Guid id, string title)
    {
        string t = title?.Trim() ?? string.Empty;
        if (t.Length == 0)
        {
            throw new ArgumentException("Title must not be empty.", nameof(title));
        }

        return UpdateMetadata(id, m => m with { Title = t });
    }

    public CaptureRecord SetFavorite(Guid id, bool favorite) => UpdateMetadata(id, m => m with { Favorite = favorite });

    public CaptureRecord SetTags(Guid id, IEnumerable<string> tags) => UpdateMetadata(id, m => m with { Tags = [.. tags] });

    public CaptureRecord SetFolder(Guid id, string folder) => UpdateMetadata(id, m => m with { Folder = folder ?? string.Empty });

    public CaptureRecord MarkOpened(Guid id, DateTime utc) => UpdateMetadata(id, m => m with { LastOpenedUtc = utc });

    public void MoveToTrash(IEnumerable<Guid> ids, DateTime utc)
    {
        foreach (Guid id in ids.ToArray())
        {
            UpdateMetadata(id, m => m with { DeletedUtc = utc });
        }
    }

    public void Restore(IEnumerable<Guid> ids)
    {
        foreach (Guid id in ids.ToArray())
        {
            UpdateMetadata(id, m => m with { DeletedUtc = null });
        }
    }

    /// <summary>Permanently deletes captures: every file is overwritten with zeros, then removed (best effort; see docs).</summary>
    public void DeletePermanently(IEnumerable<Guid> ids)
    {
        Guid[] list = ids.ToArray();
        foreach (Guid id in list)
        {
            string dir = FolderOf(id);
            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.EnumerateFiles(dir))
                {
                    DeleteLibraryFile(file);
                }

                Directory.Delete(dir, recursive: true);
            }

            lock (_gate)
            {
                _records.Remove(id);
            }
        }

        RaiseChanged(list);
    }

    public int EmptyTrash()
    {
        Guid[] trash = Records.Where(r => r.IsDeleted).Select(r => r.Id).ToArray();
        DeletePermanently(trash);
        return trash.Length;
    }

    public CaptureRecord Duplicate(Guid id, DateTime utc)
    {
        CaptureRecord source = Find(id) ?? throw new KeyNotFoundException($"Capture {id} not found.");
        RequireImage(id);
        CaptureContent content = Load(id);
        PixelBuffer thumb = LoadThumbnail(id) ?? Thumbnailer.Create(content.BaseImage, ThumbnailSize);
        var copies = content.Annotations.Select(a => a with { Id = Guid.NewGuid() }).ToList();
        CaptureRecord copy = Create(new CaptureContent(content.BaseImage, copies), thumb, utc, source.Title + " (copy)");
        return UpdateMetadata(copy.Id, m => m with { Tags = source.Meta.Tags, Folder = source.Meta.Folder });
    }

    public IReadOnlyList<CaptureRecord> Query(LibraryQuery query, DateTime nowUtc) => [.. query.Apply(Records, nowUtc)];

    public IReadOnlyList<string> AllTags() =>
        [.. Records.Where(r => !r.IsDeleted).SelectMany(r => r.Meta.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<string> AllFolders() =>
        [.. Records.Where(r => !r.IsDeleted && r.Meta.Folder.Length > 0).Select(r => r.Meta.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];

    public StorageUsage GetUsage()
    {
        IReadOnlyList<CaptureRecord> all = Records;
        return new StorageUsage(
            all.Count(r => !r.IsDeleted),
            all.Count(r => r.IsDeleted),
            all.Sum(r => r.SizeBytes),
            all.Where(r => r.IsDeleted).Sum(r => r.SizeBytes),
            Root);
    }

    /// <summary>Applies retention rules. Returns (moved to trash, permanently deleted).</summary>
    public (int Trashed, int Deleted) ApplyRetention(RetentionPolicy policy, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(policy);
        int trashed = 0;
        if (policy.MoveToTrashAfterDays is int keep)
        {
            Guid[] old = Records.Where(r => !r.IsDeleted && !r.IsFavorite && nowUtc - r.CreatedUtc > TimeSpan.FromDays(keep)).Select(r => r.Id).ToArray();
            MoveToTrash(old, nowUtc);
            trashed = old.Length;
        }

        int deleted = 0;
        if (policy.EmptyTrashAfterDays is int days)
        {
            Guid[] expired = Records.Where(r => r.Meta.DeletedUtc is { } d && nowUtc - d > TimeSpan.FromDays(days)).Select(r => r.Id).ToArray();
            DeletePermanently(expired);
            deleted = expired.Length;
        }

        return (trashed, deleted);
    }

    public void ExportPackage(Guid id, string packagePath)
    {
        RequireImage(id);
        CaptureFormat.WritePackage(ExistingFolder(id), packagePath);
    }

    /// <summary>Imports a .sfcap package as a new capture (always a new id), after verifying it loads.</summary>
    public CaptureRecord ImportPackage(string packagePath)
    {
        string staging = Path.Combine(Root, $".import-{Guid.NewGuid():N}");
        try
        {
            CaptureFormat.ExtractPackage(packagePath, staging);
            return AdoptStagedCapture(staging, keepIdIfFree: false);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>Writes a backup ZIP of every capture (including the Trash) to <paramref name="zipPath"/>.</summary>
    public int Backup(string zipPath)
    {
        int count = 0;
        AtomicFile.Write(zipPath, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            zip.CreateEntry("snapforge-backup.json").Open().Using(s => JsonSerializer.Serialize(s, new JsonObject
            {
                ["format"] = "snapforge.backup",
                ["version"] = 1,
                ["createdUtc"] = CaptureFormat.Iso(DateTime.UtcNow),
            }));
            // Images only: the backup format predates video, and older versions must be able to restore it.
            foreach (CaptureRecord r in Records.Where(r => r.Damage is null && !r.IsVideo))
            {
                string dir = FolderOf(r.Id);
                foreach (string name in CaptureFormat.ContentFiles)
                {
                    string file = Path.Combine(dir, name);
                    if (File.Exists(file))
                    {
                        zip.CreateEntryFromFile(file, $"captures/{r.Id:D}/{name}", CompressionLevel.NoCompression);
                    }
                }

                count++;
            }
        });
        return count;
    }

    /// <summary>
    /// Restores captures from a backup ZIP. Existing captures are never overwritten: a capture whose id already
    /// exists is skipped. Entry names are strictly validated. Returns the number of captures restored.
    /// </summary>
    public int RestoreBackup(string zipPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry("snapforge-backup.json") is null)
        {
            throw new InvalidDataException("This file is not a SnapForge library backup.");
        }

        var byCapture = new Dictionary<Guid, List<ZipArchiveEntry>>();
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            if (e.FullName == "snapforge-backup.json")
            {
                continue;
            }

            string[] parts = e.FullName.Split('/');
            if (parts.Length != 3 || parts[0] != "captures" || !Guid.TryParse(parts[1], out Guid id) || !CaptureFormat.ContentFiles.Contains(parts[2], StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unexpected entry '{e.FullName}' in backup.");
            }

            (byCapture.TryGetValue(id, out var list) ? list : byCapture[id] = []).Add(e);
        }

        int restored = 0;
        foreach (var (id, entries) in byCapture)
        {
            if (Find(id) is not null)
            {
                continue;
            }

            string staging = Path.Combine(Root, $".restore-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(staging);
                foreach (ZipArchiveEntry e in entries)
                {
                    using Stream s = e.Open();
                    AtomicFile.Write(Path.Combine(staging, Path.GetFileName(e.FullName)), s.CopyTo);
                }

                AdoptStagedCapture(staging, keepIdIfFree: true, id);
                restored++;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
        }

        return restored;
    }

    /// <summary>
    /// Copies the whole library to <paramref name="newRoot"/>, verifies every capture there, then removes the old
    /// copy (overwriting files first). The target must not already contain captures. Returns the opened new library.
    /// </summary>
    public CaptureLibrary MoveTo(string newRoot)
    {
        string target = Path.GetFullPath(newRoot);
        if (string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        if (target.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The new location cannot be inside the current library.", nameof(newRoot));
        }

        string targetCaptures = Path.Combine(target, "captures");
        string targetVideos = Path.Combine(target, VideosFolderName);
        if ((Directory.Exists(targetCaptures) && Directory.EnumerateFileSystemEntries(targetCaptures).Any())
            || (Directory.Exists(targetVideos) && Directory.EnumerateFileSystemEntries(targetVideos).Any()))
        {
            throw new IOException("The chosen folder already contains a SnapForge library. Choose an empty folder.");
        }

        Directory.CreateDirectory(targetCaptures);
        foreach (string dir in Directory.EnumerateDirectories(CapturesDirectory))
        {
            string dest = Path.Combine(targetCaptures, Path.GetFileName(dir));
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                AtomicFile.Write(Path.Combine(dest, Path.GetFileName(file)), s =>
                {
                    using FileStream src = File.OpenRead(file);
                    src.CopyTo(s);
                });
            }
        }

        CopyVideosTo(target);
        CaptureLibrary moved = Open(target);
        foreach (CaptureRecord r in Records.Where(r => r.Damage is null))
        {
            if (moved.Find(r.Id) is null)
            {
                throw new IOException($"Capture {r.Id} did not copy correctly; the original library was left unchanged.");
            }

            if (!r.IsVideo)
            {
                moved.Load(r.Id); // verifies hashes
            }
        }

        DeletePermanently([.. Records.Select(r => r.Id)]);
        foreach (string leftover in Directory.EnumerateDirectories(CapturesDirectory).Concat(Directory.EnumerateDirectories(VideosDirectory)))
        {
            Directory.Delete(leftover, recursive: true);
        }

        return moved;
    }

    public static string DefaultTitle(DateTime createdUtc) =>
        "Capture " + createdUtc.ToLocalTime().ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);

    private CaptureRecord AdoptStagedCapture(string staging, bool keepIdIfFree, Guid preferredId = default)
    {
        JsonObject manifest = ReadManifest(staging);
        Guid id = keepIdIfFree && preferredId != Guid.Empty && Find(preferredId) is null ? preferredId : Guid.NewGuid();
        manifest["id"] = id.ToString("D");
        AtomicFile.WriteAllBytes(Path.Combine(staging, CaptureFormat.ManifestFile), JsonSerializer.SerializeToUtf8Bytes(manifest, AnnotationSerializer.Indented));
        if (!File.Exists(Path.Combine(staging, CaptureFormat.MetaFile)))
        {
            WriteMeta(staging, new CaptureMetadata { Title = DefaultTitle(DateTime.UtcNow) });
        }

        // Verify before adopting: hashes and decodability.
        byte[] baseBytes = File.ReadAllBytes(Path.Combine(staging, CaptureFormat.BaseFile));
        byte[] annBytes = File.ReadAllBytes(Path.Combine(staging, CaptureFormat.AnnotationsFile));
        Verify(manifest, "baseSha256", baseBytes, CaptureFormat.BaseFile);
        Verify(manifest, "annotationsSha256", annBytes, CaptureFormat.AnnotationsFile);
        PixelCodec.Decode(baseBytes);
        CaptureFormat.DeserializeAnnotations(annBytes);

        Directory.Move(staging, FolderOf(id));
        return Refresh(id);
    }

    private static void WriteContent(Guid id, string dir, PixelBuffer? newBase, IReadOnlyList<Annotation> annotations, PixelBuffer flattened, DateTime createdUtc, DateTime? sanitizedUtc, bool reversibleRedactions, JsonObject? previous = null)
    {
        string? baseHash = previous?["baseSha256"]?.GetValue<string>();
        int width = previous?["width"]?.GetValue<int>() ?? 0, height = previous?["height"]?.GetValue<int>() ?? 0;
        if (newBase is not null)
        {
            byte[] baseBytes = PixelCodec.Encode(newBase);
            AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.BaseFile), baseBytes);
            baseHash = AtomicFile.Sha256Hex(baseBytes);
            (width, height) = (newBase.Width, newBase.Height);
        }

        byte[] annBytes = CaptureFormat.SerializeAnnotations(annotations);
        AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.AnnotationsFile), annBytes);
        AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.ThumbFile), PixelCodec.Encode(Thumbnailer.Create(flattened, ThumbnailSize)));

        var manifest = new JsonObject
        {
            ["format"] = CaptureFormat.FormatId,
            ["version"] = CaptureFormat.CurrentVersion,
            ["id"] = id.ToString("D"),
            ["createdUtc"] = CaptureFormat.Iso(createdUtc),
            ["modifiedUtc"] = CaptureFormat.Iso(DateTime.UtcNow),
            ["width"] = width,
            ["height"] = height,
            ["annotationCount"] = annotations.Count(a => a is not RedactionMaskAnnotation),
            ["redactionCount"] = annotations.Count(a => a is RedactionMaskAnnotation),
            ["containsUnsanitizedRedactions"] = reversibleRedactions,
            ["sanitizedUtc"] = sanitizedUtc is { } s ? CaptureFormat.Iso(s) : null,
            ["baseSha256"] = baseHash ?? throw new InvalidOperationException("A new capture needs base pixels."),
            ["annotationsSha256"] = AtomicFile.Sha256Hex(annBytes),
            ["generator"] = $"{ProductInfo.Name} {typeof(CaptureLibrary).Assembly.GetName().Version}",
        };

        // Manifest last: it is the commit point that references the files written above.
        AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.ManifestFile), JsonSerializer.SerializeToUtf8Bytes(manifest, AnnotationSerializer.Indented));
    }

    private CaptureRecord Refresh(Guid id)
    {
        string folder = FolderOf(id);
        CaptureRecord record = IsVideoFolder(folder) ? ReadVideoRecord(id, folder) : ReadRecord(id, folder);
        lock (_gate)
        {
            _records[id] = record;
        }

        RaiseChanged([id]);
        return record;
    }

    private static CaptureRecord ReadRecord(Guid id, string dir)
    {
        CaptureMetadata meta = ReadMeta(dir);
        long size = Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length);
        try
        {
            JsonObject m = ReadManifest(dir);
            if (!File.Exists(Path.Combine(dir, CaptureFormat.BaseFile)) || !File.Exists(Path.Combine(dir, CaptureFormat.AnnotationsFile)))
            {
                throw new InvalidDataException("Content files are missing.");
            }

            return new CaptureRecord
            {
                Id = id,
                Meta = meta,
                CreatedUtc = CaptureFormat.ParseIso(m["createdUtc"]?.GetValue<string>()),
                ModifiedUtc = CaptureFormat.ParseIso(m["modifiedUtc"]?.GetValue<string>()),
                Width = m["width"]?.GetValue<int>() ?? 0,
                Height = m["height"]?.GetValue<int>() ?? 0,
                AnnotationCount = m["annotationCount"]?.GetValue<int>() ?? 0,
                HasReversibleRedactions = m["containsUnsanitizedRedactions"]?.GetValue<bool>() ?? false,
                SanitizedUtc = m["sanitizedUtc"]?.GetValue<string>() is { } s ? CaptureFormat.ParseIso(s) : null,
                SizeBytes = size,
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
        {
            return new CaptureRecord { Id = id, Meta = meta, SizeBytes = size, Damage = ex.Message, CreatedUtc = Directory.GetCreationTimeUtc(dir), ModifiedUtc = Directory.GetLastWriteTimeUtc(dir) };
        }
    }

    private static JsonObject ReadManifest(string dir)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(dir, CaptureFormat.ManifestFile));
        JsonObject m = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Manifest is not an object.");
        CaptureFormat.CheckVersion(m, CaptureFormat.FormatId);
        return m;
    }

    private static CaptureMetadata ReadMeta(string dir)
    {
        string file = Path.Combine(dir, CaptureFormat.MetaFile);
        try
        {
            if (File.Exists(file) && JsonNode.Parse(File.ReadAllBytes(file)) is JsonObject o)
            {
                return new CaptureMetadata
                {
                    Title = o["title"]?.GetValue<string>() ?? string.Empty,
                    Tags = (o["tags"] as JsonArray)?.Select(t => t?.GetValue<string>() ?? string.Empty).Where(t => t.Length > 0).ToArray() ?? [],
                    Folder = o["folder"]?.GetValue<string>() ?? string.Empty,
                    Favorite = o["favorite"]?.GetValue<bool>() ?? false,
                    DeletedUtc = o["deletedUtc"]?.GetValue<string>() is { } d ? CaptureFormat.ParseIso(d) : null,
                    LastOpenedUtc = o["lastOpenedUtc"]?.GetValue<string>() is { } l ? CaptureFormat.ParseIso(l) : null,
                };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // Fall back to defaults; content is unaffected.
        }

        return new CaptureMetadata { Title = "Untitled capture" };
    }

    private static void WriteMeta(string dir, CaptureMetadata meta)
    {
        var o = new JsonObject
        {
            ["format"] = CaptureFormat.FormatId + ".meta",
            ["version"] = 1,
            ["title"] = meta.Title,
            ["tags"] = new JsonArray([.. meta.Tags.Select(t => (JsonNode?)JsonValue.Create(t))]),
            ["folder"] = meta.Folder,
            ["favorite"] = meta.Favorite,
            ["deletedUtc"] = meta.DeletedUtc is { } d ? CaptureFormat.Iso(d) : null,
            ["lastOpenedUtc"] = meta.LastOpenedUtc is { } l ? CaptureFormat.Iso(l) : null,
        };
        AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.MetaFile), JsonSerializer.SerializeToUtf8Bytes(o, AnnotationSerializer.Indented));
    }

    private static void Verify(JsonObject manifest, string key, byte[] data, string name)
    {
        string? expected = manifest[key]?.GetValue<string>();
        if (expected is null || !string.Equals(expected, AtomicFile.Sha256Hex(data), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{name} failed its integrity check (the file is damaged or was modified).");
        }
    }

    private static void CompleteInterruptedSanitize(string dir)
    {
        string pending = Path.Combine(dir, SanitizedBaseFile);
        if (File.Exists(pending))
        {
            AtomicFile.OverwriteAndDelete(Path.Combine(dir, CaptureFormat.BaseFile));
            File.Move(pending, Path.Combine(dir, CaptureFormat.BaseFile));
        }
    }

    private static string[] NormalizeTags(IEnumerable<string> tags) =>
        [.. tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The folder of an item: its video folder if it is a video, otherwise its capture folder.</summary>
    private string FolderOf(Guid id)
    {
        string video = VideoFolder(id);
        return Directory.Exists(video) ? video : CaptureFolder(id);
    }

    private string CaptureFolder(Guid id) => Path.Combine(CapturesDirectory, id.ToString("D"));

    private string ExistingFolder(Guid id)
    {
        string dir = FolderOf(id);
        return Directory.Exists(dir) ? dir : throw new KeyNotFoundException($"Capture {id} not found in the library.");
    }

    private void RaiseChanged(IReadOnlyCollection<Guid> ids) => Changed?.Invoke(this, new LibraryChangedEventArgs(ids));
}

internal static class StreamExtensions
{
    public static void Using(this Stream stream, Action<Stream> action)
    {
        using (stream)
        {
            action(stream);
        }
    }
}
