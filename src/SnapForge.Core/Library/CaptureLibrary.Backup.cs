using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SnapForge.Core.Annotations;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Library;

/// <summary>
/// Library backup and restore, format version 2 (FA-005, FA-017, FA-023). A backup is a ZIP holding every item:
/// <list type="bullet">
/// <item><c>captures/&lt;id&gt;/…</c> — images: manifest, metadata and the current generation's content files;</item>
/// <item><c>videos/&lt;id&gt;/…</c> — videos: manifest, metadata, thumbnail and the MP4 (stored, not compressed);</item>
/// <item><c>raw/captures/&lt;id&gt;/…</c>, <c>raw/videos/&lt;id&gt;/…</c> — damaged items and interrupted recordings,
/// copied as they are;</item>
/// <item><c>snapforge-backup.json</c> (written last) — version, every item, every entry with size and SHA-256, and
/// everything under the library folder that was left out, with the reason. Nothing is silently omitted.</item>
/// </list>
/// Backups are streamed (no file is buffered whole), written to a temporary file and renamed into place, can be
/// cancelled and report progress. Restore validates everything first, extracts into one staging folder inside the
/// library (hashes verified while streaming), and only then adopts items one by one under a journal, so an
/// interruption is completed (or cleaned up) the next time the library opens. Existing items are never overwritten.
/// Version 1 backups (images only, written by SnapForge 1.0 RC) are still restored and labelled as such.
/// </summary>
public sealed partial class CaptureLibrary
{
    public const string BackupManifestEntry = "snapforge-backup.json";
    public const string BackupFormatId = "snapforge.backup";

    /// <summary>Backup format version written by this build.</summary>
    public const int BackupFormatVersion = 2;

    internal const string RestoreJournalFile = "restore-journal.json";
    internal const string RestoreStagingPrefix = ".restore-staging-";
    private const string RestoreJournalFormat = "snapforge.restore-journal";
    private const string RawPrefix = "raw/";

    /// <summary>Test seam: replaces the free-space query (bytes available on the volume holding the path).</summary>
    internal static AsyncLocal<Func<string, long>?> FreeSpaceOverride { get; } = new();

    /// <summary>Test seam: a lower entry-count limit than <see cref="BackupLimits.MaxEntries"/>.</summary>
    internal static AsyncLocal<int?> MaxEntriesOverride { get; } = new();

    private static int MaxEntries => MaxEntriesOverride.Value ?? BackupLimits.MaxEntries;

    /// <summary>Set when opening the library completed (or cleaned up) a restore that had been interrupted.</summary>
    public RestoreResult? RecoveredRestore { get; private set; }

    /// <summary>Writes a backup of every item (see <see cref="CreateBackup"/>) and returns the number of items in it.</summary>
    public int Backup(string zipPath) => CreateBackup(zipPath).Items.Count;

    /// <summary>Restores a backup (see <see cref="RestoreFromBackup"/>) and returns the number of items restored.</summary>
    public int RestoreBackup(string zipPath) => RestoreFromBackup(zipPath).Restored.Count;

