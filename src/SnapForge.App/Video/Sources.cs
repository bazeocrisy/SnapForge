using System.Diagnostics;
using System.Runtime.InteropServices;
using SnapForge.App.Video.Interop;

namespace SnapForge.App.Video;

/// <summary>The shared recording clock source: QPC in 100-ns units (the time base of WGC frames and WASAPI packets).</summary>
internal static class Qpc
{
    public static long Now100ns()
    {
        long ts = Stopwatch.GetTimestamp();
        long f = Stopwatch.Frequency;
        return ((ts / f) * 10_000_000) + ((ts % f) * 10_000_000 / f);
    }
}

internal enum FramePoll
{
    /// <summary>A new frame was copied into the buffer.</summary>
    NewFrame,

    /// <summary>Nothing changed on screen since the last frame.</summary>
    NoChange,

    /// <summary>The source is gone (window closed, display disconnected); see <see cref="IFrameSource.EndReason"/>.</summary>
    Ended,
}

/// <summary>Where recorded frames come from. Width and height are fixed for a recording (even, ≥ 16).</summary>
internal unsafe interface IFrameSource : IDisposable
{
    int Width { get; }

    int Height { get; }

    string? EndReason { get; }

    /// <summary>Description for reports: what is captured and on which adapter.</summary>
    string Description { get; }

    void Start();

    /// <summary>Copies the newest frame (BGRA, top-down, <paramref name="stride"/> bytes per row) if there is one.</summary>
    FramePoll Poll(byte* destination, int stride, out long sourceTime100ns);
}

/// <summary>
/// Screen frames through Windows.Graphics.Capture for a monitor or window, cropped to a region. Windows shows its own
/// capture indicator; SnapForge does not disable it (VID: preserve OS privacy indicators).
/// </summary>
internal sealed unsafe partial class ScreenFrameSource : IFrameSource
{
    private readonly nint _target;
    private readonly bool _isWindow;
    private readonly int _cropLeft;
    private readonly int _cropTop;
    private readonly bool _cursor;
    private nint _device, _context, _winrtDevice, _item, _pool, _session, _staging;
    private GraphicsCapture.SizeInt32 _poolSize;
    private long _lastAliveCheck;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int L, T, R, B;
        public int WL, WT, WR, WB;
        public uint Flags;
    }

    /// <param name="target">HMONITOR or HWND.</param>
    /// <param name="crop">Region inside the item in physical pixels, or null for all of it.</param>
    public ScreenFrameSource(nint target, bool isWindow, (int Left, int Top, int Width, int Height)? crop, bool captureCursor)
    {
        _target = target;
        _isWindow = isWindow;
        _cursor = captureCursor;
        _ = WinRt.RoInitialize(1);
        (_device, _context, bool hardware) = D3D11.CreateDevice();
        try
        {
            Adapter = D3D11.AdapterName(_device) + (hardware ? string.Empty : " (WARP software rendering)");
            _winrtDevice = D3D11.CreateWinRtDevice(_device);
            _item = GraphicsCapture.CreateItem(target, isWindow);
            _poolSize = GraphicsCapture.ItemSize(_item);
            (int l, int t, int w, int h) = crop ?? (0, 0, _poolSize.Width, _poolSize.Height);
            l = Math.Clamp(l, 0, Math.Max(0, _poolSize.Width - 16));
            t = Math.Clamp(t, 0, Math.Max(0, _poolSize.Height - 16));
            w = Math.Min(w, _poolSize.Width - l);
            h = Math.Min(h, _poolSize.Height - t);
            (_cropLeft, _cropTop) = (l, t);
            Width = Math.Max(16, w & ~1);
            Height = Math.Max(16, h & ~1);
            _staging = D3D11.CreateTexture(_device, (uint)Width, (uint)Height, staging: true);
            _pool = GraphicsCapture.CreateFramePool(_winrtDevice, _poolSize, 2);
            _session = GraphicsCapture.CreateSession(_pool, _item);
            CursorCaptureApplied = GraphicsCapture.SetCursorCapture(_session, captureCursor);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; }

    public int Height { get; }

    public string Adapter { get; }

    public bool CursorCaptureApplied { get; }

    public string? EndReason { get; private set; }

    public string Description => $"{(_isWindow ? "window" : "display")} {Width}×{Height}, adapter {Adapter}, cursor {(_cursor ? "on" : "off")}";

    public void Start() => GraphicsCapture.StartCapture(_session);

    public FramePoll Poll(byte* destination, int stride, out long sourceTime100ns)
    {
        sourceTime100ns = 0;
        if (EndReason is not null)
        {
            return FramePoll.Ended;
        }

        long now = Environment.TickCount64;
        if (now - _lastAliveCheck > 500)
        {
            _lastAliveCheck = now;
            if (_isWindow && !IsWindow(_target))
            {
                EndReason = "The recorded window was closed.";
                return FramePoll.Ended;
            }

            var mi = new MonitorInfo { Size = sizeof(MonitorInfo) };
            if (!_isWindow && !GetMonitorInfoW(_target, ref mi))
            {
                EndReason = "The recorded display was disconnected.";
                return FramePoll.Ended;
            }
        }

        // Keep only the newest frame; older ones are returned to the pool at once.
        nint frame = 0;
        while (true)
        {
            nint next = GraphicsCapture.TryGetNextFrame(_pool);
            if (next == 0)
            {
                break;
            }

            GraphicsCapture.Close(ref frame);
            frame = next;
        }

        if (frame == 0)
        {
            return FramePoll.NoChange;
        }

        try
        {
            (nint texture, long time, GraphicsCapture.SizeInt32 content) = GraphicsCapture.FrameTexture(frame);
            try
            {
                sourceTime100ns = time;
                int w = Math.Min(Width, content.Width - _cropLeft);
                int h = Math.Min(Height, content.Height - _cropTop);
                if (w > 0 && h > 0)
                {
                    D3D11.CopyRegion(_context, _staging, texture, (uint)_cropLeft, (uint)_cropTop, (uint)w, (uint)h);
                }

                D3D11.MappedSubresource map = D3D11.MapRead(_context, _staging);
                try
                {
                    bool partial = w < Width || h < Height;
                    for (int y = 0; y < Height; y++)
                    {
                        byte* dst = destination + ((long)y * stride);
                        if (partial)
                        {
                            new Span<byte>(dst, Width * 4).Clear(); // resized/shrunk source: black outside it
                        }

                        if (y < h && w > 0)
                        {
                            Buffer.MemoryCopy((byte*)map.Data + ((long)y * map.RowPitch), dst, Width * 4, w * 4);
                        }
                    }
                }
                finally
                {
                    D3D11.Unmap(_context, _staging);
                }

                // Resolution change / window resize: recreate the pool at the new size (VID-R09).
                if (content.Width != _poolSize.Width || content.Height != _poolSize.Height)
                {
                    _poolSize = content;
                    GraphicsCapture.RecreatePool(_pool, _winrtDevice, _poolSize, 2);
                }
            }
            finally
            {
                Com.Release(ref texture);
            }
        }
        finally
        {
            GraphicsCapture.Close(ref frame);
        }

        return FramePoll.NewFrame;
    }

    public void Dispose()
    {
        GraphicsCapture.Close(ref _session);
        GraphicsCapture.Close(ref _pool);
        Com.Release(ref _staging);
        Com.Release(ref _item);
        Com.Release(ref _winrtDevice);
        Com.Release(ref _context);
        Com.Release(ref _device);
    }
}

