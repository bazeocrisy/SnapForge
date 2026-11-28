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
/// <remarks>
/// <para>Several processes may open the same Library folder at once (SnapForge Capture creates captures and records
/// videos; SnapForge Editor edits, organises, backs up, restores and relocates). The rules
/// (docs/LIBRARY-COORDINATION.md):</para>
/// <list type="bullet">
/// <item>Every write to the library folder (commits, metadata, deletes, video begin/complete/abandon, backup, restore,
/// relocation) and every clean-up or recovery on open runs inside the <em>write gate</em>: this instance's monitor plus
/// a named OS mutex derived from the root path, shared by every process. Waiting for another process is bounded
/// (2 minutes, then an <see cref="IOException"/> saying the library is busy).</item>
/// <item>Clean-up never touches another process's work: <c>.creating-</c> folders, uncommitted generation files and
/// interrupted restores are only removed under the gate (when no other writer can be mid-commit), temporary files
/// only when no handle has them open, and outside the item folders only when older than
/// <see cref="TemporaryFileSafeAge"/>. Indexing never deletes or changes an in-progress recording's folder.</item>
/// <item>Each process keeps its own index. To see another process's changes call <see cref="RefreshFromDisk"/> for an
/// item, <see cref="Reindex"/> for everything, or run a <see cref="LibraryWatcher"/> that does it automatically.</item>
/// <item>Only one process (SnapForge Capture) may act on <see cref="InterruptedRecordings"/> (recover or abandon):
/// another process cannot tell a live recording from an interrupted one.</item>
/// </list>
/// </remarks>
public sealed partial class CaptureLibrary
{
    public const int ThumbnailSize = 320;

    /// <summary>Prefix of a capture folder being created (published by renaming it to its id: the commit point).</summary>
    internal const string CreatingPrefix = ".creating-";

    private readonly object _gate = new();

    /// <summary>
    /// Serialises commits and folder clean-up within this process and across processes sharing the root, so an index
    /// never deletes files of a save in progress (see the class remarks).
    /// </summary>
    private readonly LibraryWriteGate _writeGate;
    private readonly Dictionary<Guid, CaptureRecord> _records = [];

    /// <summary>Set for <see cref="OpenForInspection"/>: indexing reads only and never cleans up or recovers.</summary>
    private bool _inspectOnly;

    private CaptureLibrary(string root)
    {
        Root = Path.GetFullPath(root);
        CapturesDirectory = Path.Combine(Root, "captures");
        VideosDirectory = Path.Combine(Root, VideosFolderName);
        _writeGate = new LibraryWriteGate(Root);
    }

    /// <summary>
    /// Temporary files (<see cref="AtomicFile.TempSuffix"/>) outside the item folders are removed on open only when
    /// older than this (and not open): another program sharing the library may be writing them.
    /// </summary>
    public static readonly TimeSpan TemporaryFileSafeAge = TimeSpan.FromMinutes(10);

    /// <summary>How long a write waits for another SnapForge program to leave the write gate (tests shorten it).</summary>
    internal TimeSpan WriteGateTimeout
    {
        get => _writeGate.Timeout;
        set => _writeGate.Timeout = value;
    }

    /// <summary>Test hook: called inside the write gate of <see cref="Create"/> just before the commit rename.</summary>
    internal static AsyncLocal<Action<string>?> CreateCommitHook { get; } = new();

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