    /// <summary>
    /// Writes a version 2 backup of the whole library to <paramref name="zipPath"/> (temp file + atomic rename): all
    /// images and videos including the Trash, metadata and thumbnails, damaged and incomplete items raw, and a
    /// manifest listing every entry (size, SHA-256) and every excluded file with its reason. Images are verified
    /// against their hashes and videos while they stream; a video that does not match is stored and reported as
    /// damaged. Refuses to start without enough free space at the destination. Cancellation leaves no file behind.
    /// </summary>
    public BackupResult CreateBackup(string zipPath, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        string target = Path.GetFullPath(zipPath);
        if (IsUnder(target, CapturesDirectory) || IsUnder(target, VideosDirectory))
        {
            throw new ArgumentException("The backup cannot be written inside the library's item folders.", nameof(zipPath));
        }

        using (EnterWrite())
        {
            (List<PlannedItem> items, List<BackupExclusion> excluded) = PlanBackup(cancellationToken);
            long total = items.Sum(i => i.Files.Sum(f => f.Bytes));
            long needed = total + (items.Count * 4096L) + BackupLimits.FreeSpaceReserveBytes;
            string targetDir = Path.GetDirectoryName(target) ?? throw new ArgumentException("Invalid backup path.", nameof(zipPath));
            Directory.CreateDirectory(targetDir);
            long free = AvailableFreeSpace(targetDir);
            if (free < needed)
            {
                throw new IOException($"Not enough free space for the backup: {FormatBytes(needed)} needed, {FormatBytes(free)} available.");
            }

            var warnings = new List<string>();
            var entries = new JsonArray();
            var hashesToRecord = new List<(string Dir, string Sha, long Bytes)>();
            long done = 0;
            AtomicFile.Write(target, stream =>
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (PlannedItem item in items)
                {
                    foreach (PlannedFile file in item.Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(new BackupProgress("Copying", done, total, file.EntryName));
                        ZipArchiveEntry entry = zip.CreateEntry(file.EntryName, CompressionLevel.NoCompression);
                        string sha;
                        long bytes;
                        long start = done;
                        using (Stream output = entry.Open())
                        using (var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, AtomicFile.StreamBufferSize, FileOptions.SequentialScan))
                        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                        {
                            bytes = AtomicFile.CopyBounded(input, output, hash, long.MaxValue, b => progress?.Report(new BackupProgress("Copying", start + b, total, file.EntryName)), cancellationToken);
                            sha = Convert.ToHexStringLower(hash.GetHashAndReset());
                        }

                        done += bytes;
                        if (file.ExpectedSha256 is not null && !string.Equals(sha, file.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            if (item.Kind == CaptureKind.Video && file.IsVideoData)
                            {
                                item.State = BackupItemState.Damaged;
                                item.Reason = "The video file does not match its recorded hash (damaged or modified).";
                                warnings.Add($"Video {item.Id}: {item.Reason} It was backed up as it is and marked damaged.");
                            }
                            else
                            {
                                throw new IOException($"{file.EntryName} changed while it was being backed up. Try again.");
                            }
                        }
                        else if (file.IsVideoData && file.ExpectedSha256 is null && item.State == BackupItemState.Ok)
                        {
                            hashesToRecord.Add((Path.GetDirectoryName(file.Source)!, sha, bytes));
                            warnings.Add($"Video {item.Id}: hash recorded now, not previously verified (saved before SnapForge recorded video hashes).");
                        }

                        entries.Add(new JsonObject { ["name"] = file.EntryName, ["bytes"] = bytes, ["sha256"] = sha });
                        DurabilityFaults.Checkpoint("backup.entry-written:" + file.EntryName);
                    }
                }

                var manifest = new JsonObject
                {
                    ["format"] = BackupFormatId,
                    ["version"] = BackupFormatVersion,
                    ["createdUtc"] = CaptureFormat.Iso(DateTime.UtcNow),
                    ["generator"] = $"{ProductInfo.Name} {typeof(CaptureLibrary).Assembly.GetName().Version}",
                    ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject
                    {
                        ["id"] = i.Id.ToString("D"),
                        ["kind"] = KindName(i.Kind),
                        ["state"] = StateName(i.State),
                        ["inTrash"] = i.InTrash,
                        ["prefix"] = i.Prefix,
                        ["reason"] = i.Reason,
                    })]),
                    ["entries"] = entries,
                    ["excluded"] = new JsonArray([.. excluded.Select(e => (JsonNode)new JsonObject { ["path"] = e.Path, ["reason"] = e.Reason })]),
                };
                using Stream m = zip.CreateEntry(BackupManifestEntry, CompressionLevel.Optimal).Open();
                JsonSerializer.Serialize(m, manifest, AnnotationSerializer.Indented);
            });
            DurabilityFaults.Checkpoint("backup.committed");

            foreach ((string dir, string sha, long bytes) in hashesToRecord)
            {
                RecordVideoHash(dir, sha, bytes);
            }

            progress?.Report(new BackupProgress("Done", total, total, null));
            return new BackupResult(target, [.. items.Select(i => new BackupItem(i.Id, i.Kind, i.State, i.InTrash, i.Reason))], excluded, warnings, done);
        }
    }

    /// <summary>
    /// Restores a backup (version 2, or a legacy version 1 image-only backup). The archive is rejected
    /// (<see cref="InvalidDataException"/>, nothing added) if its version is unknown or newer, its manifest is
    /// malformed, an entry name is not exactly one of the expected patterns (absolute paths and traversal included)
    /// or is duplicated (ignoring case), there are more than <see cref="BackupLimits.MaxEntries"/> entries, sizes
    /// exceed <see cref="BackupLimits"/> or the free space (minus a reserve), an entry's streamed size or hash differs
    /// from the manifest (reading stops at the declared size + 1 byte), or an item does not validate. Everything is
    /// extracted into one staging folder inside the library and verified first; then items are adopted one by one
    /// under a journal. Items whose id already exists are skipped, never overwritten.
    /// </summary>
    public RestoreResult RestoreFromBackup(string zipPath, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        using (EnterWrite())
        {
            // A restore interrupted earlier is finished first, so two journals never coexist.
            if (ResumeInterruptedRestore() is { } resumed)
            {
                RecoveredRestore = resumed;
            }

            string staging = Path.Combine(Root, RestoreStagingPrefix + Guid.NewGuid().ToString("N"));
            try
            {
                StagedBackup staged = ExtractAndValidate(zipPath, staging, progress, cancellationToken);
                DurabilityFaults.Checkpoint("restore.validated");
                WriteRestoreJournal(staging, staged);
                DurabilityFaults.Checkpoint("restore.journal-written");
                return CompleteRestore(staging, staged.Version, staged.Legacy, staged.Items, staged.Excluded, resumed: false, progress);
            }
            finally
            {
                // Without a journal the staging folder was never committed to: remove it. With a journal (an
                // interruption after it was written), it is left for the next open to complete.
                if (!File.Exists(Path.Combine(Root, RestoreJournalFile)) && Directory.Exists(staging))
                {
                    SecureDeleteDirectory(staging);
                }
            }
        }
    }

    /// <summary>
    /// Completes a restore that was interrupted after validation (its journal exists), or removes staging folders of
    /// restores interrupted before validation finished. Called when the library opens. Returns the completed
    /// restore, or null if none was pending.
    /// </summary>
    internal RestoreResult? ResumeInterruptedRestore()
    {
        using (EnterWrite())
        {
            string journalPath = Path.Combine(Root, RestoreJournalFile);
            RestoreResult? result = null;
            if (File.Exists(journalPath))
            {
                JsonObject? journal = null;
                try
                {
                    journal = JsonNode.Parse(File.ReadAllBytes(journalPath)) as JsonObject;
                }
                catch (JsonException)
                {
                }

                if (journal?["format"]?.GetValue<string>() != RestoreJournalFormat || journal["staging"]?.GetValue<string>() is not { } stagingName
                    || !stagingName.StartsWith(RestoreStagingPrefix, StringComparison.Ordinal) || stagingName.IndexOfAny(['/', '\\']) >= 0)
                {
                    // Not one of ours or damaged: keep it (and any staging) for inspection, but stop it blocking restores.
                    File.Move(journalPath, journalPath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture), overwrite: false);
                    return null;
                }

                var items = new List<StagedItem>();
                foreach (JsonObject o in (journal["items"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (StagedItem.FromJson(o) is { } item)
                    {
                        items.Add(item);
                    }
                }

                result = CompleteRestore(
                    Path.Combine(Root, stagingName),
                    journal["backupVersion"]?.GetValue<int>() ?? 0,
                    journal["legacy"]?.GetValue<bool>() ?? false,
                    items,
                    [],
                    resumed: true,
                    progress: null);
            }

            foreach (string dir in Directory.GetDirectories(Root))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith(RestoreStagingPrefix, StringComparison.Ordinal) || name.StartsWith(".import-", StringComparison.Ordinal))
                {
                    SecureDeleteDirectory(dir);
                }
            }

            return result;
        }
    }

    private RestoreResult CompleteRestore(string staging, int version, bool legacy, IReadOnlyList<StagedItem> items, IReadOnlyList<BackupExclusion> excluded, bool resumed, IProgress<BackupProgress>? progress)
    {
        var restored = new List<RestoredItem>();
        var skipped = new List<Guid>();
        int n = 0;
        foreach (StagedItem item in items)
        {
            progress?.Report(new BackupProgress("Adopting", n++, items.Count, item.Id.ToString("D")));
            string from = Path.Combine(staging, item.From.Replace('/', Path.DirectorySeparatorChar));
            string to = Path.Combine(Root, item.To.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(from))
            {
                if (Directory.Exists(to) || Directory.Exists(CaptureFolder(item.Id)) || Directory.Exists(VideoFolder(item.Id)) || Find(item.Id) is not null)
                {
                    skipped.Add(item.Id);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                MoveDirectory(from, to); // atomic: an item is either still staged or fully adopted
                DurabilityFaults.Checkpoint("restore.adopted:" + item.Id.ToString("D"));
            }
            else if (!Directory.Exists(to))
            {
                continue; // neither staged nor adopted (should not happen): nothing to report as restored
            }

            restored.Add(new RestoredItem(item.Id, item.Kind, item.State));
        }

        if (Directory.Exists(staging))
        {
            SecureDeleteDirectory(staging);
        }

        DurabilityFaults.Checkpoint("restore.staging-removed");
        File.Delete(Path.Combine(Root, RestoreJournalFile));
        DurabilityFaults.Checkpoint("restore.completed");
        foreach (RestoredItem r in restored)
        {
            string folder = FolderOf(r.Id);
            if (File.Exists(Path.Combine(folder, CaptureFormat.ManifestFile)) || File.Exists(Path.Combine(folder, VideoManifestFile)))
            {
                Refresh(r.Id);
            }
        }

        return new RestoreResult(version, legacy, restored, skipped, excluded, resumed);
    }

    private void WriteRestoreJournal(string staging, StagedBackup staged)
    {
        var journal = new JsonObject
        {
            ["format"] = RestoreJournalFormat,
            ["version"] = 1,
            ["staging"] = Path.GetFileName(staging),
            ["backupVersion"] = staged.Version,
            ["legacy"] = staged.Legacy,
            ["createdUtc"] = CaptureFormat.Iso(DateTime.UtcNow),
            ["items"] = new JsonArray([.. staged.Items.Select(i => (JsonNode)i.ToJson())]),
        };
        AtomicFile.WriteAllBytes(Path.Combine(Root, RestoreJournalFile), JsonSerializer.SerializeToUtf8Bytes(journal, AnnotationSerializer.Indented));
    }

    // ───────────────────────────── Backup planning ─────────────────────────────

    private (List<PlannedItem> Items, List<BackupExclusion> Excluded) PlanBackup(CancellationToken cancellationToken)
    {
        var items = new List<PlannedItem>();
        var excluded = new List<BackupExclusion>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(Root).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(entry);
            if (Directory.Exists(entry) && (name == "captures" || name == VideosFolderName))
            {
                continue;
            }

            excluded.Add(new BackupExclusion(Relative(entry), RootEntryReason(name, Directory.Exists(entry))));
        }

        foreach ((string area, CaptureKind kind) in new[] { ("captures", CaptureKind.Image), (VideosFolderName, CaptureKind.Video) })
        {
            string areaDir = Path.Combine(Root, area);
            if (!Directory.Exists(areaDir))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(areaDir).Order(StringComparer.Ordinal))
            {
                excluded.Add(new BackupExclusion(Relative(file), IsTemp(file) ? "Temporary file of an interrupted write." : "Not a SnapForge item."));
            }

            foreach (string dir in Directory.GetDirectories(areaDir).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(dir);
                if (!TryParseStrictId(name, out Guid id))
                {
                    excluded.Add(new BackupExclusion(Relative(dir), name.StartsWith(CreatingPrefix, StringComparison.Ordinal) ? "A capture still being created (not committed)." : "Not a SnapForge item (unexpected folder name)."));
                    continue;
                }

                items.Add(kind == CaptureKind.Image ? PlanImage(id, dir, excluded) : PlanVideo(id, dir, excluded));
            }
        }

        return (items, excluded);
    }

    private PlannedItem PlanImage(Guid id, string dir, List<BackupExclusion> excluded)
    {
        bool inTrash = ReadMeta(dir).DeletedUtc is not null;
        try
        {
            CaptureManifest m = CaptureManifest.Read(dir);
            var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (CaptureFileRef r in new[] { m.Base, m.Annotations, m.Thumb }.OfType<CaptureFileRef>())
            {
                if (r.Sha256 is not null)
                {
                    VerifyFileHash(dir, r); // a damaged capture is stored raw instead
                }
                else if (r != m.Thumb)
                {
                    throw new InvalidDataException($"{r.Name} has no recorded hash.");
                }

                expected[r.Name] = r.Sha256;
            }

            HashSet<string> referenced = m.ReferencedFiles();
            var files = new List<PlannedFile>();
            foreach (string file in Directory.GetFiles(dir).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);
                if (referenced.Contains(name))
                {
                    files.Add(new PlannedFile(file, $"captures/{id:D}/{name}", new FileInfo(file).Length, expected.GetValueOrDefault(name), IsVideoData: false));
                }
                else
                {
                    excluded.Add(new BackupExclusion(Relative(file), IsTemp(file) ? "Temporary file of an interrupted write." : CaptureManifest.IsContentFileName(name) ? "Superseded or uncommitted file of an earlier save." : "Not a SnapForge file."));
                }
            }

            ExcludeSubfolders(dir, excluded);
            return new PlannedItem(id, CaptureKind.Image, BackupItemState.Ok, $"captures/{id:D}/", inTrash, null, files);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
        {
            return PlanRaw(id, CaptureKind.Image, dir, BackupItemState.Damaged, inTrash, ex.Message, excluded);
        }
    }

    private PlannedItem PlanVideo(Guid id, string dir, List<BackupExclusion> excluded)
    {
        bool inTrash = ReadMeta(dir).DeletedUtc is not null;
        if (!File.Exists(Path.Combine(dir, VideoManifestFile)))
        {
            return PlanRaw(id, CaptureKind.Video, dir, BackupItemState.Incomplete, inTrash, "The recording was interrupted before it was completed.", excluded);
        }

        try
        {
            JsonObject m = ReadVideoManifest(dir);
            string video = Path.Combine(dir, VideoFile);
            if (!File.Exists(video) || new FileInfo(video).Length != (m["videoBytes"]?.GetValue<long>() ?? -1))
            {
                throw new InvalidDataException(File.Exists(video) ? "The video file is damaged or was modified (its size changed)." : "The video file is missing.");
            }

            string? sha = m[VideoHashKey]?.GetValue<string>();
            var files = new List<PlannedFile>();
            string[] known = [VideoManifestFile, VideoFile, CaptureFormat.ThumbFile, CaptureFormat.MetaFile];
            foreach (string file in Directory.GetFiles(dir).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);
                if (known.Contains(name, StringComparer.Ordinal))
                {
                    bool data = name == VideoFile;
                    files.Add(new PlannedFile(file, $"{VideosFolderName}/{id:D}/{name}", new FileInfo(file).Length, data ? sha : null, data));
                }
                else
                {
                    excluded.Add(new BackupExclusion(Relative(file), IsTemp(file) ? "Temporary file of an interrupted write." : "Not part of the completed video (leftover of recording)."));
                }
            }

            ExcludeSubfolders(dir, excluded);
            return new PlannedItem(id, CaptureKind.Video, BackupItemState.Ok, $"{VideosFolderName}/{id:D}/", inTrash, null, files);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return PlanRaw(id, CaptureKind.Video, dir, BackupItemState.Damaged, inTrash, ex.Message, excluded);
        }
    }

    private PlannedItem PlanRaw(Guid id, CaptureKind kind, string dir, BackupItemState state, bool inTrash, string reason, List<BackupExclusion> excluded)
    {
        string area = kind == CaptureKind.Image ? "captures" : VideosFolderName;
        string prefix = $"{RawPrefix}{area}/{id:D}/";
        var files = new List<PlannedFile>();
        foreach (string file in Directory.GetFiles(dir).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            long length = new FileInfo(file).Length;
            if (IsTemp(file))
            {
                excluded.Add(new BackupExclusion(Relative(file), "Temporary file of an interrupted write."));
            }
            else if (!RawFileName().IsMatch(name))
            {
                excluded.Add(new BackupExclusion(Relative(file), "File name not supported in backups."));
            }
            else if (length > BackupLimits.MaxBytesFor(name))
            {
                excluded.Add(new BackupExclusion(Relative(file), "Too large for a backup entry of this type."));
            }
            else
            {
                files.Add(new PlannedFile(file, prefix + name, length, null, IsVideoData: false));
            }
        }

        ExcludeSubfolders(dir, excluded);
        return new PlannedItem(id, kind, state, prefix, inTrash, reason, files);
    }

    private void ExcludeSubfolders(string dir, List<BackupExclusion> excluded)
    {
        foreach (string sub in Directory.GetDirectories(dir))
        {
            excluded.Add(new BackupExclusion(Relative(sub), "Unexpected folder inside an item."));
        }
    }

    private static void VerifyFileHash(string dir, CaptureFileRef file)
    {
        string path = Path.Combine(dir, file.Name);
        (string sha, long length) = AtomicFile.HashFile(path);
        if ((file.Bytes is long b && b != length) || !string.Equals(sha, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{file.Name} failed its integrity check (the file is damaged or was modified).");
        }
    }

    private static string RootEntryReason(string name, bool isDirectory) =>
        name switch
        {
            "tray.json" => "Recent-captures tray state (recreated automatically; not a library item).",
            RestoreJournalFile => "Bookkeeping of a restore in progress.",
            RelocationJournalFile => "Bookkeeping of a library move.",
            _ when name.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase) => "Temporary file of an interrupted write.",
            _ when name.StartsWith(RestoreStagingPrefix, StringComparison.Ordinal) || name.StartsWith(".import-", StringComparison.Ordinal) => "Temporary folder of an unfinished restore or import.",
            _ => isDirectory ? "Folder unknown to SnapForge (not a library item)." : "File unknown to SnapForge (not a library item).",
        };

    // ───────────────────────────── Restore: validation and extraction ─────────────────────────────

    private StagedBackup ExtractAndValidate(string zipPath, string staging, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("This file is not a valid ZIP archive.", ex);
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries + 1)
            {
                throw new InvalidDataException($"The backup has more than {MaxEntries:N0} entries.");
            }

            var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                if (!byName.TryAdd(e.FullName, e))
                {
                    throw new InvalidDataException($"The backup contains the entry '{e.FullName}' more than once.");
                }
            }

            if (!byName.TryGetValue(BackupManifestEntry, out ZipArchiveEntry? manifestEntry) || manifestEntry.FullName != BackupManifestEntry)
            {
                throw new InvalidDataException("This file is not a SnapForge library backup.");
            }

            JsonObject manifest = ReadBackupManifest(manifestEntry);
            int version = manifest["version"]!.GetValue<int>();
            var declared = new List<DeclaredEntry>();
            List<StagedItem> items;
            var excluded = new List<BackupExclusion>();
            if (version == 1)
            {
                items = PlanLegacyRestore(byName.Values, declared);
            }
            else
            {
                items = PlanV2Restore(manifest, byName, declared, excluded);
            }

            long total = 0;
            foreach (DeclaredEntry d in declared)
            {
                total = checked(total + d.Bytes);
            }

            if (total > BackupLimits.MaxTotalBytes)
            {
                throw new InvalidDataException($"The backup is larger than the supported {FormatBytes(BackupLimits.MaxTotalBytes)}.");
            }

            long free = AvailableFreeSpace(Root);
            if (total > free - BackupLimits.FreeSpaceReserveBytes)
            {
                throw new InvalidDataException($"Not enough free space to restore this backup: {FormatBytes(total + BackupLimits.FreeSpaceReserveBytes)} needed, {FormatBytes(free)} available.");
            }

            Directory.CreateDirectory(staging);
            long done = 0;
            var actualHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DeclaredEntry d in declared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(Path.Combine(staging, d.Entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsUnder(path, staging))
                {
                    throw new InvalidDataException($"Unexpected entry '{d.Entry.FullName}' in backup.");
                }

                long start = done;
                string sha = string.Empty;
                long read = 0;
                AtomicFile.Write(path, target =>
                {
                    using Stream source = d.Entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    read = AtomicFile.CopyBounded(source, target, hash, d.Bytes, b => progress?.Report(new BackupProgress("Verifying", start + b, total, d.Entry.FullName)), cancellationToken);
                    sha = Convert.ToHexStringLower(hash.GetHashAndReset());
                    if (read != d.Bytes)
                    {
                        throw new InvalidDataException($"The entry '{d.Entry.FullName}' is {(read > d.Bytes ? "longer" : "shorter")} than declared (the backup is damaged or was modified).");
                    }

                    if (d.Sha256 is not null && !string.Equals(sha, d.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"The entry '{d.Entry.FullName}' does not match its recorded hash (the backup is damaged or was modified).");
                    }
                });
                actualHashes[d.Entry.FullName] = sha;
                done += read;
                DurabilityFaults.Checkpoint("restore.extracted:" + d.Entry.FullName);
            }

            foreach (StagedItem item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateStagedItem(Path.Combine(staging, item.From.Replace('/', Path.DirectorySeparatorChar)), item, actualHashes);
            }

            return new StagedBackup(version, version == 1, items, excluded);
        }
    }

    private static JsonObject ReadBackupManifest(ZipArchiveEntry entry)
    {
        if (entry.Length > BackupLimits.MaxManifestBytes)
        {
            throw new InvalidDataException("The backup manifest is too large.");
        }

        using var buffer = new MemoryStream();
        using (Stream s = entry.Open())
        {
            if (AtomicFile.CopyBounded(s, buffer, null, BackupLimits.MaxManifestBytes, null, CancellationToken.None) > BackupLimits.MaxManifestBytes)
            {
                throw new InvalidDataException("The backup manifest is too large.");
            }
        }

        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(buffer.ToArray()) as JsonObject ?? throw new InvalidDataException("The backup manifest is malformed.");
            if (manifest["format"]?.GetValue<string>() != BackupFormatId)
            {
                throw new InvalidDataException("This file is not a SnapForge library backup.");
            }

            int version = manifest["version"]?.GetValue<int>() ?? 0;
            if (version is < 1 or > BackupFormatVersion)
            {
                throw new InvalidDataException(version > BackupFormatVersion
                    ? $"This backup was made by a newer version of SnapForge (backup format {version}). Update SnapForge to restore it."
                    : $"Unknown backup format version {version}.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The backup manifest is malformed.", ex);
        }

        return manifest;
    }

    /// <summary>Version 1 (SnapForge 1.0 RC): <c>captures/&lt;id&gt;/&lt;v1 file&gt;</c> only, no hashes; header sizes are capped.</summary>
    private static List<StagedItem> PlanLegacyRestore(IEnumerable<ZipArchiveEntry> entries, List<DeclaredEntry> declared)
    {
        var ids = new SortedSet<Guid>();
        foreach (ZipArchiveEntry e in entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            if (e.FullName == BackupManifestEntry)
            {
                continue;
            }

            Match match = LegacyEntryName().Match(e.FullName);
            if (!match.Success || !TryParseStrictId(match.Groups["id"].Value, out Guid id))
            {
                throw new InvalidDataException($"Unexpected entry '{e.FullName}' in backup.");
            }

            if (e.Length < 0 || e.Length > BackupLimits.MaxBytesFor(e.Name))
            {
                throw new InvalidDataException($"The entry '{e.FullName}' is too large.");
            }

            declared.Add(new DeclaredEntry(e, e.Length, null));
            ids.Add(id);
        }

        return [.. ids.Select(id => new StagedItem(id, CaptureKind.Image, BackupItemState.Ok, $"captures/{id:D}", $"captures/{id:D}"))];
    }

    private static List<StagedItem> PlanV2Restore(JsonObject manifest, Dictionary<string, ZipArchiveEntry> byName, List<DeclaredEntry> declared, List<BackupExclusion> excluded)
    {
        try
        {
            JsonArray entryList = manifest["entries"] as JsonArray ?? throw new InvalidDataException("The backup manifest lists no entries.");
            JsonArray itemList = manifest["items"] as JsonArray ?? throw new InvalidDataException("The backup manifest lists no items.");
            if (entryList.Count > MaxEntries)
            {
                throw new InvalidDataException($"The backup has more than {MaxEntries:N0} entries.");
            }

            var items = new Dictionary<string, StagedItem>(StringComparer.Ordinal);
            var ids = new HashSet<Guid>();
            foreach (JsonObject o in itemList.Select(n => n as JsonObject ?? throw new InvalidDataException("The backup manifest is malformed.")))
            {
                string idText = o["id"]?.GetValue<string>() ?? throw new InvalidDataException("An item has no id.");
                if (!TryParseStrictId(idText, out Guid id) || !ids.Add(id))
                {
                    throw new InvalidDataException($"Invalid or duplicate item id '{idText}'.");
                }

                CaptureKind kind = o["kind"]?.GetValue<string>() switch
                {
                    "image" => CaptureKind.Image,
                    "video" => CaptureKind.Video,
                    var k => throw new InvalidDataException($"Unknown item kind '{k}'."),
                };
                BackupItemState state = o["state"]?.GetValue<string>() switch
                {
                    "ok" => BackupItemState.Ok,
                    "damaged" => BackupItemState.Damaged,
                    "incomplete" => BackupItemState.Incomplete,
                    var s => throw new InvalidDataException($"Unknown item state '{s}'."),
                };
                string area = kind == CaptureKind.Image ? "captures" : VideosFolderName;
                string prefix = o["prefix"]?.GetValue<string>() ?? string.Empty;
                string expectedPrefix = $"{area}/{id:D}/";
                bool raw = prefix == RawPrefix + expectedPrefix;
                if (prefix != expectedPrefix && !raw)
                {
                    throw new InvalidDataException($"Item {id} has an unexpected location '{prefix}'.");
                }

                if (!raw && state == BackupItemState.Incomplete)
                {
                    throw new InvalidDataException($"Item {id}: an incomplete item must be stored raw.");
                }

                items[prefix] = new StagedItem(id, kind, state, prefix.TrimEnd('/'), $"{area}/{id:D}");
            }

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemsWithEntries = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject o in entryList.Select(n => n as JsonObject ?? throw new InvalidDataException("The backup manifest is malformed.")))
            {
                string name = o["name"]?.GetValue<string>() ?? throw new InvalidDataException("An entry has no name.");
                long bytes = o["bytes"]?.GetValue<long>() ?? throw new InvalidDataException($"The entry '{name}' has no size.");
                string sha = o["sha256"]?.GetValue<string>() ?? throw new InvalidDataException($"The entry '{name}' has no hash.");
                if (!listed.Add(name))
                {
                    throw new InvalidDataException($"The backup lists the entry '{name}' more than once.");
                }

                if (!IsValidV2EntryName(name, out string prefix) || !items.ContainsKey(prefix))
                {
                    throw new InvalidDataException($"Unexpected entry '{name}' in backup.");
                }

                if (!CaptureManifest.Sha256Pattern().IsMatch(sha))
                {
                    throw new InvalidDataException($"The entry '{name}' has an invalid hash.");
                }

                string fileName = name[(name.LastIndexOf('/') + 1)..];
                if (bytes < 0 || bytes > BackupLimits.MaxBytesFor(fileName))
                {
                    throw new InvalidDataException($"The entry '{name}' is too large ({FormatBytes(bytes)}).");
                }

                if (!byName.TryGetValue(name, out ZipArchiveEntry? entry) || entry.FullName != name)
                {
                    throw new InvalidDataException($"The entry '{name}' is listed but missing from the backup.");
                }

                if (entry.Length != bytes)
                {
                    throw new InvalidDataException($"The entry '{name}' does not have its declared size.");
                }

                declared.Add(new DeclaredEntry(entry, bytes, sha));
                itemsWithEntries.Add(prefix);
            }

            foreach (string name in byName.Keys)
            {
                if (name != BackupManifestEntry && !listed.Contains(name))
                {
                    throw new InvalidDataException($"Unexpected entry '{name}' in backup (not listed in its manifest).");
                }
            }

            foreach (JsonObject o in (manifest["excluded"] as JsonArray ?? []).OfType<JsonObject>())
            {
                excluded.Add(new BackupExclusion(o["path"]?.GetValue<string>() ?? string.Empty, o["reason"]?.GetValue<string>() ?? string.Empty));
            }

            return [.. items.Where(p => itemsWithEntries.Contains(p.Key)).Select(p => p.Value)
                .OrderBy(i => i.From.StartsWith(RawPrefix, StringComparison.Ordinal) ? 2 : i.Kind == CaptureKind.Image ? 0 : 1)
                .ThenBy(i => i.Id)];
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            throw new InvalidDataException("The backup manifest is malformed.", ex);
        }
    }

    private static bool IsValidV2EntryName(string name, out string prefix)
    {
        prefix = string.Empty;
        Match m = V2EntryName().Match(name);
        if (!m.Success || !TryParseStrictId(m.Groups["id"].Value, out _))
        {
            return false;
        }

        string area = m.Groups["area"].Value, file = m.Groups["file"].Value;
        bool raw = m.Groups["raw"].Success;
        bool fileOk = raw ? RawFileName().IsMatch(file)
            : area == "captures" ? CaptureManifest.IsCaptureFileName(file)
            : file is VideoManifestFile or VideoFile or CaptureFormat.ThumbFile or CaptureFormat.MetaFile;
        prefix = name[..(name.Length - file.Length)];
        return fileOk;
    }

    private static void ValidateStagedItem(string dir, StagedItem item, Dictionary<string, string> actualHashes)
    {
        if (item.State != BackupItemState.Ok)
        {
            return; // damaged/incomplete items are restored as they were backed up
        }

        try
        {
            if (item.Kind == CaptureKind.Image)
            {
                CaptureManifest m = CaptureManifest.Read(dir);
                PixelCodec.Decode(CaptureFormat.ReadVerified(dir, m.Base));
                CaptureFormat.DeserializeAnnotations(CaptureFormat.ReadVerified(dir, m.Annotations));
                if (m.Thumb is { Sha256: not null } thumb)
                {
                    CaptureFormat.ReadVerified(dir, thumb);
                }

                HashSet<string> referenced = m.ReferencedFiles();
                if (Directory.GetFiles(dir).FirstOrDefault(f => !referenced.Contains(Path.GetFileName(f))) is { } extra)
                {
                    throw new InvalidDataException($"{Path.GetFileName(extra)} is not part of the capture.");
                }
            }
            else
            {
                JsonObject m = ReadVideoManifest(dir);
                string video = Path.Combine(dir, VideoFile);
                if (!File.Exists(video) || new FileInfo(video).Length != (m["videoBytes"]?.GetValue<long>() ?? -1))
                {
                    throw new InvalidDataException("The video file is missing or has the wrong size.");
                }

                if (m[VideoHashKey]?.GetValue<string>() is { } sha
                    && !string.Equals(sha, actualHashes.GetValueOrDefault($"{item.From}/{VideoFile}"), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The video does not match its recorded hash.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        {
            throw new InvalidDataException($"Item {item.Id} in the backup does not validate: {ex.Message}", ex);
        }
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    internal static long AvailableFreeSpace(string path)
    {
        if (FreeSpaceOverride.Value is { } over)
        {
            return over(path);
        }

        string? root = Path.GetPathRoot(Path.GetFullPath(path));
        return root is null ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
    }

    internal static bool TryParseStrictId(string text, out Guid id) =>
        Guid.TryParseExact(text, "D", out id) && string.Equals(id.ToString("D"), text, StringComparison.Ordinal);

    private static bool IsTemp(string path) => path.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root)
    {
        string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private string Relative(string path) => Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string KindName(CaptureKind kind) => kind == CaptureKind.Image ? "image" : "video";

    private static string StateName(BackupItemState state) => state switch
    {
        BackupItemState.Ok => "ok",
        BackupItemState.Damaged => "damaged",
        _ => "incomplete",
    };

    internal static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" : $"{bytes:N0} bytes";

    [GeneratedRegex(@"^captures/(?<id>[0-9a-f-]{36})/(manifest\.json|base\.sfpx|annotations\.json|meta\.json|thumb\.sfpx)$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyEntryName();

    [GeneratedRegex(@"^(?<raw>raw/)?(?<area>captures|videos)/(?<id>[0-9a-f-]{36})/(?<file>[^/]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex V2EntryName();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex RawFileName();

    private sealed record PlannedFile(string Source, string EntryName, long Bytes, string? ExpectedSha256, bool IsVideoData);

    private sealed class PlannedItem(Guid id, CaptureKind kind, BackupItemState state, string prefix, bool inTrash, string? reason, List<PlannedFile> files)
    {
        public Guid Id { get; } = id;

        public CaptureKind Kind { get; } = kind;

        public BackupItemState State { get; set; } = state;

        public string Prefix { get; } = prefix;

        public bool InTrash { get; } = inTrash;

        public string? Reason { get; set; } = reason;

        public List<PlannedFile> Files { get; } = files;
    }

    private sealed record DeclaredEntry(ZipArchiveEntry Entry, long Bytes, string? Sha256);

    private sealed record StagedBackup(int Version, bool Legacy, List<StagedItem> Items, List<BackupExclusion> Excluded);

    /// <summary>An item extracted to staging: <see cref="From"/> is relative to the staging folder, <see cref="To"/> to the library root.</summary>
    private sealed record StagedItem(Guid Id, CaptureKind Kind, BackupItemState State, string From, string To)
    {
        public JsonObject ToJson() => new()
        {
            ["id"] = Id.ToString("D"),
            ["kind"] = KindName(Kind),
            ["state"] = StateName(State),
            ["from"] = From,
            ["to"] = To,
        };

        public static StagedItem? FromJson(JsonObject o)
        {
            try
            {
                if (!TryParseStrictId(o["id"]?.GetValue<string>() ?? string.Empty, out Guid id))
                {
                    return null;
                }

                CaptureKind kind = o["kind"]?.GetValue<string>() == "video" ? CaptureKind.Video : CaptureKind.Image;
                string area = kind == CaptureKind.Image ? "captures" : VideosFolderName;
                string from = o["from"]?.GetValue<string>() ?? string.Empty, to = o["to"]?.GetValue<string>() ?? string.Empty;
                if (to != $"{area}/{id:D}" || (from != to && from != RawPrefix + to))
                {
                    return null; // never move anything a journal names outside the expected places
                }

                BackupItemState state = o["state"]?.GetValue<string>() switch
                {
                    "damaged" => BackupItemState.Damaged,
                    "incomplete" => BackupItemState.Incomplete,
                    _ => BackupItemState.Ok,
                };
                return new StagedItem(id, kind, state, from, to);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                return null;
            }
        }
    }
}
