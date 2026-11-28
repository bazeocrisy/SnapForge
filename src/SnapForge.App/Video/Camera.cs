using System.Runtime.InteropServices;
using Microsoft.Win32;
using SnapForge.App.Video.Interop;

namespace SnapForge.App.Video;

/// <summary>Receives a camera frame: BGRA, top-down, <c>width × 4</c> bytes per row; time in QPC 100-ns units.</summary>
internal delegate void CameraFrameAction(ReadOnlySpan<byte> bgra, int width, int height, long time100ns);

/// <summary>Why a camera could not be used.</summary>
internal enum CameraFailure
{
    /// <summary>No video capture device is connected.</summary>
    NoCamera,

    /// <summary>The chosen camera is not connected any more.</summary>
    NotFound,

    /// <summary>Windows privacy settings block camera access for desktop apps.</summary>
    AccessDenied,

    /// <summary>Another app is using the camera.</summary>
    Busy,

    /// <summary>The camera was unplugged or stopped working.</summary>
    Disconnected,

    /// <summary>Any other failure (format not supported, driver error, timeout).</summary>
    Failed,
}

/// <summary>A camera problem with a message for the user (never retried silently).</summary>
internal sealed class CameraException : Exception
{
    public CameraException()
    {
    }

    public CameraException(string message)
        : base(message)
    {
    }

    public CameraException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public CameraException(CameraFailure kind, string message, int hresult = 0)
        : base(message)
    {
        Kind = kind;
        HResult = hresult;
    }

    public CameraFailure Kind { get; } = CameraFailure.Failed;

    /// <summary>The Windows Settings page that fixes it (privacy), or null.</summary>
    public string? SettingsUri => Kind == CameraFailure.AccessDenied ? PrivacySettings.CameraUri : null;

    /// <summary>The user-facing message for an HRESULT from opening or reading a camera.</summary>
    public static CameraException FromHResult(int hr, string device) => hr switch
    {
        E_ACCESSDENIED => new CameraException(CameraFailure.AccessDenied, PrivacySettings.CameraBlockedMessage, hr),
        MF_E_HW_MFT_FAILED_START_STREAMING or ERROR_BUSY or ERROR_SHARING_VIOLATION or MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED =>
            new CameraException(CameraFailure.Busy, $"{device} is being used by another app. Close that app (or turn its camera off) and try again.", hr),
        MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED or ERROR_DEVICE_NOT_CONNECTED or ERROR_DEVICE_REMOVED or MF_E_SHUTDOWN =>
            new CameraException(CameraFailure.Disconnected, $"{device} was disconnected.", hr),
        _ => new CameraException(CameraFailure.Failed, $"{device} could not be started (error 0x{hr:X8}).", hr),
    };

    // winerror.h / mferror.h values [Assumed: mferror.h is not in the scratch IDL set; documented values].
    internal const int E_ACCESSDENIED = unchecked((int)0x80070005);
    internal const int ERROR_BUSY = unchecked((int)0x800700AA);
    internal const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
    internal const int ERROR_DEVICE_NOT_CONNECTED = unchecked((int)0x8007048F);
    internal const int ERROR_DEVICE_REMOVED = unchecked((int)0x80070651);
    internal const int MF_E_HW_MFT_FAILED_START_STREAMING = unchecked((int)0xC00D3704);
    internal const int MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED = unchecked((int)0xC00D4E86);
    internal const int MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED = unchecked((int)0xC00D4E87);
    internal const int MF_E_SHUTDOWN = unchecked((int)0xC00D3E85);
}

/// <summary>
/// Windows privacy switches for the camera and microphone (Settings → Privacy &amp; security). Read from the consent
/// store in the registry — reading does not open any device. [Assumed: the registry layout is not a documented API;
/// it is only used to explain a failure early. The real check is the error Windows returns when opening.]
/// </summary>
internal static class PrivacySettings
{
    public const string CameraUri = "ms-settings:privacy-webcam";
    public const string MicrophoneUri = "ms-settings:privacy-microphone";