    /// <summary>
    /// Completes or cleans up an interrupted restore (<see cref="RecoveredRestore"/>), removes leftovers of
    /// interrupted writes and builds the index. Safe to call from a background thread.
    /// </summary>
    public void CompleteOpen()
    {
        RecoveredRestore = ResumeInterruptedRestore();

        // Capture folders first: their leftovers (which may hold pre-sanitize pixels) are overwritten before deletion.
        Reindex();
        CleanupTemporaryFiles();
        IsIndexed = true;
        Indexed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True if a capture folder with a manifest exists on disk (usable before indexing finishes).</summary>
    public bool ExistsOnDisk(Guid id) =>
        File.Exists(Path.Combine(CaptureFolder(id), CaptureFormat.ManifestFile)) || File.Exists(Path.Combine(VideoFolder(id), VideoManifestFile));

    public static string DefaultRoot() =>
        Path.Combine(ProductInfo.DataRoot, "Library");

    public CaptureRecord? Find(Guid id)
    {
        lock (_gate)
        {
            return _records.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Re-reads every capture folder (used at start-up, after restore/import, and to pick up changes made by another
    /// process). Clean-up and recovery of each folder happen inside the write gate, so they never touch a commit that
    /// is in progress in this or another process.
    /// </summary>
    public void Reindex()
    {
        var found = new Dictionary<Guid, CaptureRecord>();
        foreach (string dir in Directory.EnumerateDirectories(CapturesDirectory))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith(CreatingPrefix, StringComparison.Ordinal))
            {
                // A capture whose creation never reached its commit point (the folder rename). Only inside the gate
                // is it certain that no process is still building it; it may also have been published meanwhile.
                if (!_inspectOnly)
                {
                    using (EnterWrite())
                    {
                        if (Directory.Exists(dir))
                        {
                            SecureDeleteDirectory(dir);
                        }
                    }
                }
            }
            else if (Guid.TryParse(name, out Guid id))
            {
                if (_inspectOnly)
                {
                    if (File.Exists(Path.Combine(dir, CaptureFormat.ManifestFile)))
                    {
                        found[id] = ReadRecord(id, dir);
                    }

                    continue;
                }

                using (EnterWrite())
                {
                    // Checked inside the gate: another process may have deleted or be committing it.
                    if (File.Exists(Path.Combine(dir, CaptureFormat.ManifestFile)))
                    {
                        RecoverCaptureFolder(dir);
                        found[id] = ReadRecord(id, dir);
                    }
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
        using (EnterWrite())
        {
            // Built complete in a staging folder, then published by one rename (the commit point); a crash before
            // the rename leaves a ".creating-" folder that the next open securely deletes.
            string staging = Path.Combine(CapturesDirectory, CreatingPrefix + id.ToString("N"));
            Directory.CreateDirectory(staging);
            var meta = new CaptureMetadata { Title = title ?? DefaultTitle(createdUtc) };
            WriteMeta(staging, meta);
            bool reversible = content.Annotations.OfType<RedactionMaskAnnotation>().Any();
            CommitGeneration(staging, id, previous: null, content.BaseImage, content.Annotations, flattened, createdUtc, sanitizedUtc: null, reversible);
            CreateCommitHook.Value?.Invoke(staging);
            MoveDirectory(staging, CaptureFolder(id));
            DurabilityFaults.Checkpoint("capture.create.published");
        }

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
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(flattened);
        using (EnterWrite())
        {
            string dir = ExistingFolder(id);
            CaptureManifest previous = CaptureManifest.Read(dir);
            CaptureManifest committed = CommitGeneration(dir, id, previous, newBase, annotations, flattened, previous.CreatedUtc, hasReversibleRedactions ? null : previous.SanitizedUtc, hasReversibleRedactions);
            DeleteUnreferenced(dir, committed);
        }

        return Refresh(id);
    }

    /// <summary>
    /// Destructive sanitization of the stored copy (FA-016). Commits a new generation whose base is
    /// <paramref name="sanitizedBase"/> with every redaction burned in, whose annotations have no redaction masks and
    /// no annotation hidden under a redaction, and whose thumbnail has the redactions burned in; then overwrites the
    /// previous base, annotations and thumbnail files with zeros and deletes them. Returns only after all of that.
    /// </summary>
    /// <remarks>
    /// Enforced here whatever the caller passes: the masks applied are those in <paramref name="annotations"/> plus
    /// those stored in the capture; an annotation is "hidden" when it intersects a mask that comes later in z-order
    /// (the rule of <see cref="Editing.EditorDocument.Sanitize"/>, which the App runs first and whose rasterized
    /// result it passes as <paramref name="sanitizedBase"/>). Crash-safe: the manifest is the commit point, so an
    /// interruption leaves either the complete unsanitized capture or the complete sanitized one, and leftovers of
    /// the old generation are overwritten and deleted when the library is next opened. If deleting the old files
    /// fails, the sanitized generation is committed and an <see cref="IOException"/> is thrown.
    /// </remarks>
    public CaptureRecord Sanitize(Guid id, PixelBuffer sanitizedBase, IReadOnlyList<Annotation> annotations, PixelBuffer flattened)
    {
        RequireImage(id);
        ArgumentNullException.ThrowIfNull(sanitizedBase);
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(flattened);
        using (EnterWrite())
        {
            string dir = ExistingFolder(id);
            CaptureManifest previous = CaptureManifest.Read(dir);
            List<Annotation> stored = CaptureFormat.DeserializeAnnotations(CaptureFormat.ReadVerified(dir, previous.Annotations));
            (PixelBuffer burnedBase, PixelBuffer burnedFlat, List<Annotation> kept) = SanitizePlan(sanitizedBase, flattened, annotations, stored);
            CaptureManifest committed = CommitGeneration(dir, id, previous, burnedBase, kept, burnedFlat, previous.CreatedUtc, DateTime.UtcNow, reversibleRedactions: false);
            try
            {
                DeleteUnreferenced(dir, committed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"The sanitized copy was saved, but the previous files could not be removed yet ({ex.Message}). SnapForge removes them the next time it opens the library.", ex);
            }
        }

        return Refresh(id);
    }

    /// <summary>The sanitized base, thumbnail source and annotations for <see cref="Sanitize"/>.</summary>
    internal static (PixelBuffer Base, PixelBuffer Flattened, List<Annotation> Annotations) SanitizePlan(PixelBuffer sanitizedBase, PixelBuffer flattened, IReadOnlyList<Annotation> annotations, IReadOnlyList<Annotation> stored)
    {
        static bool Intersects(Geometry.RectD r, Geometry.PixelRect p) =>
            r.X < p.Right && r.Right > p.X && r.Y < p.Bottom && r.Bottom > p.Y;

        static HashSet<Guid> HiddenIds(IReadOnlyList<Annotation> list)
        {
            var hidden = new HashSet<Guid>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not RedactionMaskAnnotation && list.Skip(i + 1).OfType<RedactionMaskAnnotation>().Any(m => Intersects(list[i].Bounds, m.Region)))
                {
                    hidden.Add(list[i].Id);
                }
            }

            return hidden;
        }

        var masks = annotations.OfType<RedactionMaskAnnotation>().Concat(stored.OfType<RedactionMaskAnnotation>()).ToList();
        HashSet<Guid> hidden = HiddenIds(annotations);
        hidden.UnionWith(HiddenIds(stored));
        var kept = annotations.Where(a => a is not RedactionMaskAnnotation && !hidden.Contains(a.Id)).ToList();

        PixelBuffer burnedBase = sanitizedBase.Clone();
        PixelBuffer burnedFlat = flattened.Clone();
        foreach (RedactionMaskAnnotation mask in masks)
        {
            burnedBase.FillRect(mask.Region, mask.Color);
            if (flattened.Width == sanitizedBase.Width && flattened.Height == sanitizedBase.Height)
            {
                burnedFlat.FillRect(mask.Region, mask.Color);
            }
        }

        return (burnedBase, burnedFlat, kept);
    }

    public CaptureContent Load(Guid id)
    {
        RequireImage(id);
        string dir = ExistingFolder(id);
        CaptureManifest manifest = CaptureManifest.Read(dir);
        byte[] baseBytes = CaptureFormat.ReadVerified(dir, manifest.Base);
        byte[] annBytes = CaptureFormat.ReadVerified(dir, manifest.Annotations);
        return new CaptureContent(PixelCodec.Decode(baseBytes), CaptureFormat.DeserializeAnnotations(annBytes));
    }

    public PixelBuffer? LoadThumbnail(Guid id)
    {
        string dir = FolderOf(id);
        try
        {
            if (IsVideoFolder(dir))
            {
                string file = Path.Combine(dir, CaptureFormat.ThumbFile);
                return File.Exists(file) ? PixelCodec.Decode(File.ReadAllBytes(file)) : null;
            }

            CaptureManifest m = CaptureManifest.Read(dir);
            if (m.Thumb is null)
            {
                return null;
            }

            // v2 thumbnails are hashed; v1 thumbnails are not (regenerable cache).
            return m.Thumb.Sha256 is null
                ? PixelCodec.Decode(File.ReadAllBytes(Path.Combine(dir, m.Thumb.Name)))
                : PixelCodec.Decode(CaptureFormat.ReadVerified(dir, m.Thumb));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public CaptureRecord UpdateMetadata(Guid id, Func<CaptureMetadata, CaptureMetadata> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        using (EnterWrite())
        {
            // Read-modify-write inside the gate, so a change made by another process at the same time is not lost.
            string dir = ExistingFolder(id);
            CaptureMetadata meta = update(ReadMeta(dir));
            WriteMeta(dir, meta with { Tags = NormalizeTags(meta.Tags), Folder = meta.Folder.Trim(), Title = meta.Title.Trim() });
        }

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
            using (EnterWrite())
            {
                SecureDeleteDirectory(FolderOf(id));
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

        // Inside the gate: opening the library in another process removes the ".import-" folders it finds.
        using (EnterWrite())
        {
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
    }

    public static string DefaultTitle(DateTime createdUtc) =>
        "Capture " + createdUtc.ToLocalTime().ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);

    private CaptureRecord AdoptStagedCapture(string staging, bool keepIdIfFree, Guid preferredId = default)
    {
        CaptureManifest manifest = CaptureManifest.Read(staging);
        Guid id = keepIdIfFree && preferredId != Guid.Empty && Find(preferredId) is null && !Directory.Exists(CaptureFolder(preferredId)) ? preferredId : Guid.NewGuid();
        manifest.Json["id"] = id.ToString("D");
        AtomicFile.WriteAllBytes(Path.Combine(staging, CaptureFormat.ManifestFile), JsonSerializer.SerializeToUtf8Bytes(manifest.Json, AnnotationSerializer.Indented));
        if (!File.Exists(Path.Combine(staging, CaptureFormat.MetaFile)))
        {
            WriteMeta(staging, new CaptureMetadata { Title = DefaultTitle(DateTime.UtcNow) });
        }

        // Verify before adopting: hashes and decodability.
        PixelCodec.Decode(CaptureFormat.ReadVerified(staging, manifest.Base));
        CaptureFormat.DeserializeAnnotations(CaptureFormat.ReadVerified(staging, manifest.Annotations));

        using (EnterWrite())
        {
            MoveDirectory(staging, CaptureFolder(id));
        }

        return Refresh(id);
    }

    /// <summary>
    /// Writes a new generation (FA-015): the content files under new unique names (each flushed to disk), then the
    /// manifest naming them with their SHA-256 and size, atomically replacing the previous manifest. The manifest
    /// replace is the single commit point: before it the previous generation is intact and referenced; after it the
    /// new one is. Files of older generations are removed separately by <see cref="DeleteUnreferenced"/>.
    /// </summary>
    private static CaptureManifest CommitGeneration(string dir, Guid id, CaptureManifest? previous, PixelBuffer? newBase, IReadOnlyList<Annotation> annotations, PixelBuffer flattened, DateTime createdUtc, DateTime? sanitizedUtc, bool reversibleRedactions)
    {
        int generation = checked((previous?.Generation ?? 0) + 1);
        CaptureFileRef baseRef;
        int width, height;
        if (newBase is not null)
        {
            baseRef = WriteGenerationFile(dir, CaptureFormat.GenerationBaseFile(generation), PixelCodec.Encode(newBase));
            (width, height) = (newBase.Width, newBase.Height);
        }
        else
        {
            CaptureManifest prev = previous ?? throw new InvalidOperationException("A new capture needs base pixels.");
            string file = Path.Combine(dir, prev.Base.Name);
            if (!File.Exists(file) || prev.Base.Sha256 is null)
            {
                throw new InvalidDataException("The stored base image is missing or has no hash; the capture cannot be saved without new base pixels.");
            }

            // An unchanged base keeps its file (and, for a v1 folder, its legacy name): no multi-megabyte rewrite.
            baseRef = prev.Base with { Bytes = prev.Base.Bytes ?? new FileInfo(file).Length };
            (width, height) = (prev.Width, prev.Height);
        }

        CaptureFileRef annRef = WriteGenerationFile(dir, CaptureFormat.GenerationAnnotationsFile(generation), CaptureFormat.SerializeAnnotations(annotations));
        CaptureFileRef thumbRef = WriteGenerationFile(dir, CaptureFormat.GenerationThumbFile(generation), PixelCodec.Encode(Thumbnailer.Create(flattened, ThumbnailSize)));

        var json = new JsonObject
        {
            ["format"] = CaptureFormat.FormatId,
            ["version"] = CaptureFormat.CurrentVersion,
            ["id"] = id.ToString("D"),
            ["generation"] = generation,
            ["createdUtc"] = CaptureFormat.Iso(createdUtc),
            ["modifiedUtc"] = CaptureFormat.Iso(DateTime.UtcNow),
            ["width"] = width,
            ["height"] = height,
            ["annotationCount"] = annotations.Count(a => a is not RedactionMaskAnnotation),
            ["redactionCount"] = annotations.Count(a => a is RedactionMaskAnnotation),
            ["containsUnsanitizedRedactions"] = reversibleRedactions,
            ["sanitizedUtc"] = sanitizedUtc is { } s ? CaptureFormat.Iso(s) : null,
            ["files"] = CaptureManifest.FilesJson(baseRef, annRef, thumbRef),
            ["generator"] = $"{ProductInfo.Name} {typeof(CaptureLibrary).Assembly.GetName().Version}",
        };

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(json, AnnotationSerializer.Indented);
        DurabilityFaults.Checkpoint("capture.generation-written");
        AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.ManifestFile), manifestBytes); // the commit point
        DurabilityFaults.Checkpoint("capture.manifest-committed");
        return CaptureManifest.Parse(manifestBytes);
    }

    private static CaptureFileRef WriteGenerationFile(string dir, string name, byte[] data)
    {
        AtomicFile.WriteAllBytes(Path.Combine(dir, name), data);
        return new CaptureFileRef(name, AtomicFile.Sha256Hex(data), data.Length);
    }

    /// <summary>
    /// Overwrites with zeros and deletes every content file of the folder that <paramref name="manifest"/> does not
    /// reference (superseded generations, uncommitted generations, interrupted temporary files). Unknown files are
    /// left alone. Returns the number of files removed.
    /// </summary>
    private static int DeleteUnreferenced(string dir, CaptureManifest manifest)
    {
        HashSet<string> keep = manifest.ReferencedFiles();
        int removed = 0;
        foreach (string file in Directory.GetFiles(dir))
        {
            string name = Path.GetFileName(file);
            if (!keep.Contains(name) && CaptureManifest.IsContentFileName(name))
            {
                if (name.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase) && AtomicFile.IsOpenElsewhere(file))
                {
                    continue; // still being written by someone: never pulled from under a writer (removed on a later open)
                }

                AtomicFile.OverwriteAndDelete(file);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Brings a capture folder to a coherent state on open/index: recovers an interrupted sanitize of a v1 folder
    /// written by SnapForge 1.0 RC, then securely deletes every content file the manifest does not reference.
    /// A folder whose manifest cannot be read is left untouched (it is listed as damaged).
    /// </summary>
    private static void RecoverCaptureFolder(string dir)
    {
        try
        {
            CaptureManifest m = CaptureManifest.Read(dir);
            if (m.Version == 1 && !File.Exists(Path.Combine(dir, CaptureFormat.BaseFile)) && File.Exists(Path.Combine(dir, CaptureFormat.LegacySanitizedBaseFile)))
            {
                m = AdoptLegacyInterruptedSanitize(dir, m);
            }

            DeleteUnreferenced(dir, m);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            // Damaged: keep every file for inspection or recovery.
        }
    }

    /// <summary>
    /// SnapForge 1.0 RC sanitized v1 folders by swapping files: if it stopped after deleting the old base and before
    /// renaming the sanitized one into place, the sanitized base is adopted as generation 1 (with its own hash).
    /// The record keeps "contains unsanitized redactions" if masks remain, so the user is offered sanitize again
    /// (which also removes annotations hidden under them).
    /// </summary>
    private static CaptureManifest AdoptLegacyInterruptedSanitize(string dir, CaptureManifest legacy)
    {
        string pending = Path.Combine(dir, CaptureFormat.LegacySanitizedBaseFile);
        byte[] baseBytes = File.ReadAllBytes(pending);
        PixelBuffer sanitized = PixelCodec.Decode(baseBytes);
        byte[] annBytes = CaptureFormat.ReadVerified(dir, legacy.Annotations);
        List<Annotation> annotations = CaptureFormat.DeserializeAnnotations(annBytes);
        string baseName = CaptureFormat.GenerationBaseFile(1);
        File.Move(pending, Path.Combine(dir, baseName), overwrite: true);
        JsonObject json = (JsonObject)legacy.Json.DeepClone();
        json.Remove("baseSha256");
        json.Remove("annotationsSha256");
        json["version"] = CaptureFormat.CurrentVersion;
        json["generation"] = 1;
        json["width"] = sanitized.Width;
        json["height"] = sanitized.Height;
        json["containsUnsanitizedRedactions"] = annotations.OfType<RedactionMaskAnnotation>().Any();
        json["files"] = CaptureManifest.FilesJson(
            new CaptureFileRef(baseName, AtomicFile.Sha256Hex(baseBytes), baseBytes.Length),
            new CaptureFileRef(legacy.Annotations.Name, AtomicFile.Sha256Hex(annBytes), annBytes.Length),
            thumb: null);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(json, AnnotationSerializer.Indented);
        AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.ManifestFile), manifestBytes);
        return CaptureManifest.Parse(manifestBytes);
    }

    /// <summary>Overwrites (images and small files) and deletes every file of a folder, then the folder.</summary>
    private static void SecureDeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            DeleteLibraryFile(file);
        }

        Directory.Delete(dir, recursive: true);
    }

    /// <summary>Renames a folder (atomic on one volume), retrying briefly while another program (antivirus, indexer) holds a file.</summary>
    private static void MoveDirectory(string source, string destination)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception ex) when (attempt < 5 && (ex is UnauthorizedAccessException || (ex is IOException && !Directory.Exists(destination))))
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private CaptureRecord Refresh(Guid id)
    {
        CaptureRecord record;
        using (EnterWrite())
        {
            string folder = FolderOf(id);
            record = IsVideoFolder(folder) ? ReadVideoRecord(id, folder) : ReadRecord(id, folder);
            lock (_gate)
            {
                _records[id] = record;
            }
        }

        RaiseChanged([id]);
        return record;
    }

    /// <summary>
    /// Re-reads one item from disk, for changes made by another process sharing this library (or by any program):
    /// updates its record, adds it if it is new, or removes it if its folder (or manifest) is gone, then raises
    /// <see cref="Changed"/>. Returns the current record, or null if the item no longer exists. Never cleans up or
    /// recovers anything; reads inside the write gate, so it never sees a commit half-done. Cheap (one folder).
    /// </summary>
    public CaptureRecord? RefreshFromDisk(Guid id)
    {
        CaptureRecord? record = null;
        bool known;
        using (EnterWrite())
        {
            string video = VideoFolder(id);
            string capture = CaptureFolder(id);
            if (File.Exists(Path.Combine(video, VideoManifestFile)))
            {
                record = ReadVideoRecord(id, video);
            }
            else if (File.Exists(Path.Combine(capture, CaptureFormat.ManifestFile)))
            {
                record = ReadRecord(id, capture);
            }

            lock (_gate)
            {
                known = _records.ContainsKey(id);
                if (record is null)
                {
                    _records.Remove(id);
                }
                else
                {
                    _records[id] = record;
                }
            }
        }

        if (record is not null || known)
        {
            RaiseChanged([id]);
        }

        return record;
    }

    /// <summary>Enters the write gate (see the class remarks); dispose on the same thread, never across an await.</summary>
    private LibraryWriteGate.Scope EnterWrite() => _writeGate.Enter();

    /// <summary>
    /// Removes temporary files of interrupted writes (<see cref="AtomicFile.TempSuffix"/>) under the root, inside the
    /// write gate. A file that any handle still has open is never removed. Under <see cref="CapturesDirectory"/> and in
    /// completed video folders every write happens inside the gate, so while this holds the gate a closed temporary
    /// file there is a leftover; anywhere else (the root, an in-progress recording's folder, unknown folders) only
    /// files older than <see cref="TemporaryFileSafeAge"/> are removed. Returns the number removed.
    /// </summary>
    internal int CleanupTemporaryFiles()
    {
        int count = 0;
        using (EnterWrite())
        {
            if (!Directory.Exists(Root))
            {
                return 0;
            }

            DateTime cutoff = DateTime.UtcNow - TemporaryFileSafeAge;
            foreach (string file in Directory.EnumerateFiles(Root, "*" + AtomicFile.TempSuffix, SearchOption.AllDirectories))
            {
                try
                {
                    bool gated = IsUnder(file, CapturesDirectory) || IsInCompletedVideoFolder(file);
                    if ((gated || File.GetLastWriteTimeUtc(file) < cutoff) && !AtomicFile.IsOpenElsewhere(file) && AtomicFile.TryDeleteFile(file))
                    {
                        count++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort: the next open tries again.
                }
            }
        }

        return count;
    }

    private bool IsInCompletedVideoFolder(string file)
    {
        string? dir = Path.GetDirectoryName(file);
        return dir is not null && IsVideoFolder(dir) && File.Exists(Path.Combine(dir, VideoManifestFile)) && !File.Exists(Path.Combine(dir, InProgressMarker));
    }

    private static CaptureRecord ReadRecord(Guid id, string dir)
    {
        CaptureMetadata meta = ReadMeta(dir);
        long size = Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length);
        try
        {
            CaptureManifest manifest = CaptureManifest.Read(dir);
            JsonObject m = manifest.Json;
            foreach (CaptureFileRef file in new[] { manifest.Base, manifest.Annotations })
            {
                // Indexing checks presence and (v2) size only; hashes are verified when the capture is loaded.
                var info = new FileInfo(Path.Combine(dir, file.Name));
                if (!info.Exists)
                {
                    throw new InvalidDataException("Content files are missing.");
                }

                if (file.Bytes is long expected && info.Length != expected)
                {
                    throw new InvalidDataException($"{file.Name} is damaged or was modified (its size changed).");
                }
            }

            return new CaptureRecord
            {
                Id = id,
                Meta = meta,
                CreatedUtc = manifest.CreatedUtc,
                ModifiedUtc = CaptureFormat.ParseIso(m["modifiedUtc"]?.GetValue<string>()),
                Width = manifest.Width,
                Height = manifest.Height,
                AnnotationCount = m["annotationCount"]?.GetValue<int>() ?? 0,
                HasReversibleRedactions = m["containsUnsanitizedRedactions"]?.GetValue<bool>() ?? false,
                SanitizedUtc = manifest.SanitizedUtc,
                SizeBytes = size,
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
        {
            return new CaptureRecord { Id = id, Meta = meta, SizeBytes = size, Damage = ex.Message, CreatedUtc = Directory.GetCreationTimeUtc(dir), ModifiedUtc = Directory.GetLastWriteTimeUtc(dir) };
        }
    }

    /// <summary>The committed manifest of an image capture (tests and integrity checks).</summary>
    internal CaptureManifest ReadCaptureManifest(Guid id) => CaptureManifest.Read(ExistingFolder(id));

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
