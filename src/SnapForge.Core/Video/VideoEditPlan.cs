namespace SnapForge.Core.Video;

/// <summary>A half-open time range [Start, End) in milliseconds of the source video.</summary>
public readonly record struct TimeRangeMs(long Start, long End)
{
    public long Length => End - Start;

    public bool Contains(long ms) => ms >= Start && ms < End;
}

/// <summary>
/// What to keep from a recording: an optional trimmed start/end plus any number of removed sections. Produces the
/// ordered, non-overlapping list of kept ranges and maps source times to output times. The original file is never
/// modified by an edit; exporting writes a new file.
/// </summary>
public sealed class VideoEditPlan
{
    /// <summary>Shortest output an export will produce (shorter edits are rejected with a clear message).</summary>
    public const long MinimumOutputMs = 100;

    private readonly List<TimeRangeMs> _removed = [];

    public VideoEditPlan(long durationMs)
    {
        if (durationMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "The video has no duration.");
        }

        DurationMs = durationMs;
        TrimEndMs = durationMs;
    }

    public long DurationMs { get; }

    /// <summary>Everything before this time is cut.</summary>
    public long TrimStartMs { get; private set; }

    /// <summary>Everything from this time on is cut.</summary>
    public long TrimEndMs { get; private set; }

    /// <summary>Removed sections as the user entered them (merged and clipped in <see cref="KeptRanges"/>).</summary>
    public IReadOnlyList<TimeRangeMs> RemovedSections => _removed;

    /// <summary>True when the plan keeps the whole video (nothing to export).</summary>
    public bool IsUnchanged => TrimStartMs == 0 && TrimEndMs == DurationMs && _removed.Count == 0;

    public void SetTrim(long startMs, long endMs)
    {
        startMs = Math.Clamp(startMs, 0, DurationMs);
        endMs = Math.Clamp(endMs, 0, DurationMs);
        if (endMs - startMs < MinimumOutputMs)
        {
            throw new ArgumentException($"The trimmed video must be at least {MinimumOutputMs} ms long.", nameof(endMs));
        }

        TrimStartMs = startMs;
        TrimEndMs = endMs;
    }

    /// <summary>Removes [startMs, endMs) (in either order). Throws if nothing would be left.</summary>
    public void RemoveSection(long startMs, long endMs)
    {
        if (endMs < startMs)
        {
            (startMs, endMs) = (endMs, startMs);
        }

        startMs = Math.Clamp(startMs, 0, DurationMs);
        endMs = Math.Clamp(endMs, 0, DurationMs);
        if (endMs <= startMs)
        {
            return;
        }

        _removed.Add(new TimeRangeMs(startMs, endMs));
        if (OutputDurationMs < MinimumOutputMs)
        {
            _removed.RemoveAt(_removed.Count - 1);
            throw new ArgumentException("Removing this section would leave (almost) nothing of the video.", nameof(endMs));
        }
    }

    public void UndoLastRemoval()
    {
        if (_removed.Count > 0)
        {
            _removed.RemoveAt(_removed.Count - 1);
        }
    }

    public void Reset()
    {
        _removed.Clear();
        TrimStartMs = 0;
        TrimEndMs = DurationMs;
    }

    /// <summary>The ranges that make up the output, ordered and non-overlapping.</summary>
    public IReadOnlyList<TimeRangeMs> KeptRanges
    {
        get
        {
            var cuts = _removed.OrderBy(r => r.Start).ToList();
            var kept = new List<TimeRangeMs>();
            long cursor = TrimStartMs;
            foreach (TimeRangeMs cut in cuts)
            {
                if (cut.End <= cursor)
                {
                    continue;
                }

                if (cut.Start >= TrimEndMs)
                {
                    break;
                }

                if (cut.Start > cursor)
                {
                    kept.Add(new TimeRangeMs(cursor, Math.Min(cut.Start, TrimEndMs)));
                }

                cursor = Math.Max(cursor, cut.End);
            }

            if (cursor < TrimEndMs)
            {
                kept.Add(new TimeRangeMs(cursor, TrimEndMs));
            }

            return kept;
        }
    }

    public long OutputDurationMs => KeptRanges.Sum(r => r.Length);

    /// <summary>
    /// Output time for a source time, or null if that moment is cut. Times inside kept range k map to
    /// (sum of earlier kept lengths) + (t − range start), so sound and picture keep their relative timing.
    /// </summary>
    public long? MapToOutput(long sourceMs)
    {
        long offset = 0;
        foreach (TimeRangeMs r in KeptRanges)
        {
            if (r.Contains(sourceMs))
            {
                return offset + (sourceMs - r.Start);
            }

            offset += r.Length;
        }

        return null;
    }

    /// <summary>Same as <see cref="MapToOutput(long)"/> in 100-ns units (Media Foundation timestamps).</summary>
    public long? MapToOutput100ns(long source100ns)
    {
        long offset = 0;
        foreach (TimeRangeMs r in KeptRanges)
        {
            long s = r.Start * 10_000, e = r.End * 10_000;
            if (source100ns >= s && source100ns < e)
            {
                return offset + (source100ns - s);
            }

            offset += e - s;
        }

        return null;
    }

    /// <summary>
    /// For an audio block starting at <paramref name="start100ns"/> lasting <paramref name="length100ns"/>, the
    /// sub-ranges (offset within the block, length, output time) that are kept — used to cut PCM at sample precision.
    /// </summary>
    public IReadOnlyList<(long OffsetInBlock100ns, long Length100ns, long Output100ns)> KeptPartsOfBlock(long start100ns, long length100ns)
    {
        var parts = new List<(long, long, long)>();
        long blockEnd = start100ns + length100ns;
        long offset = 0;
        foreach (TimeRangeMs r in KeptRanges)
        {
            long s = r.Start * 10_000, e = r.End * 10_000;
            long a = Math.Max(s, start100ns), b = Math.Min(e, blockEnd);
            if (b > a)
            {
                parts.Add((a - start100ns, b - a, offset + (a - s)));
            }

            offset += e - s;
        }

        return parts;
    }
}