/// <summary>
/// Test source: a moving pattern with a frame counter, and a full white frame at each "flash" time. Used with
/// <see cref="SyntheticAudioSource"/> (a beep at the same instants) to measure audio/video sync (VID-R01).
/// </summary>
internal sealed unsafe class SyntheticFrameSource(int width, int height, int fps, long flashEvery100ns) : IFrameSource
{
    private long _start;
    private long _lastFrame = -1;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public string? EndReason => null;

    public string Description => $"synthetic {Width}×{Height} pattern, flash every {flashEvery100ns / 10_000} ms";

    public long StartTime => _start;

    /// <summary>Source times of the flash frames produced (diagnostics).</summary>
    public List<long> FlashTimes { get; } = [];

    public void Start() => _start = Qpc.Now100ns();

    public FramePoll Poll(byte* destination, int stride, out long sourceTime100ns)
    {
        long now = Qpc.Now100ns();
        long period = 10_000_000 / fps;
        long index = (now - _start) / period;
        sourceTime100ns = _start + (index * period);
        if (index == _lastFrame)
        {
            return FramePoll.NoChange;
        }

        _lastFrame = index;
        long t = sourceTime100ns - _start;
        bool flash = flashEvery100ns > 0 && t % flashEvery100ns < period;
        if (flash)
        {
            FlashTimes.Add(sourceTime100ns);
        }
        for (int y = 0; y < Height; y++)
        {
            uint* row = (uint*)(destination + ((long)y * stride));
            for (int x = 0; x < Width; x++)
            {
                row[x] = flash ? 0xFFFFFFFF : 0xFF000000 | (uint)((((x + (int)index) & 0xFF) << 8) | (y & 0xFF));
            }
        }

        return FramePoll.NewFrame;
    }

    public void Dispose()
    {
    }
}

/// <summary>An audio input for a recording: system sound (loopback) or a microphone.</summary>
internal interface IAudioSource : IDisposable
{
    string Name { get; }

    /// <summary>Device currently used (friendly name), or null when none is available.</summary>
    string? DeviceName { get; }

    /// <summary>Whether the source should capture. Turning a microphone off releases the device (privacy indicator off).</summary>
    bool Enabled { get; set; }

    /// <summary>
    /// Reads available packets. <paramref name="deliver"/> receives (QPC time 100 ns, interleaved stereo float,
    /// silent flag). Returns a status message when the device changed or was lost, otherwise null.
    /// </summary>
    string? Read(Action<long, ReadOnlySpan<float>, bool> deliver);
}

