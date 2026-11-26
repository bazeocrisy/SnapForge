namespace SnapForge.Core.Library;

/// <summary>User-editable metadata of a capture (stored in meta.json).</summary>
public sealed record CaptureMetadata
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Virtual folder name ("" = none). Folders organise the library; they are not file-system folders.</summary>
    public string Folder { get; init; } = string.Empty;

    public bool Favorite { get; init; }

    /// <summary>Set when the capture is in the Trash.</summary>
    public DateTime? DeletedUtc { get; init; }

    public DateTime? LastOpenedUtc { get; init; }
}

/// <summary>What a library item is.</summary>
public enum CaptureKind
{
    Image,
    Video,
}

/// <summary>Index entry for one capture: metadata plus facts from its manifest.</summary>
public sealed record CaptureRecord
{
    public required Guid Id { get; init; }

    public required CaptureMetadata Meta { get; init; }

    public DateTime CreatedUtc { get; init; }

    public DateTime ModifiedUtc { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public int AnnotationCount { get; init; }

    /// <summary>True if the stored base image still contains original pixels under at least one redaction.</summary>
    public bool HasReversibleRedactions { get; init; }

    public DateTime? SanitizedUtc { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>Non-null when the capture failed integrity checks (it is listed but cannot be opened).</summary>
    public string? Damage { get; init; }

    public CaptureKind Kind { get; init; } = CaptureKind.Image;

    /// <summary>Video length in milliseconds (videos only).</summary>
    public long DurationMs { get; init; }

    /// <summary>Video frame rate (videos only).</summary>
    public int FramesPerSecond { get; init; }

    /// <summary>True if the video has an audio track (videos only).</summary>
    public bool HasAudio { get; init; }

    /// <summary>True if the video was salvaged after the recording was interrupted (videos only).</summary>
    public bool Recovered { get; init; }

    /// <summary>True if the video was kept as fragmented MP4 (could not be optimised; plays, but less widely).</summary>
    public bool VideoFragmented { get; init; }

    public bool IsVideo => Kind == CaptureKind.Video;

    public string Title => Meta.Title;

    public bool IsDeleted => Meta.DeletedUtc is not null;

    public bool IsFavorite => Meta.Favorite;

    /// <summary>Most recent of capture time and last time it was opened in the Editor.</summary>
    public DateTime RecentActivityUtc => Meta.LastOpenedUtc is { } o && o > CreatedUtc ? o : CreatedUtc;
}
