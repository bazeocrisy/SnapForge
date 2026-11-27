using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using SnapForge.App.Video;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Tests;

/// <summary>Recorder tests share the one-recording-per-process rule (VID-R14), so they never run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RecordingTestGroup
{
    public const string Name = "Recording";
}

/// <summary>
/// FA-021 fault injection on real recordings (Media Foundation software encoder, synthetic frames — the Windows CI
/// runner has no GPU, camera or audio device): double stop, stop/cancel race, a stalled encoder, a full disk while
/// writing, stop before start, and Screen + SYNTHETIC camera / camera-only recordings.
/// </summary>
[Collection(RecordingTestGroup.Name)]
public sealed class RecordingFaultTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapforge-rec-" + Guid.NewGuid().ToString("N"));

    public RecordingFaultTests()
    {
        Directory.CreateDirectory(_dir);
        _ = Mf.MFStartup(Mf.MF_VERSION, 0);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // a deliberately stalled thread may still hold a file for a moment
        }
    }

    private static RecorderOptions Options => new(30, VideoQuality.Standard, RecordSystemAudio: false, RecordMicrophone: false, null, 1L * 1024 * 1024, 1);

    [Fact]
    public void Concurrent_stops_finalize_once_and_return_the_same_result()
    {
        string path = Path.Combine(_dir, "double-stop.mp4");
        using ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(320, 240, 30, 0), [], Options, path);
        rec.Start();
        Thread.Sleep(1500);
        var results = new RecordingResult[6];
        Parallel.For(0, results.Length, i => results[i] = rec.Stop());
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.True(results[0].Finalized);
        Assert.False(results[0].Stalled);
        Assert.Same(results[0], rec.Stop()); // and after it finished
        Assert.False(rec.Cancel());           // a cancel after the stop changes nothing
        Assert.True(File.Exists(path));
        Assert.True(Mp4Inspector.Inspect(path).IsComplete);
        Assert.False(ScreenRecorder.IsRecording);
    }

    [Fact]
    public void Stop_and_cancel_race_leaves_a_consistent_outcome()
    {
        for (int round = 0; round < 3; round++)
        {
            string path = Path.Combine(_dir, $"race-{round}.mp4");
            using ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(160, 120, 30, 0), [], Options, path);
            rec.Start();
            Thread.Sleep(700);
            RecordingResult? stopped = null;
            bool cancelled = false;
            Parallel.Invoke(() => stopped = rec.Stop(), () => cancelled = rec.Cancel());
            Assert.NotNull(stopped);
            if (cancelled)
            {
                Assert.True(stopped!.Cancelled);
                Assert.False(File.Exists(path));
            }
            else
            {
                Assert.True(stopped!.Finalized);
                Assert.True(Mp4Inspector.Inspect(path).IsComplete);
            }
        }
    }

    [Fact]
    public async Task Stalled_encoder_is_reported_within_the_timeout_and_the_file_stays_recoverable()
    {
        string path = Path.Combine(_dir, "stall.mp4");
        using var gate = new ManualResetEventSlim();
        var hooks = new RecorderHooks
        {
            JoinTimeout = TimeSpan.FromSeconds(1.5),
            BeforeVideoWrite = written =>
            {
                if (written == 90)
                {
                    gate.Wait(TimeSpan.FromSeconds(60)); // the encoder hangs after 3 s of video
                }
            },
        };
        ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(320, 240, 30, 0), [], Options, path, hooks);
        rec.Start();
        Thread.Sleep(4000);
        var sw = Stopwatch.StartNew();
        RecordingResult r = rec.Stop();
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1.5 * 3 + 2), $"Stop took {sw.Elapsed}");
        Assert.True(r.Stalled);
        Assert.False(r.Finalized);
        Assert.Contains(r.Notices, n => n.Contains("video encoder", StringComparison.Ordinal));
        Assert.False(ScreenRecorder.IsRecording); // a new recording may start
        Assert.False(rec.ThreadsExited.IsCompleted);
        rec.Dispose();                           // must not release what the stalled thread uses

        gate.Set();
        await rec.ThreadsExited.WaitAsync(TimeSpan.FromSeconds(15));
        Mp4Structure? recovered = Mp4Inspector.Inspect(path) is { IsComplete: true } done ? done : Mp4Inspector.Recover(path);
        Assert.NotNull(recovered);
        Assert.True(RecordingRecovery.Probe(path).VideoSamples >= 60);
    }

    [Fact]
    public void Disk_full_while_writing_ends_the_recording_and_keeps_the_part_already_written()
    {
        string path = Path.Combine(_dir, "disk-full.mp4");
        var hooks = new RecorderHooks
        {
            BeforeVideoWrite = written =>
            {
                if (written >= 100)
                {
                    throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
                }
            },
        };
        using ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(320, 240, 30, 0), [], Options, path, hooks);
        using var stoppedBySelf = new ManualResetEventSlim();
        rec.StoppedBySelf += (_, _) => stoppedBySelf.Set();
        rec.Start();
        Assert.True(stoppedBySelf.Wait(TimeSpan.FromSeconds(15)), "the recorder did not stop by itself");
        RecordingResult r = rec.Stop();
        Assert.False(r.Finalized);
        Assert.False(r.Stalled);
        Assert.Contains(r.Notices, n => n.Contains("disk is full", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(r.EndReason);
        Assert.True(File.Exists(path));
        Assert.NotNull(Mp4Inspector.Recover(path)); // what the crash recovery does at the next start
        Assert.True(RecordingRecovery.Probe(path).VideoSamples >= 30);
    }

    [Fact]
    public void Stop_before_start_discards_cleanly()
    {
        string path = Path.Combine(_dir, "never-started.mp4");
        using ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(64, 64, 30, 0), [], Options, path);
        RecordingResult r = rec.Stop();
        Assert.True(r.Cancelled);
        Assert.False(File.Exists(path));
        Assert.True(rec.ThreadsExited.IsCompleted);
        Assert.Throws<InvalidOperationException>(rec.Start);
    }

    [Theory]
    [InlineData(320, 240)]
    [InlineData(640, 352)]
    [InlineData(640, 360)]
    [InlineData(1280, 720)]
    [InlineData(1000, 562)]
    public void Recording_of_any_even_size_decodes_to_what_was_written_without_timestamp_gaps(int width, int height)
    {
        // Isolates the encoder path (no camera): what is decoded at t = 0 must be the first captured frame, and the
        // file's timestamps must be as regular as those handed to the writer (CI runs 96–104 diagnostics).
        string path = Path.Combine(_dir, $"size-{width}x{height}.mp4");
        var writes = new System.Collections.Concurrent.ConcurrentQueue<(long Time, bool Repeat)>();
        var restarts = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var hooks = new RecorderHooks { AfterVideoWrite = (t, rep) => writes.Enqueue((t, rep)), EncoderRestarted = restarts.Enqueue };
        RecordingResult r;
        using (ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(width, height, 30, 0), [], Options, path, hooks))
        {
            rec.Start();
            Thread.Sleep(1500);
            r = rec.Stop();
        }

        Assert.True(r.Finalized);
        Core.Imaging.PixelBuffer d0 = VideoAnalysis.DecodeFrame(path, 0) ?? throw new InvalidOperationException("no frame");
        var probes = new[] { (width / 4, height / 4), (width / 2, height / 2), (3 * width / 4, 3 * height / 4), (width / 8, 7 * height / 8) };
        string diag = $"{width}x{height}: decoded {d0.Width}x{d0.Height}; " + string.Join("; ", probes.Select(pt => $"({pt.Item1},{pt.Item2}) written {Px(r.FirstFrame!, pt.Item1, pt.Item2)} decoded {Px(d0, pt.Item1, pt.Item2)}")) +
            $"; {Timestamps(path)}; {WriteDiag(writes, restarts)}; encoder {r.Encoder} hw {r.HardwareEncoder} fallback {r.SoftwareFallback}";
        foreach ((int x, int y) in probes)
        {
            int o1 = ((y * width) + x) * 4, o2 = ((y * d0.Width) + x) * 4;
            for (int c = 0; c < 3; c++)
            {
                Assert.True(Math.Abs(r.FirstFrame!.Pixels[o1 + c] - d0.Pixels[o2 + c]) < 60, diag);
            }
        }

        // Timestamps. The recording file is fragmented MP4 written by Media Foundation without fragment start times
        // (tfdt); the Windows reader mis-times its later fragments (CI run 106: 134 / 234 ms "gaps" at a fragment
        // boundary although the writer got 33 ms steps). What is stored must be exact, and the file the app keeps —
        // converted to progressive MP4 when the recording is completed — must decode with regular timestamps.
        Mp4TimingReport stored = Mp4Timing.Read(path);
        uint video = stored.Tracks.First(t => t.Value.Handler == "vide").Key;
        uint timescale = stored.Tracks[video].Timescale;
        int storedSamples = stored.Fragments.Where(f => f.TrackId == video).Sum(f => f.Samples);
        long storedMs = stored.Fragments.Where(f => f.TrackId == video).Sum(f => f.TotalDuration) * 1000 / timescale;
        string final = Path.ChangeExtension(path, ".final.mp4");
        Mp4Defragmenter.Convert(path, final);
        diag += $"; stored: {storedSamples} samples, {storedMs} ms; fragmented as decoded: {Timestamps(path)}; final: {Timestamps(final)}";
        Assert.Equal(writes.Count, storedSamples);
        Assert.True(Math.Abs(storedMs - (writes.Count * 1000L / 30)) <= 1, diag);
        Assert.DoesNotContain("max gap 2", Timestamps(final), StringComparison.Ordinal); // no 200+ ms hole in the file
        Assert.True(Regex.IsMatch(Timestamps(final), @"max gap (3[0-9]|4[0-9]) ms"), diag);
        Core.Imaging.PixelBuffer f0 = VideoAnalysis.DecodeFrame(final, 0) ?? throw new InvalidOperationException("no frame in the final file");
        foreach ((int x, int y) in probes)
        {
            int o1 = ((y * width) + x) * 4, o2 = ((y * f0.Width) + x) * 4;
            for (int c = 0; c < 3; c++)
            {
                Assert.True(Math.Abs(r.FirstFrame!.Pixels[o1 + c] - f0.Pixels[o2 + c]) < 60, diag);
            }
        }
    }

    [Fact]
    public void Screen_plus_synthetic_camera_recording_shows_the_overlay_in_its_corner()
    {
        string path = Path.Combine(_dir, "screen-camera.mp4");
        var camera = new SyntheticCameraSource(320, 180, 30);
        camera.Start();
        var composite = new CompositeFrameSource(new SyntheticFrameSource(640, 360, 30, 0), camera, CameraOverlayShape.Circle, CameraOverlaySize.Medium, CameraCorner.TopLeft, mirror: true);
        RecordingResult r;
        var writes = new System.Collections.Concurrent.ConcurrentQueue<(long Time, bool Repeat)>();
        var restarts = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var hooks = new RecorderHooks { AfterVideoWrite = (t, rep) => writes.Enqueue((t, rep)), EncoderRestarted = restarts.Enqueue };
        using (ScreenRecorder rec = ScreenRecorder.Create(composite, [], Options, path, hooks))
        {
            rec.Start();
            Thread.Sleep(2500);
            r = rec.Stop();
        }

        Assert.True(r.Finalized);
        Core.Imaging.PixelBuffer frame = VideoAnalysis.DecodeFrame(path, 1200) ?? throw new InvalidOperationException("no frame");
        OverlayLayout l = composite.Layout;
        double inside = SyntheticShare(frame, l.X + (l.Width / 4), l.Y + (l.Height / 4), l.Width / 2, l.Height / 2);
        double opposite = SyntheticShare(frame, 640 - l.X - l.Width, 360 - l.Y - l.Height, l.Width, l.Height);
        // Diagnostics for CI (runs 96, 98): where the overlay really is and what colours were decoded there.
        string diag = $"layout {l.X},{l.Y} {l.Width}x{l.Height}; frame {frame.Width}x{frame.Height}; share inside {inside:0.00}, " +
            $"TL {SyntheticShare(frame, 0, 0, l.Width, l.Height):0.00} TR {SyntheticShare(frame, frame.Width - l.Width, 0, l.Width, l.Height):0.00} " +
            $"BL {SyntheticShare(frame, 0, frame.Height - l.Height, l.Width, l.Height):0.00} BR {SyntheticShare(frame, frame.Width - l.Width, frame.Height - l.Height, l.Width, l.Height):0.00}; " +
            $"centre pixel BGRA {Px(frame, l.X + (l.Width / 2), l.Y + (l.Height / 2))}; written {r.FramesWritten}, captured {r.FramesCaptured}, repeated {r.FramesRepeated}, dropped {r.FramesDropped}; " +
            $"overlay frames {composite.FramesWithOverlay}, camera running {camera.IsRunning}, camera sequence {camera.FrameSequence}, lost {camera.LostReason}; first-frame centre {(r.FirstFrame is { } ff ? Px(ff, l.X + (l.Width / 2), l.Y + (l.Height / 2)) : "none")}; decoded@0 centre {(VideoAnalysis.DecodeFrame(path, 0) is { } d0 ? Px(d0, l.X + (l.Width / 2), l.Y + (l.Height / 2)) : "none")}; {Timestamps(path)}; {WriteDiag(writes, restarts)}";
        Assert.True(inside > 0.9, diag);
        Assert.True(opposite < 0.05, diag);
    }

    [Fact]
    public void Camera_only_recording_has_the_camera_size_and_duration()
    {
        string path = Path.Combine(_dir, "camera-only.mp4");
        var camera = new SyntheticCameraSource(640, 360, 30);
        camera.Start();
        RecordingResult r;
        var writes = new System.Collections.Concurrent.ConcurrentQueue<(long Time, bool Repeat)>();
        var restarts = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var hooks = new RecorderHooks { AfterVideoWrite = (t, rep) => writes.Enqueue((t, rep)), EncoderRestarted = restarts.Enqueue };
        using (ScreenRecorder rec = ScreenRecorder.Create(new CameraFrameSource(camera, mirror: false), [], Options, path, hooks))
        {
            rec.Start();
            Thread.Sleep(3000);
            r = rec.Stop();
        }

        Assert.True(r.Finalized);
        (long durationMs, int w, int h, bool _, int samples) = RecordingRecovery.Probe(path);
        Assert.Equal(640, w);
        Assert.Equal(360, h);
        string diag = $"probe {durationMs} ms / {samples} samples; result {r.DurationMs} ms, written {r.FramesWritten}, captured {r.FramesCaptured}, repeated {r.FramesRepeated}, dropped {r.FramesDropped}, end {r.EndReason}; {Timestamps(path)}; {WriteDiag(writes, restarts)}";
        Assert.True(durationMs is >= 2700 and <= 3300, diag);
        Assert.True(Math.Abs(samples - (durationMs * 30 / 1000)) <= 2, diag);
        Assert.False(camera.IsRunning); // the recorder released the camera
    }

    [Fact]
    public unsafe void Decoded_frames_are_read_top_row_first()
    {
        // Regression (CI run 96): the decoder's RGB32 can be bottom-up; frames were read upside down.
        string path = Path.Combine(_dir, "orientation.mp4");
        uint[] px = new uint[64 * 64];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = i < 32 * 64 ? 0xFFFF0000u : 0xFF0000FFu; // top red, bottom blue
        }

        using (var writer = new Mp4Writer(path, new VideoStreamFormat(64, 64, 30, 1_000_000), withAudio: false, fragmented: false, EncoderInput.SystemMemory, 0))
        {
            fixed (uint* p = px)
            {
                for (int f = 0; f < 30; f++)
                {
                    writer.WriteFrame((byte*)p, 64 * 4, f * 333_333L, 333_333L);
                }
            }

            writer.Complete();
        }

        Core.Imaging.PixelBuffer frame = VideoAnalysis.DecodeFrame(path, 500) ?? throw new InvalidOperationException("no frame");
        int top = ((8 * frame.Width) + 32) * 4, bottom = ((56 * frame.Width) + 32) * 4;
        Assert.True(frame.Pixels[top + 2] > 180 && frame.Pixels[top] < 80, "top row is not red");
        Assert.True(frame.Pixels[bottom] > 180 && frame.Pixels[bottom + 2] < 80, "bottom row is not blue");
    }

    [Theory]
    [InlineData(640, 360)]
    [InlineData(1000, 562)]
    public unsafe void Encoder_keeps_every_row_for_heights_that_are_not_a_multiple_of_16(int width, int height)
    {
        // Decodes to NV12 with no video processing and reads the Y plane directly: tells the encoder side (rows
        // stored correctly) apart from the RGB32 read-back (CI run 106: sheared rows at 640×360 / 1000×562).
        string path = Path.Combine(_dir, $"rows-{width}x{height}.mp4");
        uint[] px = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            uint v = (uint)(y * 255 / (height - 1));
            Array.Fill(px, 0xFF000000u | (v << 16) | (v << 8) | v, y * width, width);
        }

        using (var writer = new Mp4Writer(path, new VideoStreamFormat(width, height, 30, 4_000_000), withAudio: false, fragmented: false, EncoderInput.SystemMemory, 0))
        {
            fixed (uint* p = px)
            {
                for (int f = 0; f < 15; f++)
                {
                    writer.WriteFrame((byte*)p, width * 4, f * 333_333L, 333_333L);
                }
            }

            writer.Complete();
        }

        nint reader = Mf.CreateSourceReader(path, 0);
        try
        {
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
            nint t = Mf.CreateMediaType();
            Mf.SetGuid(t, Mf.MT_MAJOR_TYPE, Mf.MediaType_Video);
            Mf.SetGuid(t, Mf.MT_SUBTYPE, Mf.VideoFormat_NV12);
            Mf.SetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, t);
            Com.Release(ref t);
            nint current = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            ulong size = Mf.GetUInt64(current, Mf.MT_FRAME_SIZE) ?? 0;
            Com.Release(ref current);
            int codedHeight = (int)(size & 0xFFFFFFFF);
            Assert.True(Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out _, out nint sample) && sample != 0);
            nint buffer = Mf.ConvertToContiguousBuffer(sample);
            try
            {
                byte* y0 = Mf.LockRows(buffer, width, codedHeight, out int pitch, out bool locked2D);
                try
                {
                    string diag = $"coded {(int)(size >> 32)}x{codedHeight}, pitch {pitch}, 2D {locked2D}";
                    foreach (int row in new[] { 4, height / 4, height / 2, 3 * height / 4, height - 5 })
                    {
                        int expected = 16 + ((row * 255 / (height - 1)) * 219 / 255);
                        int actual = y0[((long)row * pitch) + (width / 2)];
                        Assert.True(Math.Abs(actual - expected) <= 12, $"row {row}: Y {actual}, expected {expected}; {diag}");
                    }
                }
                finally
                {
                    Mf.UnlockRows(buffer, locked2D);
                }
            }
            finally
            {
                Com.Release(ref buffer);
                Com.Release(ref sample);
            }
        }
        finally
        {
            Com.Release(ref reader);
        }

        // And the RGB32 read-back used everywhere else now matches too.
        Core.Imaging.PixelBuffer frame = VideoAnalysis.DecodeFrame(path, 0) ?? throw new InvalidOperationException("no frame");
        Assert.Equal(height, frame.Height);
        foreach (int row in new[] { 4, height / 2, height - 5 })
        {
            int expected = row * 255 / (height - 1);
            int actual = frame.Pixels[(((row * frame.Width) + (width / 2)) * 4) + 1];
            Assert.True(Math.Abs(actual - expected) <= 16, $"RGB row {row}: G {actual}, expected {expected}");
        }
    }

    [Fact]
    public void Late_first_frame_still_gives_a_constant_frame_rate_from_zero()
    {
        // Regression (CI run 96): a first frame placed after slot 0 left a leading gap (fewer samples than duration × FPS).
        string path = Path.Combine(_dir, "late-start.mp4");
        int iteration = 0;
        var hooks = new RecorderHooks { CaptureIteration = () => Thread.Sleep(Interlocked.Increment(ref iteration) == 1 ? 300 : 0) };
        var camera = new SyntheticCameraSource(320, 180, 30);
        camera.Start();
        using (ScreenRecorder rec = ScreenRecorder.Create(new CameraFrameSource(camera, mirror: false), [], Options, path, hooks))
        {
            rec.Start();
            Thread.Sleep(2000);
            Assert.True(rec.Stop().Finalized);
        }

        (long durationMs, int _, int _, bool _, int samples) = RecordingRecovery.Probe(path);
        Assert.InRange(samples, (int)(durationMs * 30 / 1000) - 2, (int)(durationMs * 30 / 1000) + 2);
        nint reader = VideoAnalysis.OpenDecoding(path, video: true, audio: false);
        try
        {
            Assert.True(Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long first, out nint sample) && sample != 0);
            Com.Release(ref sample);
            Assert.InRange(first / 10_000.0, 0, 1);
        }
        finally
        {
            Com.Release(ref reader);
        }
    }

    /// <summary>Share of pixels in a rectangle that look like the SYNTHETIC camera (full red: magenta or yellow).</summary>
    /// <summary>What the recorder handed to the writer: count, first times, largest gap and encoder restarts.</summary>
    private static string WriteDiag(System.Collections.Concurrent.ConcurrentQueue<(long Time, bool Repeat)> writes, System.Collections.Concurrent.ConcurrentQueue<long> restarts)
    {
        var t = writes.Select(w => w.Time / 10_000).ToList();
        long maxGap = t.Zip(t.Skip(1), (a, b) => b - a).DefaultIfEmpty(0).Max();
        int at = t.Zip(t.Skip(1), (a, b) => b - a).ToList().FindIndex(g => g == maxGap);
        return $"writer got {t.Count} frames ({writes.Count(w => w.Repeat)} repeats): first {string.Join(",", t.Take(4))}, max gap {maxGap} ms after {(at >= 0 ? t[at] : -1)} ms; encoder restarts at {string.Join(",", restarts.Select(x => x / 10_000))}";
    }

    /// <summary>First and last video sample presentation times (ms) and the largest gap, for CI diagnostics.</summary>
    private static string Timestamps(string path)
    {
        var times = new List<long>();
        nint reader = Mf.CreateSourceReader(path, 0);
        try
        {
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                times.Add(ts / 10_000);
                Com.Release(ref sample);
            }
        }
        finally
        {
            Com.Release(ref reader);
        }

        long maxGap = times.Zip(times.Skip(1), (a, b) => b - a).DefaultIfEmpty(0).Max();
        return $"samples {times.Count}: first {string.Join(",", times.Take(4))} … last {string.Join(",", times.TakeLast(3))}; max gap {maxGap} ms";
    }

    private static string Px(Core.Imaging.PixelBuffer f, int x, int y)
    {
        int o = ((Math.Clamp(y, 0, f.Height - 1) * f.Width) + Math.Clamp(x, 0, f.Width - 1)) * 4;
        return $"{f.Pixels[o]},{f.Pixels[o + 1]},{f.Pixels[o + 2]},{f.Pixels[o + 3]}";
    }

    internal static double SyntheticShare(Core.Imaging.PixelBuffer frame, int x0, int y0, int w, int h)
    {
        int total = 0, hits = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(frame.Height, y0 + h); y += 2)
        {
            for (int x = Math.Max(0, x0); x < Math.Min(frame.Width, x0 + w); x += 2)
            {
                int o = ((y * frame.Width) + x) * 4;
                total++;
                if (frame.Pixels[o + 2] > 180 && (frame.Pixels[o] > 150 || frame.Pixels[o + 1] > 150))
                {
                    hits++;
                }
            }
        }

        return total == 0 ? 0 : hits / (double)total;
    }
}
