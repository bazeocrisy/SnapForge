namespace SnapForge.Core.Video;

/// <summary>Audio timing statistics of one source (system sound or microphone) for the recording report.</summary>
public sealed record AudioSourceStats(string Name, long FramesReceived, long FramesDropped, long SilenceInserted, int Reanchors, double MaxDriftMs, double LastDriftMs, bool Active);

/// <summary>
/// Mixes up to two audio sources (system sound + microphone) onto the recording timeline as 48 kHz stereo 16-bit PCM.
/// <list type="bullet">
/// <item>Each packet is placed at its own timestamp (media time from <see cref="RecordingClock"/>). Gaps — a device
/// that delivers nothing while silent, a disconnected or switched device — become silence, so audio never shifts
/// against video (VID-R01, VID-R07, VID-R08).</item>
/// <item>Drift between a source's sample count and its timestamps is measured; past <see cref="ReanchorThreshold"/>
/// the source is re-anchored (silence inserted or samples dropped) and the event is counted.</item>
/// <item>Memory is bounded: each source buffers at most <see cref="MaxBufferedFrames"/>; older audio is dropped and
/// counted rather than growing without limit (VID-R13).</item>
/// </list>
/// Not thread-safe; the recorder serialises calls.
/// </summary>
public sealed class AudioMixer
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    /// <summary>Drift (timestamp vs. sample position) that triggers a re-anchor: 40 ms.</summary>
    public const int ReanchorThreshold = SampleRate / 25;

    /// <summary>Per-source buffer cap: 4 s of audio.</summary>
    public const int MaxBufferedFrames = SampleRate * 4;

    private readonly Source[] _sources;
    private long _outputFrame;

    public AudioMixer(params string[] sourceNames)
    {
        ArgumentNullException.ThrowIfNull(sourceNames);
        if (sourceNames.Length is < 1 or > 4)
        {
            throw new ArgumentException("One to four sources.", nameof(sourceNames));
        }

        _sources = [.. sourceNames.Select(n => new Source(n))];
    }

    /// <summary>Frames already handed to the encoder (the output position on the timeline).</summary>
    public long OutputFrame => _outputFrame;

    /// <summary>Mutes or unmutes a source (its timing continues; muted audio is mixed as silence).</summary>
    public void SetMuted(int source, bool muted) => _sources[source].Muted = muted;

    public bool IsMuted(int source) => _sources[source].Muted;

    /// <summary>Peak level (0–1) of the last packet from a source, for the toolbar's level meters.</summary>
    public float Peak(int source) => _sources[source].Peak;

    public static long ToFrames(long time100ns) => time100ns * SampleRate / 10_000_000;

    public static long ToTime(long frames) => frames * 10_000_000 / SampleRate;

    /// <summary>
    /// Adds a packet of interleaved stereo float samples captured at media time <paramref name="mediaTime100ns"/>.
    /// <paramref name="silent"/> marks a packet the device flagged as silence (its samples are ignored).
    /// </summary>
    public void Push(int source, long mediaTime100ns, ReadOnlySpan<float> interleaved, bool silent = false)
    {
        Source s = _sources[source];
        int frames = interleaved.Length / Channels;
        if (frames == 0)
        {
            return;
        }

        s.Active = true;
        s.FramesReceived += frames;
        long expected = ToFrames(mediaTime100ns);
        if (s.WriteFrame < 0)
        {
            s.WriteFrame = Math.Max(expected, _outputFrame);
            s.BufferStart = s.WriteFrame;
        }

        long drift = expected - s.WriteFrame;
        s.LastDriftMs = drift * 1000.0 / SampleRate;
        s.MaxDriftMs = Math.Max(s.MaxDriftMs, Math.Abs(s.LastDriftMs));
        if (Math.Abs(drift) > ReanchorThreshold)
        {
            s.Reanchors++;
            if (drift > 0)
            {
                s.SilenceInserted += drift;
                s.AppendSilence((int)Math.Min(drift, MaxBufferedFrames));
                s.WriteFrame = expected;
            }
            else
            {
                // The packet overlaps audio already placed: drop the overlapping part.
                int skip = (int)Math.Min(-drift, frames);
                s.FramesDropped += skip;
                interleaved = interleaved[(skip * Channels)..];
                frames -= skip;
            }
        }

        // Audio for time that has already been sent to the encoder is too late to use.
        if (s.WriteFrame < _outputFrame)
        {
            int late = (int)Math.Min(_outputFrame - s.WriteFrame, frames);
            s.FramesDropped += late;
            interleaved = interleaved[(late * Channels)..];
            frames -= late;
            s.WriteFrame += late;
            s.BufferStart = Math.Max(s.BufferStart, _outputFrame);
        }

        float peak = 0;
        if (!silent)
        {
            foreach (float v in interleaved)
            {
                peak = Math.Max(peak, Math.Abs(v));
            }
        }

        s.Peak = Math.Min(1f, peak);
        if (frames > 0)
        {
            if (silent)
            {
                s.AppendSilence(frames);
            }
            else
            {
                s.Append(interleaved);
            }
        }

        s.WriteFrame = s.BufferStart + s.Buffered;
        s.EnforceCap();
    }

    /// <summary>Marks a source as gone (device removed) — it contributes silence until it pushes again.</summary>
    public void Deactivate(int source)
    {
        Source s = _sources[source];
        s.Active = false;
        s.Peak = 0;
        s.WriteFrame = -1;
        s.Clear();
    }

    /// <summary>
    /// Mixes the timeline from <see cref="OutputFrame"/> up to <paramref name="untilFrame"/> into 16-bit PCM.
    /// Sources lacking audio for part of that range contribute silence. Returns the number of frames written to
    /// <paramref name="destination"/> (which must hold at least that many frames × 2 channels).
    /// </summary>
    public int Mix(long untilFrame, Span<short> destination)
    {
        long count = Math.Min(untilFrame - _outputFrame, destination.Length / Channels);
        if (count <= 0)
        {
            return 0;
        }

        int n = (int)count;
        Span<float> mix = n * Channels <= 8192 ? stackalloc float[n * Channels] : new float[n * Channels];
        mix.Clear();
        foreach (Source s in _sources)
        {
            s.MixInto(_outputFrame, mix, n);
        }

        for (int i = 0; i < mix.Length; i++)
        {
            float v = Math.Clamp(mix[i], -1f, 1f);
            destination[i] = (short)MathF.Round(v * short.MaxValue);
        }

        _outputFrame += n;
        return n;
    }

    /// <summary>The furthest frame all active sources have audio for (mixing beyond it would cut sound short).</summary>
    public long ReadyFrame()
    {
        long ready = long.MaxValue;
        bool any = false;
        foreach (Source s in _sources)
        {
            if (s.Active && s.WriteFrame >= 0)
            {
                ready = Math.Min(ready, s.WriteFrame);
                any = true;
            }
        }

        return any ? ready : _outputFrame;
    }

    public IReadOnlyList<AudioSourceStats> Stats() =>
        [.. _sources.Select(s => new AudioSourceStats(s.Name, s.FramesReceived, s.FramesDropped, s.SilenceInserted, s.Reanchors, s.MaxDriftMs, s.LastDriftMs, s.Active))];

    private sealed class Source(string name)
    {
        private float[] _ring = new float[SampleRate * Channels / 2];
        private int _head; // index (in floats) of BufferStart
        private int _count; // floats buffered

        public string Name { get; } = name;

        public long WriteFrame { get; set; } = -1;

        public long BufferStart { get; set; }

        public int Buffered => _count / Channels;

        public bool Muted { get; set; }

        public bool Active { get; set; }

        public float Peak { get; set; }

        public long FramesReceived { get; set; }

        public long FramesDropped { get; set; }

        public long SilenceInserted { get; set; }

        public int Reanchors { get; set; }

        public double MaxDriftMs { get; set; }

        public double LastDriftMs { get; set; }

        public void Append(ReadOnlySpan<float> samples)
        {
            Ensure(_count + samples.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                _ring[(_head + _count + i) % _ring.Length] = samples[i];
            }

            _count += samples.Length;
        }

        public void AppendSilence(int frames)
        {
            int floats = frames * Channels;
            Ensure(_count + floats);
            for (int i = 0; i < floats; i++)
            {
                _ring[(_head + _count + i) % _ring.Length] = 0;
            }

            _count += floats;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public void EnforceCap()
        {
            int excess = Buffered - MaxBufferedFrames;
            if (excess > 0)
            {
                Consume(excess);
                FramesDropped += excess;
            }
        }

        /// <summary>Adds this source's audio for frames [from, from+n) to <paramref name="mix"/>, then discards it.</summary>
        public void MixInto(long from, Span<float> mix, int n)
        {
            if (WriteFrame < 0 || _count == 0)
            {
                return;
            }

            // Drop anything before 'from' (already output).
            if (BufferStart < from)
            {
                Consume((int)Math.Min(from - BufferStart, Buffered));
            }

            long offset = BufferStart - from; // frames of silence before this source's audio starts
            if (offset >= n)
            {
                return;
            }

            int start = (int)Math.Max(0, offset);
            int available = Math.Min(Buffered, n - start);
            if (!Muted)
            {
                for (int i = 0; i < available * Channels; i++)
                {
                    mix[(start * Channels) + i] += _ring[(_head + i) % _ring.Length];
                }
            }

            Consume(available);
        }

        private void Consume(int frames)
        {
            int floats = frames * Channels;
            _head = (_head + floats) % _ring.Length;
            _count -= floats;
            BufferStart += frames;
            if (_count == 0)
            {
                _head = 0;
            }
        }

        private void Ensure(int floats)
        {
            if (floats <= _ring.Length)
            {
                return;
            }

            int size = _ring.Length;
            while (size < floats)
            {
                size *= 2;
            }

            var next = new float[size];
            for (int i = 0; i < _count; i++)
            {
                next[i] = _ring[(_head + i) % _ring.Length];
            }

            _ring = next;
            _head = 0;
        }
    }
}
