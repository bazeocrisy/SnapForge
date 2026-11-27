using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Library;

/// <summary>Outcome of verifying one library item (FA-008).</summary>
public enum IntegrityStatus
{
    /// <summary>Every file matched its recorded SHA-256 and size.</summary>
    Verified,

    /// <summary>A legacy video had no recorded hash: it was computed now and recorded, but not previously verified.</summary>
    HashRecordedNow,

    /// <summary>A file is missing, has the wrong size or hash, or cannot be read or decoded.</summary>
    Damaged,
}

/// <summary>Result of <see cref="CaptureLibrary.VerifyIntegrity"/> for one item.</summary>
/// <param name="Id">The item.</param>
/// <param name="Kind">Image or video.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Message">A user-facing explanation.</param>
/// <param name="Bytes">Bytes hashed.</param>
public sealed record ItemIntegrityReport(Guid Id, CaptureKind Kind, IntegrityStatus Status, string Message, long Bytes);

/// <summary>Progress of a library-wide integrity scan or a streamed copy.</summary>
/// <param name="ItemsDone">Items finished.</param>
/// <param name="ItemsTotal">Items to do.</param>
/// <param name="BytesDone">Bytes processed.</param>
/// <param name="BytesTotal">Bytes to process (an estimate from file sizes).</param>
/// <param name="CurrentId">The item being processed.</param>
public sealed record LibraryProgress(int ItemsDone, int ItemsTotal, long BytesDone, long BytesTotal, Guid? CurrentId);

/// <summary>Result of <see cref="CaptureLibrary.VerifyLibrary"/>.</summary>
/// <param name="Items">One report per listed item (damaged items included).</param>
public sealed record LibraryIntegrityReport(IReadOnlyList<ItemIntegrityReport> Items)
{
    public int VerifiedCount => Items.Count(i => i.Status == IntegrityStatus.Verified);

    public int HashRecordedNowCount => Items.Count(i => i.Status == IntegrityStatus.HashRecordedNow);

    public int DamagedCount => Items.Count(i => i.Status == IntegrityStatus.Damaged);
}

/// <summary>Result of <see cref="CaptureLibrary.ExportVideo"/>.</summary>
/// <param name="Path">The exported file.</param>
/// <param name="Bytes">Its size.</param>
/// <param name="Sha256">Its SHA-256 (equal to the library's recorded hash).</param>
/// <param name="Status">Verified, or HashRecordedNow for a legacy video that had no recorded hash.</param>
public sealed record VideoExportResult(string Path, long Bytes, string Sha256, IntegrityStatus Status);

/// <summary>
/// Integrity verification (FA-008). Images are verified by hash on every load; videos record a streamed SHA-256 when
/// completed. Indexing at start-up checks only video lengths (hashing gigabytes at every start would be too slow);
/// full verification happens here, and during backup, restore, relocation and video export.
/// </summary>
public sealed partial class CaptureLibrary
{
    public const string VideoHashKey = "videoSha256";

    /// <summary>
    /// Verifies one item: an image's base, annotations and (v2) thumbnail against the manifest hashes and sizes,
    /// and that they decode; a video's MP4 against its recorded size and streamed SHA-256. A legacy video without a
    /// recorded hash gets one now (<see cref="IntegrityStatus.HashRecordedNow"/>: hash recorded now, not previously
    /// verified). Never throws for damage; throws <see cref="KeyNotFoundException"/> for an unknown id.
    /// </summary>
    public ItemIntegrityReport VerifyIntegrity(Guid id, IProgress<long>? bytesProgress = null, CancellationToken cancellationToken = default)
    {
        string dir = ExistingFolder(id);
        return IsVideoFolder(dir) ? VerifyVideo(id, dir, bytesProgress, cancellationToken) : VerifyImage(id, dir);
    }

    /// <summary>Verifies every listed item (see <see cref="VerifyIntegrity"/>). Cancellable; reports progress.</summary>
    public LibraryIntegrityReport VerifyLibrary(IProgress<LibraryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CaptureRecord> records = Records;
        long total = records.Sum(r => r.SizeBytes), done = 0;
        var reports = new List<ItemIntegrityReport>();
        for (int i = 0; i < records.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureRecord r = records[i];
            progress?.Report(new LibraryProgress(i, records.Count, done, total, r.Id));
            long start = done;
            ItemIntegrityReport report;
            try
            {
                report = VerifyIntegrity(r.Id, new SyncProgress<long>(b => done = start + b), cancellationToken);
            }
            catch (KeyNotFoundException ex)
            {
                report = new ItemIntegrityReport(r.Id, r.Kind, IntegrityStatus.Damaged, ex.Message, 0);
            }

            done = start + r.SizeBytes;
            reports.Add(report);
        }

        progress?.Report(new LibraryProgress(records.Count, records.Count, total, total, null));
        return new LibraryIntegrityReport(reports);
    }

    /// <summary>
    /// Copies a video out of the library to <paramref name="targetPath"/> (temp + flush + rename), hashing while it
    /// streams; if the bytes do not match the recorded hash the target is not created and
    /// <see cref="InvalidDataException"/> is thrown. Use this instead of a plain file copy for exports.
    /// </summary>
    public VideoExportResult ExportVideo(Guid id, string targetPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        string dir = VideoFolder(id);
        JsonObject manifest = ReadVideoManifest(dir);
        long expectedBytes = manifest["videoBytes"]?.GetValue<long>() ?? -1;
        string? expectedSha = manifest[VideoHashKey]?.GetValue<string>();
        string source = Path.Combine(dir, VideoFile);
        if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The export target is the library's own video file.", nameof(targetPath));
        }

