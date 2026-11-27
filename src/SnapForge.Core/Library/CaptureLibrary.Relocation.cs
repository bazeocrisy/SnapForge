using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Library;

/// <summary>A file copied by <see cref="CaptureLibrary.CopyTo"/> and verified at the target.</summary>
/// <param name="RelativePath">Path relative to the library root ('/' separators).</param>
/// <param name="Bytes">Size.</param>
/// <param name="Sha256">SHA-256 of the source bytes, equal to the verified target file.</param>
public sealed record RelocationFile(string RelativePath, long Bytes, string Sha256);

/// <summary>Result of <see cref="CaptureLibrary.CopyTo"/>: everything was copied and verified; nothing was deleted.</summary>
/// <param name="SourceRoot">The library that was copied (left untouched).</param>
/// <param name="TargetRoot">The new location.</param>
/// <param name="JournalPath">The relocation journal at the target, needed by <see cref="CaptureLibrary.DeleteSourceAfterVerifiedMove(string, CancellationToken)"/>.</param>
/// <param name="Files">Every file copied and verified.</param>
/// <param name="SkippedTemporary">SnapForge temporary files and folders that were not copied (relative paths).</param>
/// <param name="ItemCount">Items listed at the target (images and videos, damaged included); equal to the source.</param>
/// <param name="DamagedCount">Damaged items among them (copied as they are).</param>
/// <param name="InterruptedRecordingCount">Interrupted recordings copied (still recoverable at the target).</param>
/// <param name="Warnings">Things the user should know (for example a video that no longer matches its recorded hash).</param>
public sealed record RelocationResult(
    string SourceRoot,
    string TargetRoot,
    string JournalPath,
    IReadOnlyList<RelocationFile> Files,
    IReadOnlyList<string> SkippedTemporary,
    int ItemCount,
    int DamagedCount,
    int InterruptedRecordingCount,
    IReadOnlyList<string> Warnings)
{
    public long TotalBytes => Files.Sum(f => f.Bytes);
}

/// <summary>Result of <see cref="CaptureLibrary.DeleteSourceAfterVerifiedMove(string, CancellationToken)"/>.</summary>
/// <param name="Deleted">Source files deleted (each was verified copied and is unchanged since).</param>
/// <param name="Changed">Listed files kept because they changed after the copy, or their copy at the target is missing or differs.</param>
/// <param name="Missing">Listed files that were already gone from the source.</param>
/// <param name="LeftInPlace">Files under the old location that were not part of the verified copy (unknown or newer), left untouched.</param>
public sealed record SourceCleanupResult(IReadOnlyList<string> Deleted, IReadOnlyList<string> Changed, IReadOnlyList<string> Missing, IReadOnlyList<string> LeftInPlace);

/// <summary>Library relocation refused or failed verification; the source library is unchanged.</summary>
public sealed class RelocationException : IOException
{
    public RelocationException()
    {
    }

    public RelocationException(string message)
        : base(message)
    {
    }

    public RelocationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Moving the library to a new folder (FA-006, FA-007) in two explicit phases:
/// <list type="number">
/// <item><see cref="CopyTo"/> copies every file and folder under the library root (captures, videos, damaged and
/// unknown items, interrupted recordings, other files) except SnapForge temporary files, verifies every copy by hash,
/// opens the target and checks its item counts match, writes a relocation journal, and deletes nothing.</item>
/// <item>After the app has durably saved the new location and the user confirmed,
/// <see cref="DeleteSourceAfterVerifiedMove(string, CancellationToken)"/> deletes only the source files the journal
/// lists as verified copies (and only if unchanged since), and reports anything else it left in place.</item>
/// </list>
/// </summary>
public sealed partial class CaptureLibrary
{
    public const string RelocationJournalFile = "relocation-journal.json";
    private const string RelocationJournalFormat = "snapforge.relocation-journal";

