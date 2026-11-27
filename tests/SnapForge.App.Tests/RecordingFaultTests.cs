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

    [Fact]
    public void Screen_plus_synthetic_camera_recording_shows_the_overlay_in_its_corner()
    {
        string path = Path.Combine(_dir, "screen-camera.mp4");
        var camera = new SyntheticCameraSource(320, 180, 30);
        camera.Start();
        var composite = new CompositeFrameSource(new SyntheticFrameSource(640, 360, 30, 0), camera, CameraOverlayShape.Circle, CameraOverlaySize.Medium, CameraCorner.TopLeft, mirror: true);
        RecordingResult r;
        using (ScreenRecorder rec = ScreenRecorder.Create(composite, [], Options, path))
        {
            rec.Start();
            Thread.Sleep(2500);
            r = rec.Stop();
        }

        Assert.True(r.Finalized);
        Core.Imaging.PixelBuffer frame = VideoAnalysis.DecodeFrame(path, 1200) ?? throw new InvalidOperationException("no frame");
        OverlayLayout l = composite.Layout;
        Assert.True(SyntheticShare(frame, l.X + (l.Width / 4), l.Y + (l.Height / 4), l.Width / 2, l.Height / 2) > 0.9);
        Assert.True(SyntheticShare(frame, 640 - l.X - l.Width, 360 - l.Y - l.Height, l.Width, l.Height) < 0.05);
    }

    [Fact]
    public void Camera_only_recording_has_the_camera_size_and_duration()
    {
        string path = Path.Combine(_dir, "camera-only.mp4");
        var camera = new SyntheticCameraSource(640, 360, 30);
        camera.Start();
        RecordingResult r;
        using (ScreenRecorder rec = ScreenRecorder.Create(new CameraFrameSource(camera, mirror: false), [], Options, path))
        {
            rec.Start();
            Thread.Sleep(3000);
            r = rec.Stop();
        }

        Assert.True(r.Finalized);
        (long durationMs, int w, int h, bool _, int samples) = RecordingRecovery.Probe(path);
        Assert.Equal(640, w);
        Assert.Equal(360, h);
        Assert.InRange(durationMs, 2700, 3300);
        Assert.InRange(samples, (int)(durationMs * 30 / 1000) - 2, (int)(durationMs * 30 / 1000) + 2);
        Assert.False(camera.IsRunning); // the recorder released the camera
    }

    /// <summary>Share of pixels in a rectangle that look like the SYNTHETIC camera (full red: magenta or yellow).</summary>
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
