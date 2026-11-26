using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SnapForge.App.Video.Interop;

namespace SnapForge.App.Video;

/// <summary>
/// <c>SnapForge.exe --video-probe &lt;out.json&gt;</c>: reports what this PC offers for screen recording (Windows.Graphics
/// .Capture, Direct3D adapter, H.264/AAC encoders, audio endpoints, capture exclusion, display modes) and runs two smoke
/// tests: a short synthetic MP4 encode (validated by reading it back) and one second of real screen capture.
/// Used by CI to establish which parts of video recording can be verified automatically.
/// </summary>
internal static unsafe partial class VideoProbe
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

#pragma warning disable SYSLIB1054 // DEVMODEW has inline strings; the classic marshaller handles them.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string? device, int mode, ref DevMode devMode);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(long point, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    public static int Run(string outPath)
    {
        var report = new Dictionary<string, object?>
        {
            ["os"] = Environment.OSVersion.VersionString,
            ["build"] = Environment.OSVersion.Version.Build,
        };
        Step(report, "mfStartup", () => $"0x{Mf.MFStartup(Mf.MF_VERSION, 0):X8}");
        Step(report, "graphicsCaptureSupported", () => GraphicsCapture.IsSupported());
        Step(report, "d3d", () =>
        {
            (nint device, nint context, bool hardware) = D3D11.CreateDevice();
            string name = D3D11.AdapterName(device);
            Com.Release(ref context);
            Com.Release(ref device);
            return new { hardware, adapter = name };
        });
        Step(report, "h264HardwareEncoders", () => Mf.EnumerateEncoders(Mf.MFT_CATEGORY_VIDEO_ENCODER, Mf.MediaType_Video, Mf.VideoFormat_H264, hardware: true));
        Step(report, "h264SoftwareEncoders", () => Mf.EnumerateEncoders(Mf.MFT_CATEGORY_VIDEO_ENCODER, Mf.MediaType_Video, Mf.VideoFormat_H264, hardware: false));
        Step(report, "aacEncoders", () => Mf.EnumerateEncoders(Mf.MFT_CATEGORY_AUDIO_ENCODER, Mf.MediaType_Audio, Mf.AudioFormat_AAC, hardware: false));
        Step(report, "renderEndpoints", () => CoreAudio.ListEndpoints(CoreAudio.eRender).Select(e => e.Name).ToList());
        Step(report, "captureEndpoints", () => CoreAudio.ListEndpoints(CoreAudio.eCapture).Select(e => e.Name).ToList());
        Step(report, "excludeFromCapture", () =>
        {
            var p = new System.Windows.Interop.HwndSourceParameters("probe") { Width = 10, Height = 10 };
            using var src = new System.Windows.Interop.HwndSource(p);
            return SetWindowDisplayAffinity(src.Handle, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
        });
        Step(report, "displayModes", () =>
        {
            var modes = new SortedSet<string>();
            var dm = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
            for (int i = 0; EnumDisplaySettingsW(null, i, ref dm) && i < 500; i++)
            {
                modes.Add($"{dm.PelsWidth}x{dm.PelsHeight}");
            }

            var current = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
            _ = EnumDisplaySettingsW(null, -1 /* ENUM_CURRENT_SETTINGS */, ref current);
            return new { current = $"{current.PelsWidth}x{current.PelsHeight}@{current.DisplayFrequency}", available = modes.ToList() };
        });
        string dir = Path.Combine(Path.GetTempPath(), "snapforge-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Step(report, "syntheticEncode", () => SyntheticEncode(Path.Combine(dir, "synthetic.mp4")));
        Step(report, "screenCapture1s", ScreenCaptureOneSecond);
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }

        string json = JsonSerializer.Serialize(report, Json);
        File.WriteAllText(outPath, json);
        Console.WriteLine(json);
        return 0;
    }

    private static void Step(Dictionary<string, object?> report, string name, Func<object?> action)
    {
        try
        {
            report[name] = action();
        }
#pragma warning disable CA1031 // A probe records every failure instead of stopping.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report[name] = new { error = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    /// <summary>2 s of moving colour bars + a 440 Hz tone → fragmented MP4, then read back with the source reader.</summary>
    private static object SyntheticEncode(string path)
    {
        const int w = 320, h = 240, fps = 30, frames = 60;
        var sw = Stopwatch.StartNew();
        using (var writer = new Mp4Writer(path, new VideoStreamFormat(w, h, fps, 1_000_000), withAudio: true, fragmented: true, EncoderInput.SystemMemory, 0))
        {
            byte[] frame = new byte[w * h * 4];
            short[] audio = new short[Mp4Writer.AudioSampleRate / fps * 2];
            long frameDuration = 10_000_000L / fps;
            for (int i = 0; i < frames; i++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int o = ((y * w) + x) * 4;
                        frame[o] = (byte)((x + (i * 4)) & 0xFF);
                        frame[o + 1] = (byte)(y & 0xFF);
                        frame[o + 2] = (byte)(i * 4);
                        frame[o + 3] = 255;
                    }
                }

                fixed (byte* p = frame)
                {
                    writer.WriteFrame(p, w * 4, i * frameDuration, frameDuration);
                }

                for (int s = 0; s < audio.Length / 2; s++)
                {
                    long n = ((long)i * (audio.Length / 2)) + s;
                    short v = (short)(Math.Sin(2 * Math.PI * 440 * n / Mp4Writer.AudioSampleRate) * 8000);
                    audio[s * 2] = v;
                    audio[(s * 2) + 1] = v;
                }

                writer.WriteAudio(audio, i * frameDuration);
            }

            writer.Complete();
        }

        long encodeMs = sw.ElapsedMilliseconds;
        return new { encodeMs, bytes = new FileInfo(path).Length, readBack = ReadBack(path) };
    }

    /// <summary>Reads an MP4 back: duration and the number of decodable video and audio samples.</summary>
    internal static object ReadBack(string path)
    {
        nint reader = Mf.CreateSourceReader(path, 0);
        try
        {
            long? duration = Mf.GetDuration(reader);
            int video = 0, audio = 0;
            long lastVideo = -1;
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint s) && video < 100_000)
            {
                if (s == 0)
                {
                    break;
                }

                video++;
                lastVideo = ts;
                Com.Release(ref s);
            }

            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out _, out nint s) && audio < 1_000_000)
            {
                if (s == 0)
                {
                    break;
                }

                audio++;
                Com.Release(ref s);
            }

            return new { durationMs = duration / 10_000, videoSamples = video, lastVideoMs = lastVideo / 10_000, audioSamples = audio };
        }
        finally
        {
            Com.Release(ref reader);
        }
    }

    /// <summary>Captures the primary monitor for one second through Windows.Graphics.Capture (no encoding).</summary>
    private static object ScreenCaptureOneSecond()
    {
        _ = WinRt.RoInitialize(1 /* MTA */);
        (nint device, nint context, bool hardware) = D3D11.CreateDevice();
        nint winrtDevice = 0, item = 0, pool = 0, session = 0;
        try
        {
            winrtDevice = D3D11.CreateWinRtDevice(device);
            nint monitor = MonitorFromPoint(0, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            item = GraphicsCapture.CreateItem(monitor, window: false);
            GraphicsCapture.SizeInt32 size = GraphicsCapture.ItemSize(item);
            pool = GraphicsCapture.CreateFramePool(winrtDevice, size, 2);
            session = GraphicsCapture.CreateSession(pool, item);
            bool cursor = GraphicsCapture.SetCursorCapture(session, true);
            GraphicsCapture.StartCapture(session);
            int frames = 0;
            string? firstSize = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1000)
            {
                nint frame = GraphicsCapture.TryGetNextFrame(pool);
                if (frame == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                (nint texture, long _, GraphicsCapture.SizeInt32 content) = GraphicsCapture.FrameTexture(frame);
                firstSize ??= $"{content.Width}x{content.Height}";
                Com.Release(ref texture);
                GraphicsCapture.Close(ref frame);
                frames++;
            }

            return new { item = $"{size.Width}x{size.Height}", frames, firstFrame = firstSize, cursorCapture = cursor, hardwareDevice = hardware };
        }
        finally
        {
            GraphicsCapture.Close(ref session);
            GraphicsCapture.Close(ref pool);
            Com.Release(ref item);
            Com.Release(ref winrtDevice);
            Com.Release(ref context);
            Com.Release(ref device);
        }
    }
}
