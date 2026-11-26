using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Video;

internal enum RecorderState
{
    Ready,
    Recording,
    Paused,
    Stopping,
    Finished,
    Failed,
    Cancelled,
}

/// <summary>Options for one recording.</summary>
internal sealed record RecorderOptions(int FramesPerSecond, VideoQuality Quality, bool RecordSystemAudio, bool RecordMicrophone, string? MicrophoneDeviceId, long MinimumFreeBytes, int LowSpaceWarningMinutes)
{
    /// <summary>True if the file gets an audio track (system sound or microphone requested at the start).</summary>
    public bool AudioTrack => RecordSystemAudio || RecordMicrophone;
}

/// <summary>What a recording produced, with the measurements the benchmark report needs.</summary>
internal sealed record RecordingResult(
    string Path,
    int Width,
    int Height,
    int FramesPerSecond,
    long DurationMs,
    long FramesWritten,
    long FramesRepeated,
    long FramesDropped,
    long FramesCaptured,
    bool HasAudio,
    string Encoder,
    bool HardwareEncoder,
    bool SoftwareFallback,
    string? EndReason,
    IReadOnlyList<string> Notices,
    IReadOnlyList<AudioSourceStats> Audio,
    double CpuPercent,
    long PeakWorkingSetMb,
    long PeakPrivateMb,
    double MeanEncodeLatencyMs,
    double MaxEncodeLatencyMs,
    long Bytes,
    bool Finalized,
    PixelBuffer? FirstFrame);

/// <summary>
/// One screen recording (Milestone V1). Threads:
/// <list type="bullet">
/// <item><b>capture</b> — polls the frame source on a fixed frame-rate schedule; a new screen frame is placed in the
/// nearest frame slot, and when the screen did not change the previous frame is repeated, so the file has a constant
/// frame rate. Also watches free disk space.</item>
/// <item><b>audio</b> — reads the audio sources every 10 ms, maps their timestamps through the shared
/// <see cref="RecordingClock"/> and mixes them (<see cref="AudioMixer"/>).</item>
/// <item><b>encoder</b> — the only thread that touches the MP4 writer: writes queued frames and mixed audio.</item>
/// </list>
/// Frames wait in a bounded queue sized from a memory budget; when the encoder falls behind, new frames are dropped
/// and counted instead of using more memory (VID-R13). Only one recording can exist at a time (VID-R14).
/// </summary>
internal sealed unsafe class ScreenRecorder : IDisposable
{
    private const long MemoryBudgetBytes = 256L * 1024 * 1024;
    private const long AudioLatency100ns = 2_000_000; // audio later than 200 ms behind real time is dropped

    private static ScreenRecorder? _active;
    private static readonly Lock ActiveGate = new();

    private readonly IFrameSource _video;
    private readonly List<IAudioSource> _audio;
    private readonly RecorderOptions _options;
    private readonly string _path;
    private readonly RecordingClock _clock = new();
    private readonly AudioMixer _mixer;
    private readonly Lock _gate = new();
    private readonly BlockingCollection<FrameItem> _frames;
    private readonly ConcurrentQueue<(long Time, short[] Pcm, int Frames)> _pcm = new();
    private readonly ConcurrentBag<FrameBuffer> _freeBuffers = [];
    private readonly List<string> _notices = [];
    private readonly int _frameBytes;
    private Mp4Writer? _writer;
    private Thread? _captureThread, _audioThread, _encoderThread;
    private volatile bool _stopCapture;
    private volatile bool _cancel;
    private long _framesWritten, _framesRepeated, _framesDropped, _framesCaptured;
    private long _lastVideoEnd;
    private bool _softwareFallback;
    private string? _endReason;
    private Exception? _failure;
    private PixelBuffer? _firstFrame;
    private double _latencySum;
    private long _latencySamples;
    private double _latencyMax;
    private long _peakWorkingSet, _peakPrivate;
    private TimeSpan _cpuStart;
    private long _wallStart;
    private bool _lowSpaceWarned;

