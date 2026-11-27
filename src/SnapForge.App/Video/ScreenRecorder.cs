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

    /// <summary>Capture and audio have ended; the encoder writes what is queued and completes the file.</summary>
    Finalizing,
    Finished,

    /// <summary>Faulted: encoding failed, the disk was full, or a recorder thread stalled (<see cref="RecordingResult.Stalled"/>).</summary>
    Failed,
    Cancelled,
}

/// <summary>
/// Fault-injection seams (tests and the self-test only; the app never sets them). Each runs on the named recorder
/// thread and may block (a stalled thread) or throw (e.g. an <see cref="IOException"/> = disk full).
/// </summary>
internal sealed class RecorderHooks
{
    /// <summary>Encoder thread, before each video frame is written; the argument is the number of frames written so far.</summary>
    public Action<long>? BeforeVideoWrite { get; init; }

    /// <summary>Capture thread, once per frame slot.</summary>
    public Action? CaptureIteration { get; init; }

    /// <summary>Encoder thread, just before the file is completed (Sink Writer Finalize).</summary>
    public Action? BeforeFinalize { get; init; }

    /// <summary>How long Stop waits for each recorder thread before reporting it as stalled.</summary>
    public TimeSpan JoinTimeout { get; init; } = ScreenRecorder.DefaultJoinTimeout;
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
    PixelBuffer? FirstFrame,
    bool Cancelled = false,
    bool Stalled = false);

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
    /// <summary>Default bound on waiting for each recorder thread when stopping (then it is reported as stalled).</summary>
    public static readonly TimeSpan DefaultJoinTimeout = TimeSpan.FromSeconds(10);

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
    private readonly RecorderHooks? _hooks;
    private readonly Lock _lifecycle = new();
    private readonly TaskCompletionSource _threadsExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<RecordingResult>? _ending;
    private int _liveThreads;
    private volatile bool _finalized;
    private bool _disposed;
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

    private ScreenRecorder(IFrameSource video, IEnumerable<IAudioSource> audio, RecorderOptions options, string path, RecorderHooks? hooks)
    {
        _hooks = hooks;
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

    /// <summary>The frame source (screen, screen + camera, or camera).</summary>
    public IFrameSource Source => _video;

    /// <summary>Completes when every recorder thread has exited (at the end of Stop/Cancel if none was started).</summary>
    public Task ThreadsExited => _threadsExited.Task;

    private TimeSpan JoinTimeout => _hooks?.JoinTimeout ?? DefaultJoinTimeout;

    /// <summary>Media time (100 ns) of a source timestamp on this recording's clock, or null if it was paused / before start.</summary>
    internal long? MapSourceTime(long source)
    {
        lock (_gate)
        {
            return _clock.TryMap(source, out long media) ? media : null;
        }
    }

    /// <summary>QPC time (100 ns) at which the recording clock started (diagnostics).</summary>
    public long ClockStart => _wallStart;

    /// <summary>Diagnostics: called on the capture thread for each new frame with (source time, media time it was placed at).</summary>
    internal Action<long, long>? FrameScheduled { get; set; }

    /// <summary>
    /// Creates the recorder (refusing a second simultaneous recording) and opens the MP4 writer, falling back to the
    /// software encoder if the hardware path cannot start (VID-R02, VID-R03).
    /// </summary>
    public static ScreenRecorder Create(IFrameSource video, IEnumerable<IAudioSource> audio, RecorderOptions options, string path, RecorderHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(options);
        lock (ActiveGate)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException("A recording is already in progress. Stop it before starting another.");
            }

            var recorder = new ScreenRecorder(video, audio ?? [], options, path, hooks);
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
        lock (_lifecycle)
        {
            if (_ending is not null)
            {
                throw new InvalidOperationException("The recording was already stopped.");
            }

            if (State != RecorderState.Ready)
            {
                throw new InvalidOperationException("The recording has already started.");
            }

            StartLocked();
        }
    }

    private void StartLocked()
    {
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
                _mixer.ReanchorAll();
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

    /// <summary>
    /// Stops, writes what is queued and completes the MP4. Idempotent: the first Stop or Cancel does the work; later
    /// and concurrent calls wait for it and get the same result. Every wait is bounded (<see cref="RecorderHooks.JoinTimeout"/>
    /// per thread): a thread that does not finish is reported as stalled (<see cref="RecordingResult.Stalled"/>), the
    /// partial fragmented file is left for recovery, and native objects that thread still uses are not released here.
    /// </summary>
    public RecordingResult Stop(string? reason = null) => End(cancel: false, reason);

    /// <summary>Cancels and discards: nothing is kept. Returns false if a Stop got there first (then the recording is saved).</summary>
    public bool Cancel() => End(cancel: true, null).Cancelled;

    private RecordingResult End(bool cancel, string? reason)
    {
        TaskCompletionSource<RecordingResult> ending;
        bool owner = false;
        lock (_lifecycle)
        {
            if (_ending is null)
            {
                _ending = new TaskCompletionSource<RecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                owner = true;
                _cancel = cancel;
            }

            ending = _ending;
        }

        if (!owner)
        {
            // Already ending: the owner's work is bounded (three bounded joins), so this wait is too.
            TimeSpan budget = (JoinTimeout * 3) + TimeSpan.FromSeconds(10);
            if (ending.Task.Wait(budget))
            {
                return ending.Task.GetAwaiter().GetResult();
            }

            return BuildResult(finalized: false) with { Stalled = true };
        }

        RecordingResult result;
        try
        {
            result = EndAsOwner(cancel, reason);
        }
#pragma warning disable CA1031 // the result must always be published, or concurrent callers would wait in vain
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RecordingLog.Write("stopping the recording failed: " + ex);
            _failure ??= ex;
            State = RecorderState.Failed;
            Release();
            result = BuildResult(finalized: false);
        }

        ending.TrySetResult(result);
        return result;
    }

    private RecordingResult EndAsOwner(bool cancel, string? reason)
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
        if (_encoderThread is null)
        {
            // Never started (stopped during the countdown): nothing was recorded.
            _frames.CompleteAdding();
            _writer?.Dispose();
            _writer = null;
            TryDelete(_path);
            _threadsExited.TrySetResult();
            State = RecorderState.Cancelled;
            Release();
            return BuildResult(finalized: false) with { Cancelled = true };
        }

        var stalled = new List<string>();
        if (!Join(_captureThread))
        {
            stalled.Add("screen capture");
        }

        if (!Join(_audioThread))
        {
            stalled.Add("audio capture");
        }

        _frames.CompleteAdding();
        if (!cancel)
        {
            lock (_gate)
            {
                State = RecorderState.Finalizing;
            }
        }

        if (!Join(_encoderThread))
        {
            stalled.Add("video encoder");
        }

        if (_liveThreads == 0)
        {
            _threadsExited.TrySetResult();
        }

        bool isStalled = stalled.Count > 0;
        if (isStalled)
        {
            string which = string.Join(" and ", stalled);
            RecordingLog.Write($"{which} did not finish within {JoinTimeout.TotalSeconds:0} s while stopping {_path}; the native objects it uses stay with that thread (released when it exits); the partial file is left for recovery.");
            AddNotice(cancel
                ? $"The {which} stopped responding. The recording is discarded as soon as it lets go of the file."
                : $"The {which} stopped responding, so the recording could not be finished normally. What was recorded is kept and recovered from its last complete part.");
        }
        else if (cancel)
        {
            TryDelete(_path);
        }

        bool finalized = !cancel && !isStalled && _failure is null && _finalized;
        State = cancel ? RecorderState.Cancelled : finalized ? RecorderState.Finished : RecorderState.Failed;
        Release(); // a new recording may start; a stalled thread keeps only its own objects
        return BuildResult(finalized) with { Cancelled = cancel, Stalled = isStalled };
    }

    private bool Join(Thread? thread) => thread is null || thread.Join(JoinTimeout);

    /// <summary>
    /// Releases the sources. A recording still running is cancelled first. Native objects a stalled thread still uses
    /// are released only once that thread exits (deliberately leaked until then, with a log entry).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        bool ending;
        lock (_lifecycle)
        {
            ending = _ending is not null;
        }

        if (!ending)
        {
            End(cancel: true, null);
        }

        if (ThreadsExited.IsCompleted)
        {
            DisposeSources();
        }
        else
        {
            RecordingLog.Write($"recorder threads for {_path} are still running; the capture sources are released when they exit.");
            _ = ThreadsExited.ContinueWith(_ => DisposeSources(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        Release();
    }

    private void DisposeSources()
    {
        _frames.Dispose();
        _video.Dispose();
        foreach (IAudioSource a in _audio)
        {
            a.Dispose();
        }
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
                _hooks?.CaptureIteration?.Invoke();

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
                        // Slots skipped since the previous frame (the screen changed only between polls) keep the
                        // previous picture, so the file stays at a constant frame rate. Benchmarks on CI showed
                        // ~7–17 % of slots missing on real screens before this.
                        while (last is not null && lastSlot >= 0 && lastSlot < slot - 1)
                        {
                            lastSlot++;
                            last.AddRef();
                            Interlocked.Increment(ref _framesRepeated);
                            Enqueue(new FrameItem(last, lastSlot * period, period, Repeat: true));
                        }

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

                    // Screen unchanged: repeat the previous frame in each slot that has already ended (constant
                    // frame rate). The slot in progress is left open: a new frame may still arrive for it, and
                    // filling it early would push that frame one slot late (measured 35 ms after resume on CI).
                    if (!paused && last is not null)
                    {
                        while (lastSlot < slotNow - 1 && !_stopCapture)
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
        bool added;
        try
        {
            added = _frames.TryAdd(item);
        }
        catch (InvalidOperationException)
        {
            added = false; // Stop gave up waiting for this (stalled) thread and closed the queue
        }

        if (!added)
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
                    _hooks?.BeforeVideoWrite?.Invoke(_framesWritten);
                    fixed (byte* p = item.Buffer.Data)
                    {
                        // Constant frame rate from t = 0: if the first frame arrives after the first slot (slow first
                        // poll, a camera's first frame, JIT on a busy PC) or a frame was dropped because the queue was
                        // full, this picture is repeated into the empty slots. Without it the file had a leading gap /
                        // holes: fewer samples than duration × FPS (CI run 96, camera-only: 90 samples in 3.2 s).
                        long next = _framesWritten == 0 ? 0 : _lastVideoEnd;
                        while (next + (item.Duration / 2) < item.Time)
                        {
                            WriteFrameWithFallback(p, item with { Time = next });
                            next += item.Duration;
                            Interlocked.Increment(ref _framesWritten);
                            Interlocked.Increment(ref _framesRepeated);
                        }

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
        finally
        {
            FinishWriter();
        }
    }

    /// <summary>
    /// Encoder thread, at its end: completes the file (Sink Writer Finalize) unless cancelled or failed, then releases
    /// the writer. The encoder thread is the writer's only user from the first frame to its release.
    /// </summary>
    private void FinishWriter()
    {
        if (_writer is null)
        {
            return;
        }

        if (!_cancel && _failure is null)
        {
            try
            {
                _hooks?.BeforeFinalize?.Invoke();
                _writer.Complete();
                _finalized = true;
            }
            catch (Exception ex) when (ex is NativeCallException or IOException)
            {
                _failure ??= ex;
                AddNotice($"The video could not be completed normally ({ex.Message}). It will be recovered from its last complete part.");
            }
        }

        EncoderNameAtEnd = _writer.Encoder?.Name;
        HardwareAtEnd = _writer.Encoder?.Hardware ?? false;
        _writer.Dispose();
        _writer = null;
        if (_cancel)
        {
            TryDelete(_path);
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

    private Thread StartThread(Action body, string name, ThreadPriority priority)
    {
        Interlocked.Increment(ref _liveThreads);
        var t = new Thread(() =>
        {
            try
            {
                _ = Com.CoInitializeEx(0, Com.COINIT_MULTITHREADED);
                body();
            }
            finally
            {
                // Threads that outlive a timed-out Stop complete ThreadsExited when the last one ends.
                if (Interlocked.Decrement(ref _liveThreads) == 0 && Volatile.Read(ref _ending) is not null)
                {
                    _threadsExited.TrySetResult();
                }
            }
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
