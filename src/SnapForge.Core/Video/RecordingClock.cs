namespace SnapForge.Core.Video;

/// <summary>
/// Maps source timestamps (the shared QPC clock used by screen frames and audio packets, in 100-ns units) to media
/// time in the recording, removing paused intervals so a resumed recording continues without a gap. Video and audio
/// use the same clock instance, so pause/resume shifts both streams by exactly the same amount (VID-R12).
/// Not thread-safe by itself; the recorder calls it under its own lock.
/// </summary>
public sealed class RecordingClock
{
    private readonly long _pauseQuantum;
    private long _start = -1;
    private long _pausedAt = -1;
    private long _pausedTotal;
    private readonly List<(long Start, long End)> _pauses = [];

    /// <summary>
    /// Creates the clock. With a <paramref name="frameDuration"/> (100 ns), every pause is lengthened to a whole number
    /// of frames: a constant-frame-rate recording places video on a fixed frame grid while audio is sample-accurate,
    /// so a pause of an arbitrary length would leave every later video frame up to one frame behind the audio (CI run
    /// 113: +7.9 ms and +41 ms after a resume, 0.02 ms before the pause). The extra time (less than one frame) is
    /// dropped from both streams like the rest of the pause.
    /// </summary>
    public RecordingClock(long frameDuration = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameDuration);
        _pauseQuantum = frameDuration;
    }

    public bool IsStarted => _start >= 0;

    public bool IsPaused => _pausedAt >= 0;

    /// <summary>Total paused time so far, in 100-ns units (including a pause in progress up to its start).</summary>
    public long PausedTotal => _pausedTotal;

    /// <summary>Completed pause intervals in source time (for reports and tests).</summary>
    public IReadOnlyList<(long Start, long End)> Pauses => _pauses;

    public void Start(long sourceTime)
    {
        if (IsStarted)
        {
            throw new InvalidOperationException("The recording clock has already started.");
        }

        _start = sourceTime;
    }

    public void Pause(long sourceTime)
    {
        RequireStarted();
        if (IsPaused)
        {
            return;
        }

        _pausedAt = Math.Max(sourceTime, _start);
    }

    public void Resume(long sourceTime)
    {
        RequireStarted();
        if (!IsPaused)
        {
            return;
        }

        long end = Math.Max(sourceTime, _pausedAt);
        if (_pauseQuantum > 0)
        {
            long length = end - _pausedAt;
            end = _pausedAt + ((length + _pauseQuantum - 1) / _pauseQuantum * _pauseQuantum);
        }

        _pauses.Add((_pausedAt, end));
        _pausedTotal += end - _pausedAt;
        _pausedAt = -1;
    }

    /// <summary>
    /// Media time of a sample captured at <paramref name="sourceTime"/>. False if it was captured before the start or
    /// while paused (the sample must be dropped). Samples from before a pause that arrive late are still mapped.
    /// </summary>
    public bool TryMap(long sourceTime, out long mediaTime)
    {
        mediaTime = 0;
        if (!IsStarted || sourceTime < _start)
        {
            return false;
        }

        if (IsPaused && sourceTime >= _pausedAt)
        {
            return false;
        }

        // Subtract every pause that ended before this sample; a sample inside a completed pause is dropped.
        long shift = 0;
        foreach ((long s, long e) in _pauses)
        {
            if (sourceTime >= e)
            {
                shift += e - s;
            }
            else if (sourceTime >= s)
            {
                return false;
            }
        }

        mediaTime = sourceTime - _start - shift;
        return true;
    }

    /// <summary>Recorded (media) time at <paramref name="now"/>: what the toolbar's elapsed time shows.</summary>
    public long Elapsed(long now)
    {
        if (!IsStarted)
        {
            return 0;
        }

        long end = IsPaused ? _pausedAt : now;
        return Math.Max(0, end - _start - _pausedTotal);
    }

    private void RequireStarted()
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException("The recording clock has not started.");
        }
    }
}