    public const string CameraBlockedMessage = "Windows is blocking camera access for SnapForge. Turn it on in Settings → Privacy & security → Camera (\"Camera access\" and \"Let desktop apps access your camera\"), then try again.";
    public const string MicrophoneBlockedMessage = "Windows is blocking microphone access for SnapForge. Turn it on in Settings → Privacy & security → Microphone (\"Microphone access\" and \"Let desktop apps access your microphone\"), then try again.";

    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public static bool CameraBlocked() => Blocked("webcam");

    public static bool MicrophoneBlocked() => Blocked("microphone");

    /// <summary>Opens the Windows Settings page (explicit user action only).</summary>
    public static void Open(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static bool Blocked(string capability)
    {
        try
        {
            return Denied(Registry.LocalMachine, Store + capability) || Denied(Registry.CurrentUser, Store + capability)
                || Denied(Registry.CurrentUser, Store + capability + @"\NonPackaged");
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Denied(RegistryKey root, string path)
    {
        using RegistryKey? key = root.OpenSubKey(path);
        return key?.GetValue("Value") is string v && string.Equals(v, "Deny", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// A camera that delivers BGRA frames timestamped on the recording clock (QPC). One implementation reads a real
/// device through Media Foundation (<see cref="MediaFoundationCameraSource"/>); a synthetic one is used by tests and
/// CI, which has no camera (<see cref="SyntheticCameraSource"/>). The device is open only between
/// <see cref="Start"/> and <see cref="Stop"/> — the camera light is off otherwise.
/// </summary>
internal interface ICameraSource : IDisposable
{
    /// <summary>Device name for the UI and reports ("… (SYNTHETIC)" for the test source).</summary>
    string Name { get; }

    bool IsSynthetic { get; }

    /// <summary>Frame size (valid once started).</summary>
    int Width { get; }

    int Height { get; }

    /// <summary>True between a successful <see cref="Start"/> and <see cref="Stop"/> / loss of the device.</summary>
    bool IsRunning { get; }

    /// <summary>Set when the camera was lost while running (unplugged, taken by another app); null otherwise.</summary>
    string? LostReason { get; }

    /// <summary>Frames delivered since the last start (changes whenever a new frame is available).</summary>
    long FrameSequence { get; }

    /// <summary>Raised (on the camera thread) once when the running camera is lost.</summary>
    event EventHandler<string>? Lost;

    /// <summary>Opens the device and waits for its first frame. Throws <see cref="CameraException"/>.</summary>
    void Start();

    /// <summary>Releases the device promptly (bounded wait). Restartable with <see cref="Start"/>.</summary>
    void Stop();

    /// <summary>
    /// Gives the newest frame whose time is not after <paramref name="notAfter100ns"/> to <paramref name="use"/>, under
    /// the frame lock — or the newest frame if none is that old or the qualifying one is more than 500 ms behind the
    /// newest (clock mismatch can never hide the camera). Returns false only when there is no frame at all.
    /// </summary>
    bool WithFrame(long notAfter100ns, CameraFrameAction use);
}

/// <summary>The last few camera frames (BGRA) with their times; one writer (camera thread), any readers.</summary>
internal sealed class CameraFrameRing
{
    private const int Slots = 4;

    /// <summary>A frame older than the newest by more than this is never preferred (500 ms).</summary>
    internal const long MaxStaleness100ns = 5_000_000;

    private readonly Lock _gate = new();
    private readonly byte[]?[] _data = new byte[Slots][];
    private readonly long[] _time = new long[Slots];
    private readonly bool[] _valid = new bool[Slots];
    private readonly int[] _width = new int[Slots];
    private readonly int[] _height = new int[Slots];
    private int _writing = -1;
    private int _next;
    private long _sequence;

    public long Sequence => Interlocked.Read(ref _sequence);

    /// <summary>A buffer for the next frame (the oldest slot, withdrawn from readers until <see cref="Commit"/>).</summary>
    public byte[] Rent(int width, int height)
    {
        lock (_gate)
        {
            int slot = _next;
            _valid[slot] = false;
            _writing = slot;
            int bytes = width * height * 4;
            if (_data[slot] is not { } d || d.Length != bytes)
            {
                _data[slot] = new byte[bytes];
            }

            _width[slot] = width;
            _height[slot] = height;
            return _data[slot]!;
        }
    }

    public void Commit(long time100ns)
    {
        lock (_gate)
        {
            if (_writing < 0)
            {
                return;
            }

            _time[_writing] = time100ns;
            _valid[_writing] = true;
            _next = (_writing + 1) % Slots;
            _writing = -1;
            Interlocked.Increment(ref _sequence);
        }
    }

    /// <summary>Forgets all frames (a restarted camera must not show a stale picture).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_valid);
            _writing = -1;
        }
    }

    public bool WithFrame(long notAfter100ns, CameraFrameAction use)
    {
        ArgumentNullException.ThrowIfNull(use);
        lock (_gate)
        {
            int best = -1, newest = -1;
            for (int i = 0; i < Slots; i++)
            {
                if (!_valid[i])
                {
                    continue;
                }

                if (_time[i] <= notAfter100ns && (best < 0 || _time[i] > _time[best]))
                {
                    best = i;
                }

                if (newest < 0 || _time[i] > _time[newest])
                {
                    newest = i;
                }
            }

            // No frame old enough (the camera runs ahead of the screen time, e.g. a clock offset): use the newest one
            // rather than hiding the camera. Bounded staleness: if the qualifying frame is far behind the newest,
            // the clocks disagree and the newest picture is used too.
            int pick = best >= 0 && _time[newest] - _time[best] <= MaxStaleness100ns ? best : newest;
            if (pick < 0)
            {
                return false;
            }

            use(_data[pick], _width[pick], _height[pick], _time[pick]);
            return true;
        }
    }
}

/// <summary>A camera format offered by the device.</summary>
internal readonly record struct CameraFormat(int Width, int Height, double FramesPerSecond, Guid Subtype, int Index);

/// <summary>Chooses the camera format closest to the wanted size and frame rate (pure; unit-tested).</summary>
internal static class CameraFormatChooser
{
    /// <summary>
    /// Prefers formats at least <paramref name="fps"/> (frame rates below cost the most), then the size closest to
    /// <paramref name="width"/> × <paramref name="height"/> (larger sizes cost more CPU than smaller ones lose), then
    /// uncompressed formats (no MJPG decode).
    /// </summary>
    public static CameraFormat? Choose(IReadOnlyList<CameraFormat> formats, int width = 1280, int height = 720, double fps = 30)
    {
        ArgumentNullException.ThrowIfNull(formats);
        CameraFormat? best = null;
        double bestScore = double.MaxValue;
        foreach (CameraFormat f in formats)
        {
            if (f.Width < 16 || f.Height < 16)
            {
                continue;
            }

            double rate = f.FramesPerSecond <= 0 ? 0 : f.FramesPerSecond;
            double fpsPenalty = rate + 0.5 < fps ? (fps - rate) * 40 : Math.Max(0, rate - fps) * 0.5;
            double area = f.Width * (double)f.Height, want = width * (double)height;
            double sizePenalty = area >= want ? (area / want - 1) * 30 : (want / area - 1) * 60;
            double formatPenalty = f.Subtype == Mf.VideoFormat_MJPG ? 2 : 0;
            double score = fpsPenalty + sizePenalty + formatPenalty;
            if (score < bestScore)
            {
                bestScore = score;
                best = f;
            }
        }

        return best;
    }

    /// <summary>
    /// The capture size to ask the camera for (RC3): camera-only recordings use the camera picture as the video, so
    /// 1920×1080; for an overlay, 1280×720 while it gives at least 1.25× the overlay's height in source pixels (the
    /// area-average downscale then has real detail to average), otherwise 1920×1080. Never more than 1080p (CPU and USB
    /// bandwidth) — <see cref="Choose"/> then picks the closest format the camera offers at full frame rate, so tiny
    /// defaults such as 160×120 or 320×240 are only used by cameras that offer nothing larger.
    /// </summary>
    public static (int Width, int Height) WantedSize(SnapForge.Core.Settings.StudioMode mode, int frameWidth, int frameHeight, SnapForge.Core.Settings.CameraOverlayShape shape, SnapForge.Core.Settings.CameraOverlaySize size)
    {
        if (mode == SnapForge.Core.Settings.StudioMode.Camera || frameWidth < 16 || frameHeight < 16)
        {
            return (1920, 1080);
        }

        // Circle: a square of the camera's full height is used; rounded rectangle: the 16:9 picture. Either way the
        // camera height must cover the overlay height.
        OverlayLayout box = OverlayLayout.For(frameWidth, frameHeight, shape, size, SnapForge.Core.Settings.CameraCorner.BottomRight, mirror: false);
        return box.Height * 1.25 <= 720 ? (1280, 720) : (1920, 1080);
    }
}

/// <summary>YUV → BGRA conversion for cameras whose reader does not offer RGB32 (BT.601 limited range).</summary>
internal static unsafe class CameraPixels
{
    /// <summary>NV12 (Y plane, then interleaved U/V at half resolution) to BGRA.</summary>
    public static void Nv12ToBgra(byte* y, int yPitch, byte* uv, int uvPitch, int width, int height, Span<byte> bgra)
    {
        for (int row = 0; row < height; row++)
        {
            byte* yr = y + ((long)row * yPitch);
            byte* uvr = uv + ((long)(row / 2) * uvPitch);
            Span<byte> o = bgra.Slice(row * width * 4, width * 4);
            for (int x = 0; x < width; x++)
            {
                int c = yr[x], u = uvr[x & ~1], v = uvr[(x & ~1) + 1];
                Write(o.Slice(x * 4, 4), c, u, v);
            }
        }
    }

    /// <summary>YUY2 (Y0 U Y1 V per two pixels) to BGRA.</summary>
    public static void Yuy2ToBgra(byte* src, int pitch, int width, int height, Span<byte> bgra)
    {
        for (int row = 0; row < height; row++)
        {
            byte* r = src + ((long)row * pitch);
            Span<byte> o = bgra.Slice(row * width * 4, width * 4);
            for (int x = 0; x < width; x++)
            {
                byte* pair = r + ((x & ~1) * 2);
                Write(o.Slice(x * 4, 4), (x & 1) == 0 ? pair[0] : pair[2], pair[1], pair[3]);
            }
        }
    }

    private static void Write(Span<byte> px, int y, int u, int v)
    {
        int c = (y - 16) * 298, d = u - 128, e = v - 128;
        px[0] = Clamp((c + (516 * d) + 128) >> 8);
        px[1] = Clamp((c - (100 * d) - (208 * e) + 128) >> 8);
        px[2] = Clamp((c + (409 * e) + 128) >> 8);
        px[3] = 255;
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
}

/// <summary>
/// A real camera through Media Foundation: <c>MFEnumDeviceSources</c> (video capture) → <c>IMFActivate::ActivateObject</c>
/// → <c>MFCreateSourceReaderFromMediaSource</c> with advanced video processing, a native format close to 1280×720 at
/// 30 FPS, output RGB32 (NV12/YUY2 converted here if RGB32 is refused). One MTA thread owns every native object: it
/// opens the device, reads frames into a <see cref="CameraFrameRing"/>, and shuts the device down when it exits —
/// no other thread releases them, so a slow driver can never cause a use-after-release.
/// </summary>
internal sealed unsafe class MediaFoundationCameraSource : ICameraSource
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly string? _symbolicLink;
    private readonly int _wantWidth, _wantHeight, _wantFps;
    private readonly CameraFrameRing _ring = new();
    private readonly Lock _lifecycle = new();
    private readonly Lock _comGate = new();
    private Thread? _thread;
    private ManualResetEventSlim? _exited;
    private volatile bool _stop;
    private volatile bool _running;
    private nint _liveSource; // for Shutdown from Stop() when the reader thread is stuck (guarded by _comGate)
    private long _offset;
    private bool _haveOffset;

    public MediaFoundationCameraSource(string? symbolicLink, string name, int width = 1280, int height = 720, int fps = 30)
    {
        _symbolicLink = symbolicLink;
        Name = name;
        (_wantWidth, _wantHeight, _wantFps) = (width, height, fps);
    }

    public event EventHandler<string>? Lost;

    public string Name { get; private set; }

    public bool IsSynthetic => false;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsRunning => _running;

    public string? LostReason { get; private set; }

    public long FrameSequence => _ring.Sequence;

    /// <summary>The native format chosen (diagnostics).</summary>
    public CameraFormat? Format { get; private set; }

    /// <summary>The cameras Windows lists. Does not open any of them.</summary>
    public static IReadOnlyList<Mf.VideoDevice> Enumerate() => Mf.EnumerateVideoDevices();

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_running)
            {
                return;
            }

            if (_thread is { IsAlive: true })
            {
                // A previous stop gave up waiting; the old thread still owns the device.
                if (!_exited!.Wait(StopTimeout))
                {
                    throw new CameraException(CameraFailure.Busy, $"{Name} is still closing. Try again in a moment.");
                }
            }

            _exited?.Dispose();
            _ring.Clear();
            _stop = false;
            LostReason = null;
            _haveOffset = false;
            var ready = new ManualResetEventSlim();
            var exited = new ManualResetEventSlim();
            _exited = exited;
            CameraException? failure = null;
            var thread = new Thread(() => Run(ready, exited, e => failure = e)) { IsBackground = true, Name = "SnapForge camera" };
            _thread = thread;
            thread.Start();
            bool signalled = ready.Wait(OpenTimeout);
            ready.Dispose();
            if (!signalled)
            {
                _stop = true;
                RecordingLog.Write($"camera '{Name}' did not deliver a frame within {OpenTimeout.TotalSeconds:0} s; its thread releases it when it exits.");
                throw new CameraException(CameraFailure.Failed, $"{Name} did not respond. Unplug it and plug it in again, or choose another camera.");
            }

            if (failure is not null)
            {
                exited.Wait(StopTimeout);
                throw failure;
            }

            _running = true;
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            _stop = true;
            _running = false;
            if (_exited is not { } exited || _thread is not { IsAlive: true })
            {
                return;
            }

            if (exited.Wait(StopTimeout))
            {
                return;
            }

            // The reader is blocked inside the driver: shutting the source down makes ReadSample return. The reader
            // thread still owns (and releases) every object.
            lock (_comGate)
            {
                if (_liveSource != 0)
                {
                    _ = Com.Call(_liveSource, 12); // IMFMediaSource::Shutdown — slot 12
                }
            }

            if (!exited.Wait(StopTimeout))
            {
                RecordingLog.Write($"camera '{Name}' thread did not stop within {2 * StopTimeout.TotalSeconds:0} s; its native objects are left to that thread (released when it exits).");
            }
        }
    }

    public bool WithFrame(long notAfter100ns, CameraFrameAction use) => _ring.WithFrame(notAfter100ns, use);

    public void Dispose()
    {
        Stop();
        if (_thread is not { IsAlive: true })
        {
            _exited?.Dispose();
            _exited = null;
        }
    }

    private enum Layout
    {
        Rgb32,
        Nv12,
        Yuy2,
    }

    private void Run(ManualResetEventSlim ready, ManualResetEventSlim exited, Action<CameraException> fail)
    {
        _ = Com.CoInitializeEx(0, Com.COINIT_MULTITHREADED);
        nint source = 0, activate = 0, reader = 0;
        bool first = true;
        try
        {
            if (Mf.ActivateVideoDevice(_symbolicLink) is not { } opened)
            {
                fail(_symbolicLink is null
                    ? new CameraException(CameraFailure.NoCamera, "No camera was found. Connect a camera, or choose Screen only.")
                    : new CameraException(CameraFailure.NotFound, $"{Name} is not connected. Connect it or choose another camera."));
                ready.Set();
                return;
            }

            (source, activate, Mf.VideoDevice device) = opened;
            Name = device.Name;
            lock (_comGate)
            {
                _liveSource = source;
            }

            try
            {
                reader = Mf.CreateSourceReaderFromMediaSource(source, advancedProcessing: true);
            }
            catch (NativeCallException)
            {
                reader = Mf.CreateSourceReaderFromMediaSource(source, advancedProcessing: false);
            }

            (Layout layout, int pitch) = Configure(reader);
            while (!_stop)
            {
                int hr = Mf.TryReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out uint flags, out long ts, out nint sample);
                if (hr < 0 || (flags & (Mf.MF_SOURCE_READERF_ERROR | Mf.MF_SOURCE_READERF_ENDOFSTREAM)) != 0)
                {
                    Com.Release(ref sample);
                    if (_stop)
                    {
                        break;
                    }

                    CameraException e = CameraException.FromHResult(hr < 0 ? hr : CameraException.ERROR_DEVICE_NOT_CONNECTED, Name);
                    if (first)
                    {
                        fail(e);
                        ready.Set();
                    }
                    else
                    {
                        ReportLost(e.Kind == CameraFailure.Busy ? $"{Name} was taken over by another app; the camera overlay was removed." : $"{Name} was disconnected or stopped working; the camera overlay was removed.");
                    }

                    return;
                }

                if (sample == 0)
                {
                    continue; // stream tick (gap)
                }

                try
                {
                    Deliver(sample, ts, layout, pitch);
                }
                finally
                {
                    Com.Release(ref sample);
                }

                if (first)
                {
                    first = false;
                    ready.Set();
                }
            }
        }
        catch (NativeCallException ex)
        {
            if (first)
            {
                fail(CameraException.FromHResult(ex.HResult, Name));
                ready.Set();
            }
            else
            {
                ReportLost($"{Name} stopped working ({ex.Message}); the camera overlay was removed.");
            }
        }
        catch (CameraException ex)
        {
            fail(ex);
            ready.Set();
        }
        finally
        {
            lock (_comGate)
            {
                _liveSource = 0;
            }

            Com.Release(ref reader);
            Mf.ShutdownVideoDevice(ref source, ref activate); // camera light off
            _running = false;
            ready.Set();
            exited.Set();
        }
    }