    private ScreenRecorder(IFrameSource video, IEnumerable<IAudioSource> audio, RecorderOptions options, string path)
    {
        _video = video;
        _audio = [.. audio];
        _options = options;
        _path = path;
        _mixer = new AudioMixer(_audio.Count == 0 ? ["silence"] : [.. _audio.Select(a => a.Name)]);
        _frameBytes = video.Width * video.Height * 4;
        int capacity = (int)Math.Clamp(MemoryBudgetBytes / _frameBytes, 3, 60);
        QueueCapacity = capacity;
        _frames = new BlockingCollection<FrameItem>(capacity);
        for (int i = 0; i < capacity + 2; i++)
        {
            _freeBuffers.Add(new FrameBuffer(this, _frameBytes));
        }
    }

    /// <summary>Raised (on a recorder thread) with a message for the user: device changes, low disk space, end of source.</summary>
    public event EventHandler<string>? Notice;

    /// <summary>Raised (on a recorder thread) when the recording stopped by itself (source ended, disk full, failure).</summary>
    public event EventHandler? StoppedBySelf;

    public static bool IsRecording
    {
        get
        {
            lock (ActiveGate)
            {
                return _active is not null;
            }
        }
    }

    public RecorderState State { get; private set; } = RecorderState.Ready;

    public int Width => _video.Width;

    public int Height => _video.Height;

    public int FramesPerSecond => _options.FramesPerSecond;

    public int QueueCapacity { get; }

    public string OutputPath => _path;

