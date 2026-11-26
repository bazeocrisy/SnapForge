using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Video;

/// <summary>
/// Automated recording checks run by CI on a real Windows desktop (no audio hardware there):
/// <list type="bullet">
/// <item><c>--video-selftest &lt;dir&gt;</c> — sync, pause/resume, 60 FPS, real screen with absent audio devices,
/// single-session refusal and cancel; writes report.json and keeps sample MP4s.</item>
/// <item><c>--video-record-until-killed &lt;libraryRoot&gt; &lt;readyFile&gt;</c> — starts a real-screen recording
/// into a library and never stops (CI kills the process to simulate a crash or power loss).</item>
/// <item><c>--video-recover &lt;libraryRoot&gt; &lt;report.json&gt;</c> — runs the start-up recovery and reports.</item>
/// <item><c>--video-benchmark &lt;dir&gt; &lt;w&gt; &lt;h&gt; &lt;fps&gt; &lt;seconds&gt; &lt;screen|synthetic&gt;</c>.</item>
/// </list>
/// </summary>
internal static partial class VideoSelfTest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(long point, uint flags);

    private static RecorderOptions Options(int fps, bool audio = true) =>
        new(fps, VideoQuality.Standard, RecordSystemAudio: audio, RecordMicrophone: false, null, 100L * 1024 * 1024, 1);

    public static int Run(string[] args)
    {
        _ = Mf.MFStartup(Mf.MF_VERSION, 0);
        _ = Com.CoInitializeEx(0, Com.COINIT_MULTITHREADED);
        return args[0] switch
        {
            "--video-selftest" => SelfTest(args[1]),
            "--video-record-until-killed" => RecordUntilKilled(args[1], args[2]),
            "--video-recover" => Recover(args[1], args[2]),
            "--video-benchmark" => Benchmark(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[6]),
            _ => 2,
        };
    }

    private static int SelfTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var report = new Dictionary<string, object?>();
        var failures = new List<string>();

        void Check(string name, bool ok, string detail)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: {detail}");
            if (!ok)
            {
                failures.Add(name);
            }
        }

        // 1. A/V sync with synthetic flashes and beeps (12 s, 30 FPS).
        try
        {
            string path = Path.Combine(dir, "sample-sync-640x360-30fps.mp4");
            (RecordingResult r, SyncReport sync) = RecordSynthetic(path, 640, 360, 30, TimeSpan.FromSeconds(12), pauseAt: null);
            report["sync"] = new { result = Summary(r), sync = SyncSummary(sync) };
            Check("av-sync", sync.OffsetsMs.Count >= 10 && sync.MaxAbsOffsetMs <= 45, $"{sync.OffsetsMs.Count} flash/beep pairs, mean {sync.MeanOffsetMs} ms, max |offset| {sync.MaxAbsOffsetMs} ms, drift {sync.DriftMsPerMinute} ms/min");
            Check("constant-frame-rate", Math.Abs(sync.VideoFrames - (r.DurationMs * 30 / 1000.0)) <= 2, $"{sync.VideoFrames} frames for {r.DurationMs} ms");
            Check("fragmented-mp4", Mp4Inspector.Inspect(path) is { IsComplete: true, IsFragmented: true } s1 && s1.CompleteFragments >= 5, Mp4Inspector.Inspect(path).CompleteFragments + " fragments");
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["sync"] = new { error = ex.ToString() };
            Check("av-sync", false, ex.Message);
        }

        // 2. Pause / resume: 4 s, pause 3 s, 4 s → about 8 s of video, still in sync.
        try
        {
            string path = Path.Combine(dir, "pause-resume.mp4");
            (RecordingResult r, SyncReport sync) = RecordSynthetic(path, 640, 360, 30, TimeSpan.FromSeconds(11), pauseAt: (TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3)));
            report["pauseResume"] = new { result = Summary(r), sync = SyncSummary(sync) };
            Check("pause-resume-duration", Math.Abs(r.DurationMs - 8000) <= 200, $"{r.DurationMs} ms recorded of 11 s with a 3 s pause");
            Check("pause-resume-sync", sync.OffsetsMs.Count >= 6 && sync.MaxAbsOffsetMs <= 45, $"max |offset| {sync.MaxAbsOffsetMs} ms over {sync.OffsetsMs.Count} pairs");
            File.Delete(path);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["pauseResume"] = new { error = ex.ToString() };
            Check("pause-resume", false, ex.Message);
        }

        // 3. 60 FPS synthetic 1280×720.
        try
        {
            string path = Path.Combine(dir, "synthetic-1280x720-60fps.mp4");
            (RecordingResult r, SyncReport sync) = RecordSynthetic(path, 1280, 720, 60, TimeSpan.FromSeconds(5), pauseAt: null);
            report["fps60"] = new { result = Summary(r), sync = SyncSummary(sync) };
            Check("60fps", sync.VideoFrames >= 280 && r.FramesDropped < 30, $"{sync.VideoFrames} frames, {r.FramesDropped} dropped, encoder {r.Encoder}");
            File.Delete(path);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["fps60"] = new { error = ex.ToString() };
            Check("60fps", false, ex.Message);
        }

        // 4. Real screen (primary display) for 5 s with system audio requested — on a PC without audio devices the
        //    recording continues with a silent track.
        try
        {
            string path = Path.Combine(dir, "sample-screen-30fps.mp4");
            var video = new ScreenFrameSource(MonitorFromPoint(0, 1), isWindow: false, crop: null, captureCursor: true);
            IAudioSource[] audio = [new WasapiSource(ScreenRecorder.AudioNames.System, loopback: true, null), new WasapiSource(ScreenRecorder.AudioNames.Microphone, loopback: false, null)];
            using ScreenRecorder rec = ScreenRecorder.Create(video, audio, Options(30), path);

            // 5. A second recording cannot start while one is running (VID-R14).
            bool refused;
            try
            {
                using var second = ScreenRecorder.Create(new SyntheticFrameSource(64, 64, 30, 0), [], Options(30, audio: false), Path.Combine(dir, "second.mp4"));
                refused = false;
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }

            Check("single-session", refused, refused ? "second recording refused while one is running" : "a second recording started");

            rec.Start();
            ScreenActivity(TimeSpan.FromSeconds(5));
            RecordingResult r = rec.Stop();
            (long durationMs, int w, int h, bool hasAudio, int samples) = RecordingRecovery.Probe(path);
            report["screen"] = new { result = Summary(r), probe = new { durationMs, w, h, hasAudio, samples }, source = video.Description };
            Check("screen-recording", r.Finalized && samples >= 140 && hasAudio, $"{w}x{h}, {samples} frames ({r.FramesCaptured} captured, {r.FramesRepeated} repeated), {durationMs} ms, audio track {hasAudio}, devices: {string.Join("; ", r.Notices.DefaultIfEmpty("none reported"))}");
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["screen"] = new { error = ex.ToString() };
            Check("screen-recording", false, ex.Message);
        }

        // 6. Cancel discards the file.
        try
        {
            string path = Path.Combine(dir, "cancelled.mp4");
            using ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(320, 240, 30, 0), [], Options(30, audio: false), path);
            rec.Start();
            Thread.Sleep(1500);
            rec.Cancel();
            Check("cancel-discards", !File.Exists(path) && !ScreenRecorder.IsRecording, "file removed, recorder released");
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Check("cancel-discards", false, ex.Message);
        }

        report["failures"] = failures;
        File.WriteAllText(Path.Combine(dir, "report.json"), JsonSerializer.Serialize(report, Json));
        Console.WriteLine(failures.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + string.Join(", ", failures) + ")");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Records the synthetic flash/beep sources, optionally pausing, then measures sync from the file.</summary>
    internal static (RecordingResult Result, SyncReport Sync) RecordSynthetic(string path, int w, int h, int fps, TimeSpan length, (TimeSpan At, TimeSpan For)? pauseAt)
    {
        const long flashEvery = 10_000_000; // 1 s
        var video = new SyntheticFrameSource(w, h, fps, flashEvery);
        var audio = new SyntheticAudioSource(() => video.StartTime, flashEvery);
        RecordingResult result;
        using (ScreenRecorder rec = ScreenRecorder.Create(video, [audio], Options(fps) with { RecordSystemAudio = true }, path))
        {
            // The synthetic source is named differently from the system/mic names: enable it explicitly.
            rec.Start();
            audio.Enabled = true;
            var sw = Stopwatch.StartNew();
            if (pauseAt is { } p)
            {
                Thread.Sleep(p.At);
                rec.Pause();
                Thread.Sleep(p.For);
                rec.Resume();
            }

            Thread.Sleep(length - sw.Elapsed);
            result = rec.Stop();
        }

        return (result, VideoAnalysis.MeasureSync(path, 1000));
    }

    private static int RecordUntilKilled(string libraryRoot, string readyFile)
    {
        CaptureLibrary library = CaptureLibrary.Open(libraryRoot);
        (Guid _, string path) = library.BeginVideo(DateTime.UtcNow);
        IFrameSource video = new ScreenFrameSource(MonitorFromPoint(0, 1), isWindow: false, crop: null, captureCursor: true);
        ScreenRecorder rec = ScreenRecorder.Create(video, [new WasapiSource(ScreenRecorder.AudioNames.System, loopback: true, null)], Options(30), path);
        rec.Start();
        File.WriteAllText(readyFile, path);
        Console.WriteLine("RECORDING " + path);
        ScreenActivity(TimeSpan.FromHours(1)); // killed by CI long before this ends
        rec.Dispose();
        return 0;
    }

    private static int Recover(string libraryRoot, string reportPath)
    {
        CaptureLibrary library = CaptureLibrary.Open(libraryRoot);
        IReadOnlyList<InterruptedRecording> interrupted = library.InterruptedRecordings();
        IReadOnlyList<RecoveryOutcome> outcomes = RecordingRecovery.RecoverAll(library);
        CaptureLibrary reopened = CaptureLibrary.Open(libraryRoot);
        var report = new
        {
            interrupted = interrupted.Count,
            outcomes,
            listed = reopened.Records.Select(r => new { r.Id, r.Title, r.IsVideo, r.Recovered, r.DurationMs, r.Width, r.Height, r.Damage }),
        };
        string json = JsonSerializer.Serialize(report, Json);
        File.WriteAllText(reportPath, json);
        Console.WriteLine(json);
        bool ok = interrupted.Count == 1 && outcomes.Count == 1 && outcomes[0].Recovered && outcomes[0].DurationMs >= 2000
            && reopened.Records.Count == 1 && reopened.Records[0].Recovered && reopened.Records[0].Damage is null;
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 1;
    }

    private static int Benchmark(string dir, int w, int h, int fps, int seconds, string source)
    {
        Directory.CreateDirectory(dir);
        string name = $"{w}x{h}-{fps}fps-{seconds}s-{source}";
        string path = Path.Combine(dir, name + ".mp4");
        object row;
        try
        {
            IFrameSource video = source == "screen"
                ? new ScreenFrameSource(MonitorFromPoint(0, 1), isWindow: false, crop: (0, 0, w, h), captureCursor: true)
                : new SyntheticFrameSource(w, h, fps, 10_000_000);
            if (video.Width != w || video.Height != h)
            {
                row = new { name, status = "unsupported", reason = $"the display offers {video.Width}x{video.Height}, not {w}x{h}" };
                video.Dispose();
            }
            else
            {
                IAudioSource audio = source == "screen"
                    ? new WasapiSource(ScreenRecorder.AudioNames.System, loopback: true, null)
                    : new SyntheticAudioSource(() => ((SyntheticFrameSource)video).StartTime, 10_000_000);
                using ScreenRecorder rec = ScreenRecorder.Create(video, [audio], Options(fps), path);
                rec.Start();
                audio.Enabled = true;
                if (source == "screen")
                {
                    ScreenActivity(TimeSpan.FromSeconds(seconds));
                }
                else
                {
                    Thread.Sleep(TimeSpan.FromSeconds(seconds));
                }

                RecordingResult r = rec.Stop();
                object? sync = source == "synthetic" ? SyncSummary(VideoAnalysis.MeasureSync(path, 1000)) : null;
                Mp4Structure s = Mp4Inspector.Inspect(path);
                row = new { name, status = "measured", result = Summary(r), sync, valid = s.IsComplete, fragments = s.CompleteFragments, mbPerMinute = Math.Round(r.Bytes / 1048576.0 / (r.DurationMs / 60000.0), 1) };
                File.Delete(path);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            row = new { name, status = "failed", reason = ex.Message };
        }

        string json = JsonSerializer.Serialize(row, Json);
        File.WriteAllText(Path.Combine(dir, name + ".json"), json);
        Console.WriteLine(json);
        return 0;
    }

    /// <summary>Changes what is on screen (a moving window) so the screen source produces new frames.</summary>
    private static void ScreenActivity(TimeSpan duration)
    {
        var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var window = new System.Windows.Window
            {
                Width = 300,
                Height = 200,
                WindowStyle = System.Windows.WindowStyle.None,
                ShowInTaskbar = false,
                Topmost = true,
                Left = 0,
                Top = 0,
                Title = "SnapForge recording test",
            };
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            var sw = Stopwatch.StartNew();
            timer.Tick += (_, _) =>
            {
                double t = sw.Elapsed.TotalSeconds;
                window.Left = 50 + (Math.Sin(t) * 40) + 40;
                window.Top = 50 + (Math.Cos(t) * 30) + 30;
                byte c = (byte)((t * 80) % 255);
                window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c, (byte)(255 - c), 128));
                if (sw.Elapsed >= duration)
                {
                    timer.Stop();
                    window.Close();
                }
            };
            window.Closed += (_, _) =>
            {
                done.Set();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            };
            window.Show();
            timer.Start();
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        done.Wait();
    }

    private static object Summary(RecordingResult r) => new
    {
        r.Width,
        r.Height,
        r.FramesPerSecond,
        r.DurationMs,
        r.FramesWritten,
        r.FramesCaptured,
        r.FramesRepeated,
        r.FramesDropped,
        r.Encoder,
        r.HardwareEncoder,
        r.SoftwareFallback,
        r.CpuPercent,
        r.PeakWorkingSetMb,
        r.PeakPrivateMb,
        r.MeanEncodeLatencyMs,
        r.MaxEncodeLatencyMs,
        r.Bytes,
        r.Finalized,
        r.Notices,
        audio = r.Audio,
    };

    private static object SyncSummary(SyncReport s) => new
    {
        pairs = s.OffsetsMs.Count,
        s.MeanOffsetMs,
        s.MaxAbsOffsetMs,
        s.DriftMsPerMinute,
        s.VideoFrames,
        s.VideoDurationMs,
        s.AudioDurationMs,
        flashes = s.FlashesMs.Count,
        beeps = s.BeepsMs.Count,
        offsets = s.OffsetsMs.Select(o => Math.Round(o, 1)),
    };
}