    private void ReportLost(string reason)
    {
        _running = false;
        LostReason = reason;
        RecordingLog.Write("camera lost: " + reason);
        Lost?.Invoke(this, reason);
    }

    private (Layout Layout, int Pitch) Configure(nint reader)
    {
        Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_ALL_STREAMS, false);
        Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
        var formats = new List<CameraFormat>();
        for (int i = 0; i < 512; i++)
        {
            nint t = Mf.GetNativeMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, i);
            if (t == 0)
            {
                break;
            }

            try
            {
                ulong size = Mf.GetUInt64(t, Mf.MT_FRAME_SIZE) ?? 0;
                ulong rate = Mf.GetUInt64(t, Mf.MT_FRAME_RATE) ?? 0;
                uint num = (uint)(rate >> 32), den = (uint)(rate & 0xFFFFFFFF);
                formats.Add(new CameraFormat((int)(size >> 32), (int)(size & 0xFFFFFFFF), den == 0 ? 0 : num / (double)den, Mf.GetGuid(t, Mf.MT_SUBTYPE) ?? Guid.Empty, i));
            }
            finally
            {
                Com.Release(ref t);
            }
        }

        bool nativeSet = false;
        if (CameraFormatChooser.Choose(formats, _wantWidth, _wantHeight, _wantFps) is { } chosen)
        {
            Format = chosen;
            nint native = Mf.GetNativeMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, chosen.Index);
            if (native != 0)
            {
                nativeSet = Mf.TrySetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, native) >= 0;
                Com.Release(ref native);
            }
        }

        // RC3 diagnostics: which native format was used (a refused native type leaves the driver's default, which
        // the reader's video processor would then scale to the requested size — a soft or blocky camera picture).
        RecordingLog.Write($"camera '{Name}': {formats.Count} native formats, wanted {_wantWidth}×{_wantHeight} at {_wantFps} FPS, chose "
            + (Format is { } cf ? $"{cf.Width}×{cf.Height} at {cf.FramesPerSecond:0.##} FPS ({(cf.Subtype == Mf.VideoFormat_MJPG ? "MJPG" : cf.Subtype == Mf.VideoFormat_NV12 ? "NV12" : cf.Subtype == Mf.VideoFormat_YUY2 ? "YUY2" : cf.Subtype.ToString())}), {(nativeSet ? "set" : "NOT set — driver default in use")}" : "nothing (driver default in use)"));

        // Ask for RGB32 (the reader's video processor converts); fall back to formats converted here.
        foreach ((Guid subtype, Layout layout) in new[] { (Mf.VideoFormat_RGB32, Layout.Rgb32), (Mf.VideoFormat_NV12, Layout.Nv12), (Mf.VideoFormat_YUY2, Layout.Yuy2) })
        {
            nint want = Mf.CreateMediaType();
            try
            {
                Mf.SetGuid(want, Mf.MT_MAJOR_TYPE, Mf.MediaType_Video);
                Mf.SetGuid(want, Mf.MT_SUBTYPE, subtype);
                if (Format is { } f)
                {
                    Mf.SetRatio(want, Mf.MT_FRAME_SIZE, (uint)f.Width, (uint)f.Height);
                }

                if (Mf.TrySetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, want) < 0)
                {
                    continue;
                }
            }
            finally
            {
                Com.Release(ref want);
            }

            nint current = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            try
            {
                ulong size = Mf.GetUInt64(current, Mf.MT_FRAME_SIZE) ?? 0;
                Width = (int)(size >> 32);
                Height = (int)(size & 0xFFFFFFFF);
                if (Width < 16 || Height < 16)
                {
                    throw new CameraException(CameraFailure.Failed, $"{Name} reported an unusable picture size.");
                }

                int defaultPitch = layout == Layout.Rgb32 ? Width * 4 : layout == Layout.Yuy2 ? Width * 2 : Width;
                // Contiguous buffers without MF_MT_DEFAULT_STRIDE: top-down, no row padding (as measured for the
                // reader's RGB32 output on CI run 106). Buffers with IMF2DBuffer report their own pitch.
                int pitch = Mf.GetInt32(current, Mf.MT_DEFAULT_STRIDE) ?? defaultPitch;
                RecordingLog.Write($"camera '{Name}': reader delivers {Width}×{Height} as {layout}");
                return (layout, pitch);
            }
            finally
            {
                Com.Release(ref current);
            }
        }

        throw new CameraException(CameraFailure.Failed, $"{Name} does not offer a picture format SnapForge can read.");
    }

    private void Deliver(nint sample, long sampleTime, Layout layout, int fallbackPitch)
    {
        long arrival = Qpc.Now100ns();
        // Camera sample times have their own origin: map them onto QPC with the smallest offset seen (no jitter,
        // never in the future); re-anchor after a discontinuity (> 200 ms off).
        long candidate = arrival - sampleTime;
        if (!_haveOffset || candidate < _offset || candidate - _offset > 2_000_000)
        {
            _offset = candidate;
            _haveOffset = true;
        }

        long time = Math.Min(arrival, sampleTime + _offset);
        nint buffer = Mf.ConvertToContiguousBuffer(sample);
        try
        {
            byte* scan0 = Mf.LockRows(buffer, fallbackPitch, Height, out int pitch, out bool locked2D);
            try
            {
                byte[] dst = _ring.Rent(Width, Height);
                switch (layout)
                {
                    case Layout.Rgb32:
                        fixed (byte* d = dst)
                        {
                            for (int y = 0; y < Height; y++)
                            {
                                uint* s = (uint*)(scan0 + ((long)y * pitch));
                                uint* o = (uint*)(d + ((long)y * Width * 4));
                                for (int x = 0; x < Width; x++)
                                {
                                    o[x] = s[x] | 0xFF000000; // RGB32 has an undefined alpha byte
                                }
                            }
                        }

                        break;
                    case Layout.Nv12:
                        CameraPixels.Nv12ToBgra(scan0, pitch, scan0 + ((long)pitch * Height), pitch, Width, Height, dst);
                        break;
                    default:
                        CameraPixels.Yuy2ToBgra(scan0, pitch, Width, Height, dst);
                        break;
                }

                _ring.Commit(time);
            }
            finally
            {
                Mf.UnlockRows(buffer, locked2D);
            }
        }
        finally
        {
            Com.Release(ref buffer);
        }
    }
}