    /// <summary>Recorded time so far (excludes pauses).</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromTicks(_clock.Elapsed(Qpc.Now100ns()));
            }
        }
    }

    /// <summary>Peak level 0–1 of an audio source (for the toolbar meters).</summary>
    public float Level(int source)
    {
        lock (_gate)
        {
            return source < _audio.Count ? _mixer.Peak(source) : 0;
        }
    }

    public IReadOnlyList<IAudioSource> AudioSources => _audio;

    public string? EncoderName => _writer?.Encoder?.Name;

    /// <summary>QPC time (100 ns) at which the recording clock started (diagnostics).</summary>
    public long ClockStart => _wallStart;

    /// <summary>Diagnostics: called on the capture thread for each new frame with (source time, media time it was placed at).</summary>
    internal Action<long, long>? FrameScheduled { get; set; }

    /// <summary>
    /// Creates the recorder (refusing a second simultaneous recording) and opens the MP4 writer, falling back to the
    /// software encoder if the hardware path cannot start (VID-R02, VID-R03).
    /// </summary>
    public static ScreenRecorder Create(IFrameSource video, IEnumerable<IAudioSource> audio, RecorderOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(options);
        lock (ActiveGate)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException("A recording is already in progress. Stop it before starting another.");
            }

            var recorder = new ScreenRecorder(video, audio ?? [], options, path);
            recorder.OpenWriter();
            _active = recorder;
            return recorder;
        }
    }

    private void OpenWriter()
    {
        var format = new VideoStreamFormat(_video.Width, _video.Height, _options.FramesPerSecond, VideoSettings.BitRate(_video.Width, _video.Height, _options.FramesPerSecond, _options.Quality));
        try
        {
            _writer = new Mp4Writer(_path, format, _options.AudioTrack, fragmented: true, EncoderInput.SystemMemory, 0, allowHardware: true);
        }
        catch (NativeCallException ex)
        {
            AddNotice($"The hardware video encoder could not start ({ex.Message}); using the Windows software encoder.");
            TryDelete(_path);
            _softwareFallback = true;
            _writer = new Mp4Writer(_path, format, _options.AudioTrack, fragmented: true, EncoderInput.SystemMemory, 0, allowHardware: false);
        }
    }

    /// <summary>Starts capturing (call after the countdown).</summary>
    public void Start()
    {
        if (State != RecorderState.Ready)
        {
            throw new InvalidOperationException("The recording has already started.");
        }

        foreach (IAudioSource a in _audio)
        {
            a.Enabled = a.Name == AudioNames.System ? _options.RecordSystemAudio : _options.RecordMicrophone;
        }

        _video.Start();
        using (var p = Process.GetCurrentProcess())
        {
            _cpuStart = p.TotalProcessorTime;
        }

        _wallStart = Qpc.Now100ns();
        lock (_gate)
        {
            _clock.Start(_wallStart);
        }

        State = RecorderState.Recording;
        _encoderThread = StartThread(EncoderLoop, "SnapForge encoder", ThreadPriority.AboveNormal);
        _captureThread = StartThread(CaptureLoop, "SnapForge capture", ThreadPriority.Highest);
        if (_options.AudioTrack)
        {
            _audioThread = StartThread(AudioLoop, "SnapForge audio", ThreadPriority.Highest);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (State == RecorderState.Recording)
            {
                _clock.Pause(Qpc.Now100ns());
                State = RecorderState.Paused;
            }
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (State == RecorderState.Paused)
            {
                _clock.Resume(Qpc.Now100ns());
                State = RecorderState.Recording;
            }
        }
    }

    /// <summary>Turns a source on or off during the recording (toolbar toggles). Off = silence, device released.</summary>
    public void SetSourceEnabled(int source, bool enabled)
    {
        if (source < _audio.Count)
        {
            lock (_gate)
            {
                _audio[source].Enabled = enabled;
                if (!enabled)
                {
                    _mixer.Deactivate(source);
                }
            }
        }
    }

    /// <summary>Stops, writes what is queued and completes the MP4. Blocks until the file is finished.</summary>
    public RecordingResult Stop(string? reason = null)
    {
        lock (_gate)
        {
            if (State is RecorderState.Recording or RecorderState.Paused)
            {
                State = RecorderState.Stopping;
            }

            _endReason ??= reason;
        }

        _stopCapture = true;
        _captureThread?.Join();
        _audioThread?.Join();
        _frames.CompleteAdding();
        _encoderThread?.Join();
        bool finalized = false;
        if (_failure is null && _writer is not null)
        {
            try
            {
                _writer.Complete();
                finalized = true;
            }
            catch (NativeCallException ex)
            {
                _failure = ex;
                AddNotice($"The video could not be completed normally ({ex.Message}). It will be recovered from its last complete part.");
            }
        }

        EncoderNameAtEnd = _writer?.Encoder?.Name;
        HardwareAtEnd = _writer?.Encoder?.Hardware ?? false;
        _writer?.Dispose();
        _writer = null;
        State = _failure is null ? RecorderState.Finished : RecorderState.Failed;
        Release();
        return BuildResult(finalized);
    }

    /// <summary>Cancels and discards: nothing is kept.</summary>
    public void Cancel()
    {
        _cancel = true;
        _stopCapture = true;
        _captureThread?.Join();
        _audioThread?.Join();
        _frames.CompleteAdding();
        _encoderThread?.Join();
        _writer?.Dispose();
        _writer = null;
        TryDelete(_path);
        State = RecorderState.Cancelled;
        Release();
    }

    public void Dispose()
    {
        if (State is RecorderState.Recording or RecorderState.Paused or RecorderState.Stopping)
        {
            Cancel();
        }

        _frames.Dispose();
        _video.Dispose();
        foreach (IAudioSource a in _audio)
        {
            a.Dispose();
        }

        Release();
    }

    private void Release()
    {
        lock (ActiveGate)
        {
            if (ReferenceEquals(_active, this))
            {
                _active = null;
            }
        }
    }

    // ---------------------------------------------------------------- capture

    private void CaptureLoop()
    {
        long period = 10_000_000L / _options.FramesPerSecond;
        FrameBuffer? last = null;
        long lastSlot = -1;
        long nextDiskCheck = 0;
        try
        {
            while (!_stopCapture)
            {
                // Wait for the next frame slot on the shared clock.
                long now = Qpc.Now100ns();
                long mediaNow;
                bool paused;
                lock (_gate)
                {
                    paused = _clock.IsPaused;
                    mediaNow = _clock.Elapsed(now);
                }

                long slotNow = mediaNow / period;
                FrameBuffer? buffer = TakeBuffer();
                FramePoll poll = FramePoll.NoChange;
                long sourceTime = 0;
                if (buffer is null && !paused)
                {
                    Interlocked.Increment(ref _framesDropped); // every buffer is waiting for the encoder
                }
                else if (buffer is not null)
                {
                    fixed (byte* p = buffer.Data)
                    {
                        poll = _video.Poll(p, _video.Width * 4, out sourceTime);
                    }
                }

                if (poll == FramePoll.Ended)
                {
                    ReturnBuffer(buffer);
                    StopFromInside(_video.EndReason ?? "The recorded source ended.");
                    break;
                }

                if (poll == FramePoll.NewFrame)
                {
                    Interlocked.Increment(ref _framesCaptured);
                    _firstFrame ??= new PixelBuffer(_video.Width, _video.Height, (byte[])buffer!.Data.Clone());
                }

                if (!paused && poll == FramePoll.NewFrame)
                {
                    long media;
                    bool mapped;
                    lock (_gate)
                    {
                        mapped = _clock.TryMap(sourceTime, out media);
                    }

                    long slot = mapped ? (media + (period / 2)) / period : slotNow;
                    slot = Math.Max(slot, lastSlot + 1);
                    if (slot > slotNow + 1)
                    {
                        slot = slotNow + 1;
                    }

                    if (slot > lastSlot)
                    {
                        FrameScheduled?.Invoke(sourceTime, slot * period);
                        Release(ref last);
                        last = buffer;
                        last!.AddRef();
                        Enqueue(new FrameItem(buffer!, slot * period, period, Repeat: false));
                        lastSlot = slot;
                    }
                    else
                    {
                        ReturnBuffer(buffer);
                    }
                }
                else
                {
                    if (poll == FramePoll.NewFrame && paused)
                    {
                        // Keep the newest picture so recording resumes with what is on screen now.
                        Release(ref last);
                        last = buffer;
                    }
                    else
                    {
                        ReturnBuffer(buffer);
                    }

                    // Screen unchanged: repeat the previous frame in each elapsed slot (constant frame rate).
                    if (!paused && last is not null)
                    {
                        while (lastSlot < slotNow && !_stopCapture)
                        {
                            lastSlot++;
                            last.AddRef();
                            Interlocked.Increment(ref _framesRepeated);
                            Enqueue(new FrameItem(last, lastSlot * period, period, Repeat: true));
                        }
                    }
                }

                if (Environment.TickCount64 >= nextDiskCheck)
                {
                    nextDiskCheck = Environment.TickCount64 + 2000;
                    CheckDiskSpace();
                    SampleProcess();
                }

                // Sleep until the next slot (short sleeps keep slot timing within ~1 ms).
                long wait;
                lock (_gate)
                {
                    wait = ((slotNow + 1) * period) - _clock.Elapsed(Qpc.Now100ns());
                }

                Thread.Sleep(TimeSpan.FromTicks(Math.Clamp(wait, 10_000, period)));
            }
        }
#pragma warning disable CA1031 // any failure ends the recording and is reported
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _failure ??= ex;
            StopFromInside("Screen capture failed: " + ex.Message);
        }
        finally
        {
            Release(ref last);
        }
    }

    private void Enqueue(FrameItem item)
    {
        if (!_frames.TryAdd(item))
        {
            // Encoder is behind and the bounded queue is full: drop rather than grow (VID-R13).
            Interlocked.Increment(ref _framesDropped);
            item.Buffer.Release();
        }
    }

    // ---------------------------------------------------------------- audio

    private void AudioLoop()
    {
        var pcm = new short[4800 * 2];
        try
        {
            while (!_stopCapture)
            {
                for (int i = 0; i < _audio.Count; i++)
                {
                    IAudioSource source = _audio[i];
                    int index = i;
                    string? message = source.Read((qpc, samples, silent) =>
                    {
                        lock (_gate)
                        {
                            if (_clock.TryMap(qpc, out long media))
                            {
                                _mixer.Push(index, media, samples, silent);
                            }
                        }
                    });
                    if (message is not null)
                    {
                        lock (_gate)
                        {
                            if (source.DeviceName is null)
                            {
                                _mixer.Deactivate(index);
                            }
                        }

                        AddNotice(message);
                    }
                }

                MixAvailable(pcm, final: false);
                Thread.Sleep(10);
            }

            MixAvailable(pcm, final: true);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AddNotice("Audio capture stopped: " + ex.Message + " The video continues without further sound.");
        }
    }

    /// <summary>Mixes the timeline up to what every source has delivered, or up to 200 ms behind now.</summary>
    private void MixAvailable(short[] pcm, bool final)
    {
        while (true)
        {
            long until;
            long start;
            int n;
            lock (_gate)
            {
                long elapsed = AudioMixer.ToFrames(_clock.Elapsed(Qpc.Now100ns()));
                long late = AudioMixer.ToFrames(Math.Max(0, _clock.Elapsed(Qpc.Now100ns()) - AudioLatency100ns));
                until = final ? elapsed : Math.Min(elapsed, Math.Max(_mixer.ReadyFrame(), late));
                start = _mixer.OutputFrame;
                n = _mixer.Mix(until, pcm);
            }

            if (n <= 0)
            {
                return;
            }

            var block = new short[n * 2];
            Array.Copy(pcm, block, block.Length);
            _pcm.Enqueue((AudioMixer.ToTime(start), block, n));
        }
    }

    // ---------------------------------------------------------------- encoder

    private void EncoderLoop()
    {
        try
        {
            foreach (FrameItem item in _frames.GetConsumingEnumerable())
            {
                try
                {
                    if (_cancel || _failure is not null)
                    {
                        continue;
                    }

                    WriteAudioUpTo(item.Time + item.Duration);
                    fixed (byte* p = item.Buffer.Data)
                    {
                        WriteFrameWithFallback(p, item);
                    }

                    _lastVideoEnd = item.Time + item.Duration;
                    Interlocked.Increment(ref _framesWritten);
                    if ((_framesWritten & 15) == 0 && _writer is not null)
                    {
                        (long _, long latency) = _writer.Backlog();
                        _latencySum += latency;
                        _latencySamples++;
                        _latencyMax = Math.Max(_latencyMax, latency);
                    }
                }
                finally
                {
                    item.Buffer.Release();
                }
            }

            if (!_cancel && _failure is null)
            {
                WriteAudioUpTo(long.MaxValue);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _failure ??= ex;
            string message = ex is IOException || ex.HResult == unchecked((int)0x80070070) /* disk full */
                ? "The disk is full or could not be written. The recording was stopped; what was recorded is kept."
                : "Video encoding failed: " + ex.Message;
            StopFromInside(message);
            foreach (FrameItem rest in _frames.GetConsumingEnumerable())
            {
                rest.Buffer.Release();
            }
        }
    }

    private void WriteFrameWithFallback(byte* p, FrameItem item)
    {
        try
        {
            _writer!.WriteFrame(p, _video.Width * 4, item.Time, item.Duration);
        }
        catch (NativeCallException ex) when (!_softwareFallback && _framesWritten < _options.FramesPerSecond)
        {
            // A hardware encoder that fails on its first frames: restart the file on the software encoder.
            AddNotice($"The hardware video encoder failed ({ex.Message}); switched to the Windows software encoder.");
            _writer!.Dispose();
            TryDelete(_path);
            _softwareFallback = true;
            var format = new VideoStreamFormat(_video.Width, _video.Height, _options.FramesPerSecond, VideoSettings.BitRate(_video.Width, _video.Height, _options.FramesPerSecond, _options.Quality));
            _writer = new Mp4Writer(_path, format, _options.AudioTrack, fragmented: true, EncoderInput.SystemMemory, 0, allowHardware: false);
            _writer.WriteFrame(p, _video.Width * 4, item.Time, item.Duration);
        }
    }

    private void WriteAudioUpTo(long time)
    {
        if (_writer is null || !_writer.HasAudio)
        {
            return;
        }

        while (_pcm.TryPeek(out var block) && block.Time <= time)
        {
            _pcm.TryDequeue(out _);
            _writer.WriteAudio(block.Pcm.AsSpan(0, block.Frames * 2), block.Time);
        }
    }

    // ---------------------------------------------------------------- monitoring

    private void CheckDiskSpace()
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(_path));
        if (root is null)
        {
            return;
        }

        long free;
        try
        {
            free = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (IOException)
        {
            return;
        }
        catch (ArgumentException)
        {
            return; // network paths
        }

        double bytesPerSecond = VideoSettings.BytesPerSecond(VideoSettings.BitRate(_video.Width, _video.Height, _options.FramesPerSecond, _options.Quality), _options.AudioTrack);
        double minutesLeft = (free - _options.MinimumFreeBytes) / bytesPerSecond / 60;
        if (free < _options.MinimumFreeBytes)
        {
            StopFromInside($"Recording stopped: less than {_options.MinimumFreeBytes / (1024 * 1024)} MB of disk space is left. The recording was saved.");
        }
        else if (minutesLeft < _options.LowSpaceWarningMinutes && !_lowSpaceWarned)
        {
            _lowSpaceWarned = true;
            AddNotice($"Low disk space: about {Math.Max(0, (int)minutesLeft)} minutes of recording left. The recording stops and is saved automatically before the disk is full.");
        }
    }

    private void SampleProcess()
    {
        using var p = Process.GetCurrentProcess();
        _peakWorkingSet = Math.Max(_peakWorkingSet, p.WorkingSet64);
        _peakPrivate = Math.Max(_peakPrivate, p.PrivateMemorySize64);
    }

    private void StopFromInside(string reason)
    {
        lock (_gate)
        {
            if (_endReason is not null || State is not (RecorderState.Recording or RecorderState.Paused))
            {
                return;
            }

            _endReason = reason;
        }

        AddNotice(reason);
        _stopCapture = true;
        StoppedBySelf?.Invoke(this, EventArgs.Empty);
    }

    private void AddNotice(string message)
    {
        lock (_notices)
        {
            _notices.Add(message);
        }

        Notice?.Invoke(this, message);
    }

    private RecordingResult BuildResult(bool finalized)
    {
        SampleProcess();
        double cpu;
        using (var p = Process.GetCurrentProcess())
        {
            double wall = (Qpc.Now100ns() - _wallStart) / 10_000_000.0;
            cpu = wall <= 0 ? 0 : (p.TotalProcessorTime - _cpuStart).TotalSeconds / wall / Environment.ProcessorCount * 100;
        }

        IReadOnlyList<AudioSourceStats> audio;
        lock (_gate)
        {
            audio = _mixer.Stats();
        }

        string[] notices;
        lock (_notices)
        {
            notices = [.. _notices];
        }

        (string Name, bool Hardware) encoder = (EncoderNameAtEnd ?? "unknown", HardwareAtEnd);
        return new RecordingResult(
            _path,
            _video.Width,
            _video.Height,
            _options.FramesPerSecond,
            _lastVideoEnd / 10_000,
            _framesWritten,
            _framesRepeated,
            _framesDropped,
            _framesCaptured,
            _options.AudioTrack,
            encoder.Name,
            encoder.Hardware,
            _softwareFallback,
            _endReason,
            notices,
            audio,
            Math.Round(cpu, 1),
            _peakWorkingSet / (1024 * 1024),
            _peakPrivate / (1024 * 1024),
            _latencySamples == 0 ? 0 : Math.Round(_latencySum / _latencySamples, 1),
            _latencyMax,
            File.Exists(_path) ? new FileInfo(_path).Length : 0,
            finalized,
            _firstFrame);
    }

    private string? EncoderNameAtEnd { get; set; }

    private bool HardwareAtEnd { get; set; }

    private static Thread StartThread(Action body, string name, ThreadPriority priority)
    {
        var t = new Thread(() =>
        {
            _ = Com.CoInitializeEx(0, Com.COINIT_MULTITHREADED);
            body();
        })
        { IsBackground = true, Name = name, Priority = priority };
        t.Start();
        return t;
    }

    private FrameBuffer? TakeBuffer()
    {
        if (_freeBuffers.TryTake(out FrameBuffer? b))
        {
            b.Reset();
            return b;
        }

        return null; // all buffers are queued: this slot is skipped (counted as a drop if it was a new frame)
    }

    private static void ReturnBuffer(FrameBuffer? buffer) => buffer?.Release();

    private static void Release(ref FrameBuffer? buffer)
    {
        if (buffer is not null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    internal void Recycle(FrameBuffer buffer) => _freeBuffers.Add(buffer);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A pooled frame; reference counted because a repeated frame is queued several times.</summary>
    internal sealed class FrameBuffer(ScreenRecorder owner, int bytes)
    {
        private int _refs = 1;

        public byte[] Data { get; } = new byte[bytes];

        public void Reset() => _refs = 1;

        public void AddRef() => Interlocked.Increment(ref _refs);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refs) == 0)
            {
                owner.Recycle(this);
            }
        }
    }

    private readonly record struct FrameItem(FrameBuffer Buffer, long Time, long Duration, bool Repeat);

    /// <summary>Names used for the audio sources (also the toolbar labels).</summary>
    internal static class AudioNames
    {
        public const string System = "System audio";
        public const string Microphone = "Microphone";
    }
}