        (string sha, long bytes) = (string.Empty, 0L);
        AtomicFile.Write(targetPath, target =>
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, AtomicFile.StreamBufferSize, FileOptions.SequentialScan);
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            bytes = AtomicFile.CopyBounded(input, target, hash, long.MaxValue, progress is null ? null : progress.Report, cancellationToken);
            sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (bytes != expectedBytes || (expectedSha is not null && !string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("The video in the library is damaged or was modified (it does not match its recorded hash); it was not exported.");
            }
        });

        if (expectedSha is null)
        {
            RecordVideoHash(dir, sha, bytes);
            return new VideoExportResult(targetPath, bytes, sha, IntegrityStatus.HashRecordedNow);
        }

        return new VideoExportResult(targetPath, bytes, sha, IntegrityStatus.Verified);
    }

    private static ItemIntegrityReport VerifyImage(Guid id, string dir)
    {
        long bytes = 0;
        try
        {
            CaptureManifest m = CaptureManifest.Read(dir);
            byte[] baseBytes = CaptureFormat.ReadVerified(dir, m.Base);
            byte[] annBytes = CaptureFormat.ReadVerified(dir, m.Annotations);
            bytes = baseBytes.Length + annBytes.Length;
            if (m.Thumb is { Sha256: not null } thumb)
            {
                bytes += CaptureFormat.ReadVerified(dir, thumb).Length;
            }

            PixelCodec.Decode(baseBytes);
            CaptureFormat.DeserializeAnnotations(annBytes);
            return new ItemIntegrityReport(id, CaptureKind.Image, IntegrityStatus.Verified, "All files match their recorded hashes.", bytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException or ArgumentException)
        {
            return new ItemIntegrityReport(id, CaptureKind.Image, IntegrityStatus.Damaged, ex.Message, bytes);
        }
    }

    private ItemIntegrityReport VerifyVideo(Guid id, string dir, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        try
        {
            JsonObject m = ReadVideoManifest(dir);
            string video = Path.Combine(dir, VideoFile);
            if (!File.Exists(video))
            {
                return new ItemIntegrityReport(id, CaptureKind.Video, IntegrityStatus.Damaged, "The video file is missing.", 0);
            }

            long expectedBytes = m["videoBytes"]?.GetValue<long>() ?? -1;
            if (new FileInfo(video).Length != expectedBytes)
            {
                return new ItemIntegrityReport(id, CaptureKind.Video, IntegrityStatus.Damaged, "The video file is damaged or was modified (its size changed).", 0);
            }

            (string sha, long bytes) = AtomicFile.HashFile(video, progress is null ? null : progress.Report, cancellationToken);
            string? expected = m[VideoHashKey]?.GetValue<string>();
            if (expected is null)
            {
                RecordVideoHash(dir, sha, bytes);
                return new ItemIntegrityReport(id, CaptureKind.Video, IntegrityStatus.HashRecordedNow, "Hash recorded now, not previously verified (this video was saved before SnapForge recorded video hashes).", bytes);
            }

            return string.Equals(sha, expected, StringComparison.OrdinalIgnoreCase)
                ? new ItemIntegrityReport(id, CaptureKind.Video, IntegrityStatus.Verified, "The video matches its recorded hash.", bytes)
                : new ItemIntegrityReport(id, CaptureKind.Video, IntegrityStatus.Damaged, "The video file is damaged or was modified (its content does not match its recorded hash).", bytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return new ItemIntegrityReport(id, CaptureKind.Video, IntegrityStatus.Damaged, ex.Message, 0);
        }
    }

    /// <summary>Records the hash of a legacy video (computed on first verification) in its manifest.</summary>
    private void RecordVideoHash(string dir, string sha, long bytes)
    {
        lock (_writeLock)
        {
            JsonObject m = ReadVideoManifest(dir);
            if (m[VideoHashKey] is not null || (m["videoBytes"]?.GetValue<long>() ?? -1) != bytes)
            {
                return;
            }

            m[VideoHashKey] = sha;
            m["videoHashRecordedUtc"] = CaptureFormat.Iso(DateTime.UtcNow);
            AtomicFile.WriteAllBytes(Path.Combine(dir, VideoManifestFile), JsonSerializer.SerializeToUtf8Bytes(m, AnnotationSerializer.Indented));
        }
    }

    private static JsonObject ReadVideoManifest(string dir)
    {
        string file = Path.Combine(dir, VideoManifestFile);
        if (!File.Exists(file))
        {
            throw new KeyNotFoundException("This item is not a completed video in the library.");
        }

        JsonObject m;
        try
        {
            m = JsonNode.Parse(File.ReadAllBytes(file)) as JsonObject ?? throw new InvalidDataException("Manifest is not an object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The video manifest is damaged.", ex);
        }

        CheckVideoVersion(m);
        if (m[VideoHashKey]?.GetValue<string>() is { } sha && !CaptureManifest.Sha256Pattern().IsMatch(sha))
        {
            throw new InvalidDataException("The video manifest has an invalid hash.");
        }

        return m;
    }
}

/// <summary>An <see cref="IProgress{T}"/> that reports synchronously on the calling thread (unlike <see cref="Progress{T}"/>).</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