/// <summary>
/// SYNTHETIC test camera (CI has no camera): a magenta picture with a yellow bar sweeping across it once per second,
/// at a fixed frame rate on the recording clock. Can simulate being unplugged
/// or failing to open. Every report that uses it says SYNTHETIC.
/// </summary>
internal sealed class SyntheticCameraSource(int width = 640, int height = 360, int fps = 30) : ICameraSource
{
    public const uint Background = 0xFFFF00FF; // BGRA magenta
    public const uint Bar = 0xFFFFFF00;        // BGRA yellow

    private readonly CameraFrameRing _ring = new();
    private readonly Lock _lifecycle = new();
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _running;

    public event EventHandler<string>? Lost;

    public string Name => "Synthetic test camera (SYNTHETIC)";

    public bool IsSynthetic => true;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public bool IsRunning => _running;

    public string? LostReason { get; private set; }

    public long FrameSequence => _ring.Sequence;

    /// <summary>Makes the next <see cref="Start"/> fail like a real camera would (tests).</summary>
    public CameraFailure? FailNextStart { get; set; }

    /// <summary>Number of times the "device" was opened (tests: the camera must not open in Screen-only mode).</summary>
    public int StartCount { get; private set; }

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_running)
            {
                return;
            }

            if (FailNextStart is { } failure)
            {
                FailNextStart = null;
                throw failure == CameraFailure.AccessDenied
                    ? new CameraException(failure, PrivacySettings.CameraBlockedMessage, CameraException.E_ACCESSDENIED)
                    : new CameraException(failure, $"{Name}: simulated {failure}.");
            }

            StartCount++;
            _ring.Clear();
            LostReason = null;
            _stop = false;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "SnapForge synthetic camera" };
            _thread.Start();
            SpinWait.SpinUntil(() => _ring.Sequence > 0 || !_running, TimeSpan.FromSeconds(2));
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            _stop = true;
            _thread?.Join(TimeSpan.FromSeconds(2));
            _running = false;
        }
    }

    /// <summary>Simulates the camera being unplugged while running.</summary>
    public void SimulateUnplug()
    {
        _stop = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _running = false;
        LostReason = $"{Name} was disconnected (simulated); the camera overlay was removed.";
        Lost?.Invoke(this, LostReason);
    }

    public bool WithFrame(long notAfter100ns, CameraFrameAction use) => _ring.WithFrame(notAfter100ns, use);

    public void Dispose() => Stop();

    /// <summary>The bar's left edge for a frame index (also used by tests to predict the picture).</summary>
    public static int BarX(long index, int width, int fps) => (int)(index % fps * width / fps);

    private void Run()
    {
        long period = 10_000_000L / fps;
        long start = Qpc.Now100ns();
        long index = 0;
        while (!_stop)
        {
            long due = start + (index * period);
            long now = Qpc.Now100ns();
            if (now < due)
            {
                Thread.Sleep(TimeSpan.FromTicks(Math.Max(10_000, due - now)));
                continue;
            }

            byte[] buffer = _ring.Rent(Width, Height);
            Span<uint> px = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan());
            int barX = BarX(index, Width, fps), barW = Math.Max(4, Width / 8);
            for (int y = 0; y < Height; y++)
            {
                Span<uint> row = px.Slice(y * Width, Width);
                row.Fill(Background);
                int x0 = barX, x1 = Math.Min(Width, barX + barW);
                if (x1 > x0)
                {
                    row[x0..x1].Fill(Bar);
                }
            }

            _ring.Commit(due);
            index++;
        }
    }
}
