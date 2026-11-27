namespace SnapForge.Core.Library;

/// <summary>
/// Documented limits applied when restoring a backup (FA-017/FA-023). Anything beyond them is rejected before
/// extraction; while extracting, no entry is read beyond its declared size + 1 byte.
/// </summary>
public static class BackupLimits
{
    /// <summary>Most entries a backup may contain.</summary>
    public const int MaxEntries = 200_000;

    /// <summary>Largest backup manifest (snapforge-backup.json).</summary>
    public const long MaxManifestBytes = 64L * 1024 * 1024;

    /// <summary>Largest JSON or other small file (manifests, metadata, annotations, markers).</summary>
    public const long MaxSmallFileBytes = 64L * 1024 * 1024;

    /// <summary>Largest pixel file (.sfpx): the 1 GiB image budget plus compression overhead.</summary>
    public const long MaxImageFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Largest single video file (.mp4).</summary>
    public const long MaxVideoBytes = 512L * 1024 * 1024 * 1024;

    /// <summary>Largest total uncompressed size of a backup.</summary>
    public const long MaxTotalBytes = 2L * 1024 * 1024 * 1024 * 1024;

    /// <summary>Free space that must remain on the library's drive after a restore, and on the target after a backup.</summary>
    public const long FreeSpaceReserveBytes = 256L * 1024 * 1024;

    /// <summary>The per-entry limit for a file name (by extension).</summary>
    public static long MaxBytesFor(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? MaxVideoBytes
            : fileName.EndsWith(".sfpx", StringComparison.OrdinalIgnoreCase) ? MaxImageFileBytes
            : MaxSmallFileBytes;
    }
}

/// <summary>How an item was stored in a backup.</summary>
public enum BackupItemState
{
    /// <summary>Complete and verified: restored only if it validates again.</summary>
    Ok,

    /// <summary>Damaged in the library (or found damaged while backing up): copied raw, restored as it was.</summary>
    Damaged,

    /// <summary>An interrupted recording (no manifest): copied raw, restored as it was so it can still be recovered.</summary>
    Incomplete,
}

/// <summary>Something under the library folder that a backup did not include, and why.</summary>
/// <param name="Path">Path relative to the library root ('/' separators).</param>
/// <param name="Reason">Why it was not included.</param>
public sealed record BackupExclusion(string Path, string Reason);

/// <summary>One item in a backup.</summary>
/// <param name="Id">The item id.</param>
/// <param name="Kind">Image or video.</param>
/// <param name="State">How it was stored.</param>
/// <param name="InTrash">True if the item was in the Trash.</param>
/// <param name="Reason">For damaged or incomplete items: what is wrong.</param>
public sealed record BackupItem(Guid Id, CaptureKind Kind, BackupItemState State, bool InTrash, string? Reason);

/// <summary>Progress of a backup or restore.</summary>
/// <param name="Phase">What is happening ("Checking", "Copying", "Verifying", "Adopting").</param>
/// <param name="BytesDone">Bytes processed so far.</param>
/// <param name="BytesTotal">Bytes to process.</param>
/// <param name="Current">The entry or item being processed.</param>
public sealed record BackupProgress(string Phase, long BytesDone, long BytesTotal, string? Current);

/// <summary>Result of <see cref="CaptureLibrary.CreateBackup"/>.</summary>
/// <param name="Path">The backup file.</param>
/// <param name="Items">Every item in the backup (images, videos, Trash, damaged and incomplete items).</param>
/// <param name="Excluded">Everything under the library folder that is not in the backup, with the reason.</param>
/// <param name="Warnings">Things the user should know (an item found damaged while copying, a legacy video hash recorded now).</param>
/// <param name="TotalBytes">Uncompressed bytes stored.</param>
public sealed record BackupResult(string Path, IReadOnlyList<BackupItem> Items, IReadOnlyList<BackupExclusion> Excluded, IReadOnlyList<string> Warnings, long TotalBytes)
{
    public int ImageCount => Items.Count(i => i.Kind == CaptureKind.Image && i.State == BackupItemState.Ok);

    public int VideoCount => Items.Count(i => i.Kind == CaptureKind.Video && i.State == BackupItemState.Ok);

    public int TrashCount => Items.Count(i => i.InTrash);

    public int DamagedOrIncompleteCount => Items.Count(i => i.State != BackupItemState.Ok);
}

/// <summary>An item restored from a backup.</summary>
/// <param name="Id">The item id (kept from the backup).</param>
/// <param name="Kind">Image or video.</param>
/// <param name="State">Ok, or Damaged/Incomplete for an item restored raw as it was backed up.</param>
public sealed record RestoredItem(Guid Id, CaptureKind Kind, BackupItemState State);

/// <summary>Result of <see cref="CaptureLibrary.RestoreFromBackup"/> (or of resuming an interrupted restore).</summary>
/// <param name="BackupVersion">Format version of the backup (1 = legacy, 2 = current).</param>
/// <param name="IsLegacyImageOnlyBackup">True for a version 1 backup: it holds images only (no videos, no damaged items) and has no per-entry hashes.</param>
/// <param name="Restored">Items added to the library.</param>
/// <param name="SkippedExisting">Items not restored because an item with the same id already exists (never overwritten).</param>
/// <param name="ExcludedAtBackup">What the backup itself says it did not include, with reasons.</param>
/// <param name="Resumed">True if this completes a restore that was interrupted (found at start-up).</param>
public sealed record RestoreResult(
    int BackupVersion,
    bool IsLegacyImageOnlyBackup,
    IReadOnlyList<RestoredItem> Restored,
    IReadOnlyList<Guid> SkippedExisting,
    IReadOnlyList<BackupExclusion> ExcludedAtBackup,
    bool Resumed);