    /// <summary>
    /// Copies the whole library to <paramref name="newRoot"/> and verifies it (see the class remarks). Refuses before
    /// copying anything if the target is this library, inside it, already holds a library or any file that would be
    /// overwritten, lacks free space, or a source file cannot be read. Throws <see cref="RelocationException"/> if a
    /// copy fails verification; the source is never modified. Cancellable.
    /// </summary>
    public RelocationResult CopyTo(string newRoot, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newRoot);
        string target = Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar);
        string source = Root.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The new location is the current library folder.", nameof(newRoot));
        }

        if (IsUnder(target, source))
        {
            throw new ArgumentException("The new location cannot be inside the current library.", nameof(newRoot));
        }

        if (IsUnder(source, target))
        {
            throw new ArgumentException("The new location cannot be a folder that contains the current library.", nameof(newRoot));
        }

        lock (_writeLock)
        {
            // ── Plan and preflight: nothing is written until every check passed.
            var files = new List<(string Source, string Relative, long Bytes)>();
            var skipped = new List<string>();
            CollectRelocationFiles(Root, files, skipped);
            if ((Directory.Exists(Path.Combine(target, "captures")) && Directory.EnumerateFileSystemEntries(Path.Combine(target, "captures")).Any())
                || (Directory.Exists(Path.Combine(target, VideosFolderName)) && Directory.EnumerateFileSystemEntries(Path.Combine(target, VideosFolderName)).Any())
                || File.Exists(Path.Combine(target, RelocationJournalFile)))
            {
                throw new IOException("The chosen folder already contains a SnapForge library. Choose an empty folder.");
            }

            var unreadable = new List<string>();
            foreach ((string file, string rel, _) in files)
            {
                if (File.Exists(Path.Combine(target, rel)))
                {
                    throw new RelocationException($"The chosen folder already contains '{rel}', which would be overwritten. Choose an empty folder.");
                }

                try
                {
                    using var probe = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadable.Add($"{rel} ({ex.Message})");
                }
            }

            if (unreadable.Count > 0)
            {
                throw new RelocationException("Nothing was copied: these files cannot be read: " + string.Join("; ", unreadable.Take(10)) + (unreadable.Count > 10 ? $" and {unreadable.Count - 10} more" : string.Empty));
            }

            long total = files.Sum(f => f.Bytes);
            Directory.CreateDirectory(target);
            long free = AvailableFreeSpace(target);
            if (free < total + BackupLimits.FreeSpaceReserveBytes)
            {
                throw new RelocationException($"Not enough free space at the new location: {FormatBytes(total + BackupLimits.FreeSpaceReserveBytes)} needed, {FormatBytes(free)} available. Nothing was copied.");
            }

            // ── Copy (each file atomically, hashing the source bytes as they stream) and verify each copy.
            var copied = new List<RelocationFile>();
            long done = 0;
            foreach ((string file, string rel, _) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string dest = Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar));
                long start = done;
                (string sha, long bytes) = AtomicFile.CopyFile(file, dest, b => progress?.Report(new BackupProgress("Copying", start + b, total, rel)), cancellationToken);
                DurabilityFaults.Checkpoint("relocate.copied:" + rel);
                done += bytes;
                copied.Add(new RelocationFile(rel, bytes, sha));
            }

            done = 0;
            foreach (RelocationFile f in copied)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long start = done;
                (string sha, long bytes) = AtomicFile.HashFile(Path.Combine(target, f.RelativePath.Replace('/', Path.DirectorySeparatorChar)), b => progress?.Report(new BackupProgress("Verifying", start + b, total, f.RelativePath)), cancellationToken);
                DurabilityFaults.Checkpoint("relocate.verified:" + f.RelativePath);
                if (bytes != f.Bytes || !string.Equals(sha, f.Sha256, StringComparison.Ordinal))
                {
                    throw new RelocationException($"The copy of '{f.RelativePath}' at the new location does not match the original. The original library was left unchanged.");
                }

                done += bytes;
            }

            // ── Open both without changing them and compare what they list.
            CaptureLibrary before = OpenForInspection(Root);
            CaptureLibrary after = OpenForInspection(target);
            var expectedIds = before.Records.Select(r => (r.Id, r.Kind, Damaged: r.Damage is not null)).Order().ToList();
            var actualIds = after.Records.Select(r => (r.Id, r.Kind, Damaged: r.Damage is not null)).Order().ToList();
            int interrupted = before.InterruptedRecordings().Count;
            if (!expectedIds.SequenceEqual(actualIds) || interrupted != after.InterruptedRecordings().Count)
            {
                throw new RelocationException($"The new location lists {actualIds.Count} item(s) ({actualIds.Count(i => i.Damaged)} damaged) but the library has {expectedIds.Count} ({expectedIds.Count(i => i.Damaged)} damaged). The original library was left unchanged.");
            }

            var warnings = new List<string>();
            foreach (CaptureRecord v in after.Records.Where(r => r.IsVideo && r.Damage is null))
            {
                string rel = $"{VideosFolderName}/{v.Id:D}/{VideoFile}";
                string? recorded = TryReadVideoHash(Path.Combine(target, VideosFolderName, v.Id.ToString("D")));
                RelocationFile? f = copied.Find(c => c.RelativePath == rel);
                if (recorded is not null && f is not null && !string.Equals(recorded, f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Video {v.Id}: it does not match its recorded hash (it was already damaged or modified in the old location; copied as it is).");
                }
            }

            // ── Journal: the verified list that source clean-up relies on.
            string journalPath = Path.Combine(target, RelocationJournalFile);
            var journal = new JsonObject
            {
                ["format"] = RelocationJournalFormat,
                ["version"] = 1,
                ["sourceRoot"] = Root,
                ["targetRoot"] = target,
                ["verifiedUtc"] = CaptureFormat.Iso(DateTime.UtcNow),
                ["itemCount"] = actualIds.Count,
                ["files"] = new JsonArray([.. copied.Select(f => (JsonNode)new JsonObject { ["path"] = f.RelativePath, ["bytes"] = f.Bytes, ["sha256"] = f.Sha256 })]),
            };
            AtomicFile.WriteAllBytes(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal, AnnotationSerializer.Indented));
            DurabilityFaults.Checkpoint("relocate.journal-written");
            return new RelocationResult(Root, target, journalPath, copied, skipped, actualIds.Count, actualIds.Count(i => i.Damaged), interrupted, warnings);
        }
    }

    /// <summary>
    /// Copies and verifies the library at <paramref name="newRoot"/> (<see cref="CopyTo"/>) and returns the opened
    /// new library. It never deletes the old copy: call <see cref="DeleteSourceAfterVerifiedMove(string, CancellationToken)"/>
    /// with the journal at the new location once the new path is saved and the user confirmed. Same folder: returns this.
    /// </summary>
    public CaptureLibrary MoveTo(string newRoot)
    {
        if (string.Equals(Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar), Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        RelocationResult result = CopyTo(newRoot);
        return Open(result.TargetRoot);
    }

    /// <summary>Deletes the source files of a verified move (see the overload taking the journal path).</summary>
    public SourceCleanupResult DeleteSourceAfterVerifiedMove(RelocationResult verifiedCopy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedCopy);
        if (!string.Equals(Path.GetFullPath(verifiedCopy.SourceRoot), Root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("This relocation was made from another library.", nameof(verifiedCopy));
        }

        SourceCleanupResult result;
        lock (_writeLock)
        {
            result = DeleteSourceAfterVerifiedMove(verifiedCopy.JournalPath, cancellationToken);
            lock (_gate)
            {
                _records.Clear();
            }
        }

        RaiseChanged([]);
        return result;
    }

    /// <summary>
    /// Phase two of a move. Only call it after the new library location has been durably saved and the user
    /// confirmed. Deletes (images and small files overwritten first) exactly the source files that the journal at
    /// <paramref name="relocationJournalPath"/> lists as verified copies, each only if it is unchanged since the
    /// copy and its copy at the target is still present with the same size. Everything else under the old location
    /// (unknown files, files written after the copy) is left in place and reported. Empty folders are removed.
    /// </summary>
    public static SourceCleanupResult DeleteSourceAfterVerifiedMove(string relocationJournalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relocationJournalPath);
        JsonObject journal = JsonNode.Parse(File.ReadAllBytes(relocationJournalPath)) as JsonObject ?? throw new InvalidDataException("The relocation journal is damaged.");
        if (journal["format"]?.GetValue<string>() != RelocationJournalFormat || journal["version"]?.GetValue<int>() != 1)
        {
            throw new InvalidDataException("Not a SnapForge relocation journal.");
        }

        string sourceRoot = Path.GetFullPath(journal["sourceRoot"]?.GetValue<string>() ?? throw new InvalidDataException("The relocation journal names no source."));
        string targetRoot = Path.GetFullPath(journal["targetRoot"]?.GetValue<string>() ?? throw new InvalidDataException("The relocation journal names no target."));
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(relocationJournalPath))!.TrimEnd(Path.DirectorySeparatorChar), targetRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The relocation journal is not at the location it describes.");
        }

        var deleted = new List<string>();
        var changed = new List<string>();
        var missing = new List<string>();
        foreach (JsonObject f in (journal["files"] as JsonArray ?? []).OfType<JsonObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rel = f["path"]?.GetValue<string>() ?? string.Empty;
            string src = Path.GetFullPath(Path.Combine(sourceRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            string dst = Path.GetFullPath(Path.Combine(targetRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (rel.Length == 0 || !IsUnder(src, sourceRoot) || !IsUnder(dst, targetRoot))
            {
                continue; // never touch anything outside the two roots
            }

            long bytes = f["bytes"]?.GetValue<long>() ?? -1;
            string sha = f["sha256"]?.GetValue<string>() ?? string.Empty;
            if (!File.Exists(src))
            {
                missing.Add(rel);
                continue;
            }

            if (!File.Exists(dst) || new FileInfo(dst).Length != bytes || new FileInfo(src).Length != bytes
                || !string.Equals(AtomicFile.HashFile(src, null, cancellationToken).Sha256, sha, StringComparison.Ordinal))
            {
                changed.Add(rel);
                continue;
            }

            DeleteLibraryFile(src);
            DurabilityFaults.Checkpoint("relocate.source-deleted:" + rel);
            deleted.Add(rel);
        }

        var leftovers = new List<string>();
        if (Directory.Exists(sourceRoot))
        {
            leftovers.AddRange(Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(sourceRoot, p).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal));
            foreach (string dir in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }

        journal["sourceCleanupUtc"] = CaptureFormat.Iso(DateTime.UtcNow);
        AtomicFile.WriteAllBytes(relocationJournalPath, JsonSerializer.SerializeToUtf8Bytes(journal, AnnotationSerializer.Indented));
        return new SourceCleanupResult(deleted, changed, missing, leftovers);
    }

    /// <summary>Every file under the root to relocate, excluding SnapForge temporary files and folders (reported).</summary>
    private static void CollectRelocationFiles(string root, List<(string Source, string Relative, long Bytes)> files, List<string> skipped)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            foreach (string sub in Directory.GetDirectories(dir).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(sub);
                string rel = Path.GetRelativePath(root, sub).Replace(Path.DirectorySeparatorChar, '/');
                if ((dir == root && (name.StartsWith(RestoreStagingPrefix, StringComparison.Ordinal) || name.StartsWith(".import-", StringComparison.Ordinal)))
                    || name.StartsWith(CreatingPrefix, StringComparison.Ordinal))
                {
                    skipped.Add(rel + "/");
                    continue;
                }

                pending.Push(sub);
            }

            // Manifests last within each folder, so a copy interrupted midway is never listed as a complete item.
            foreach (string file in Directory.GetFiles(dir).OrderBy(f => Path.GetFileName(f) is CaptureFormat.ManifestFile or VideoManifestFile ? 1 : 0).ThenBy(f => f, StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (file.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase) || (dir == root && Path.GetFileName(file) == RelocationJournalFile))
                {
                    skipped.Add(rel);
                    continue;
                }

                files.Add((file, rel, new FileInfo(file).Length));
            }
        }
    }

    private static string? TryReadVideoHash(string dir)
    {
        try
        {
            return ReadVideoManifest(dir)[VideoHashKey]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Opens and indexes a library without changing anything on disk (no clean-up, no recovery).</summary>
    internal static CaptureLibrary OpenForInspection(string root)
    {
        var library = new CaptureLibrary(root) { _inspectOnly = true };
        if (Directory.Exists(library.CapturesDirectory))
        {
            library.Reindex();
        }

        return library;
    }
}