/// <summary>
/// WASAPI shared-mode capture. Loopback follows the default playback device (switching to Bluetooth headphones moves
/// the capture to them, VID-R08); a microphone follows the default microphone unless a specific one was chosen.
/// Device loss (unplugged, disabled) is detected and retried every two seconds (VID-R07).
/// </summary>
internal sealed unsafe class WasapiSource(string name, bool loopback, string? deviceId) : IAudioSource
{
    private nint _enumerator;
    private nint _client, _capture;
    private string? _openId;
    private long _nextCheck;
    private bool _enabled;
    private float[] _scratch = [];

    public string Name { get; } = name;

    public string? DeviceName { get; private set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                Close();
            }

            _nextCheck = 0;
        }
    }

    public string? Read(Action<long, ReadOnlySpan<float>, bool> deliver)
    {
        if (!_enabled)
        {
            return null;
        }

        string? status = null;
        long now = Environment.TickCount64;
        if (now >= _nextCheck)
        {
            _nextCheck = now + (_client == 0 ? 2000 : 1000);
            status = EnsureDevice();
        }

        if (_capture == 0)
        {
            return status;
        }

        while (true)
        {
            uint packet = CoreAudio.NextPacketSize(_capture, out int hr);
            if (hr < 0)
            {
                string lost = $"{Name}: {DeviceName ?? "device"} was disconnected. Recording continues; this source is silent until a device is available.";
                Close();
                _nextCheck = now + 500;
                return lost;
            }

            if (packet == 0)
            {
                return status;
            }

            hr = CoreAudio.GetBuffer(_capture, out float* data, out uint frames, out uint flags, out long qpc);
            if (hr < 0)
            {
                Close();
                return $"{Name}: the audio device stopped responding.";
            }

            int n = (int)frames * CoreAudio.Channels;
            if (_scratch.Length < n)
            {
                _scratch = new float[n];
            }

            bool silent = (flags & CoreAudio.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
            if (!silent)
            {
                new ReadOnlySpan<float>(data, n).CopyTo(_scratch);
            }
            else
            {
                Array.Clear(_scratch, 0, n);
            }

            CoreAudio.ReleaseBuffer(_capture, frames);
            deliver(qpc, _scratch.AsSpan(0, n), silent);
        }
    }

    /// <summary>Opens the right device, or switches when the default changed. Returns a message when it changed.</summary>
    private string? EnsureDevice()
    {
        if (_enumerator == 0)
        {
            _enumerator = CoreAudio.CreateEnumerator();
        }

        nint device = deviceId is null
            ? CoreAudio.DefaultEndpoint(_enumerator, loopback ? CoreAudio.eRender : CoreAudio.eCapture)
            : CoreAudio.EndpointById(_enumerator, deviceId);
        if (device == 0)
        {
            if (_client != 0)
            {
                Close();
                return $"{Name}: no audio device is available. Recording continues; this source is silent.";
            }

            return null;
        }

        try
        {
            string id = CoreAudio.DeviceId(device);
            if (_client != 0 && id == _openId)
            {
                return null;
            }

            bool switched = _client != 0;
            Close();
            try
            {
                (_client, _capture) = CoreAudio.OpenCapture(device, loopback);
                CoreAudio.Start(_client);
            }
            catch (NativeCallException)
            {
                Close();
                return $"{Name}: the audio device could not be opened.";
            }

            _openId = id;
            DeviceName = CoreAudio.FriendlyName(device);
            return switched ? $"{Name}: now recording from {DeviceName}." : null;
        }
        finally
        {
            Com.Release(ref device);
        }
    }

    private void Close()
    {
        if (_client != 0)
        {
            CoreAudio.Stop(_client);
        }

        Com.Release(ref _capture);
        Com.Release(ref _client);
        _openId = null;
        DeviceName = null;
    }

    public void Dispose()
    {
        Close();
        Com.Release(ref _enumerator);
    }
}

/// <summary>Test source: a 1 kHz beep of 50 ms at each flash instant of <see cref="SyntheticFrameSource"/>.</summary>
internal sealed class SyntheticAudioSource(Func<long> startTime, long flashEvery100ns) : IAudioSource
{
    private long _next = -1;
    private readonly float[] _block = new float[480 * 2];

    public string Name => "Synthetic tone";

    public string? DeviceName => "synthetic";

    public bool Enabled { get; set; } = true;

    public string? Read(Action<long, ReadOnlySpan<float>, bool> deliver)
    {
        long start = startTime();
        if (!Enabled || start == 0)
        {
            return null;
        }

        if (_next < 0)
        {
            _next = start;
        }

        // Deliver 10 ms packets up to now, timestamped exactly like a device would.
        long now = Qpc.Now100ns();
        while (_next + 100_000 <= now)
        {
            for (int i = 0; i < 480; i++)
            {
                long t = _next - start + (i * 10_000_000L / 48000);
                bool beep = t % flashEvery100ns < 500_000;
                float v = beep ? (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * t / 10_000_000.0)) : 0f;
                _block[i * 2] = v;
                _block[(i * 2) + 1] = v;
            }

            deliver(_next, _block, false);
            _next += 100_000;
        }

        return null;
    }

    public void Dispose()
    {
    }
}
