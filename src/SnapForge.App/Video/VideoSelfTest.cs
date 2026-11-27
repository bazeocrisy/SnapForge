using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Geometry;
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

#pragma warning disable SYSLIB1054 // DEVMODEW has inline strings; the classic marshaller handles them.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string? device, int mode, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsW(ref DevMode devMode, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsW(nint devMode, uint flags);

    /// <summary>Returns the display to its saved (registry) mode.</summary>
    internal static void RestoreDisplayMode() => _ = ChangeDisplaySettingsW(0, 0);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    /// <summary>
    /// Benchmarks only: temporarily switches the primary display to w×h if the display offers that mode
    /// (CDS_FULLSCREEN = not saved, restored by Windows when the process ends). Returns false if unsupported.
    /// </summary>
    internal static bool TrySetDisplayMode(int w, int h)
    {
        var dm = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
        for (int i = 0; EnumDisplaySettingsW(null, i, ref dm); i++)
        {
            if (dm.PelsWidth == w && dm.PelsHeight == h)
            {
                dm.Fields = 0x00080000 | 0x00100000; // DM_PELSWIDTH | DM_PELSHEIGHT
                return ChangeDisplaySettingsW(ref dm, 0x4 /* CDS_FULLSCREEN */) == 0;
            }
        }

        return false;
    }

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
            report["storedTiming"] = StoredTiming(LastFragmentedPath!);
            report["fragmentedFileSync"] = LastFragmentedSync is { } fs ? SyncSummary(fs) : null;
            report["sync"] = new { result = Summary(r), sync = SyncSummary(sync), diagnostics = LastDiagnostics, decodedFlashesMs = sync.FlashesMs.Select(f => Math.Round(f, 1)), decodedBeepsMs = sync.BeepsMs.Select(b => Math.Round(b, 1)), frames = VideoAnalysis.FrameTimeline(path, 0, 75) };
            SyncError e = LastSyncError!;
            report["syncError"] = e;
            Check("av-sync", e.ErrorsMs.Count >= 10 && e.MaxAbsMs <= 20, $"{e.ErrorsMs.Count} flash/beep pairs: A/V error mean {e.MeanMs} ms, max |error| {e.MaxAbsMs} ms, drift {e.DriftMsPerMinute} ms/min (video timestamp error max {e.VideoErrorsMs.Max(Math.Abs)} ms, audio {e.AudioErrorsMs.Max(Math.Abs)} ms)");
            Check("constant-frame-rate", Math.Abs(sync.VideoFrames - (r.DurationMs * 30 / 1000.0)) <= 2, $"{sync.VideoFrames} frames for {r.DurationMs} ms");
            Check("timestamps-exact", sync.NonMonotonicTimestamps == 0 && Math.Abs(sync.FirstVideoMs) < 1, $"first decoded frame at {sync.FirstVideoMs} ms, {sync.NonMonotonicTimestamps} out-of-order timestamps");
            (double minGap, double maxGap) = VideoFrameGapsMs(path);
            Check("timestamps-regular", minGap >= 33.3 / 2 && maxGap <= 33.4 * 1.5, $"decoded frame spacing {minGap:0.0}–{maxGap:0.0} ms in the final file (30 FPS = 33.3 ms; a jump at a fragment boundary would show here)");
            Check("fragmented-recording", Mp4Inspector.Inspect(LastFragmentedPath!) is { IsComplete: true, IsFragmented: true } s1 && s1.CompleteFragments >= 5, Mp4Inspector.Inspect(LastFragmentedPath!).CompleteFragments + " fragments while recording");
            Check("progressive-final", Mp4Inspector.Inspect(path) is { IsComplete: true, IsFragmented: false } s2 && s2.Boxes.Select(b => b.Type).SequenceEqual(["ftyp", "moov", "mdat"]), string.Join(" ", Mp4Inspector.Inspect(path).Boxes.Select(b => b.Type)));
            File.Delete(LastFragmentedPath!);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["sync"] = new { error = ex.ToString() };
            Check("av-sync", false, ex.Message);
        }

        // 2. Pause / resume: 4.5 s, pause 3 s, 3.5 s → about 8 s of video, still in sync. The pause starts and ends
        //    between the once-a-second flash/beep markers: a marker exactly at the resume instant is cut by the pause
        //    in one stream and not the other, depending on sub-frame scheduling (CI runs 113 and 115), which measures
        //    the cut, not the sync.
        try
        {
            string path = Path.Combine(dir, "pause-resume.mp4");
            (RecordingResult r, SyncReport sync) = RecordSynthetic(path, 640, 360, 30, TimeSpan.FromSeconds(11), pauseAt: (TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(3)));
            report["pauseResume"] = new { result = Summary(r), sync = SyncSummary(sync) };
            Check("pause-resume-duration", Math.Abs(r.DurationMs - 8000) <= 200, $"{r.DurationMs} ms recorded of 11 s with a 3 s pause");
            SyncError pe = LastSyncError!;
            report["pauseResumeSyncError"] = pe;
            Check("pause-resume-sync", pe.ErrorsMs.Count >= 6 && pe.MaxAbsMs <= 20, $"A/V error max {pe.MaxAbsMs} ms, mean {pe.MeanMs} ms over {pe.ErrorsMs.Count} pairs (errors: {string.Join(", ", pe.ErrorsMs)})");
            File.Delete(path);
            File.Delete(LastFragmentedPath!);
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
            SyncError fe = LastSyncError!;
            report["fps60SyncError"] = fe;
            Check("60fps-sync", fe.ErrorsMs.Count >= 3 && fe.MaxAbsMs <= 20, $"A/V error max {fe.MaxAbsMs} ms over {fe.ErrorsMs.Count} pairs");
            File.Delete(path);
            File.Delete(LastFragmentedPath!);
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
            string path = Path.Combine(dir, "sample-screen-30fps.fragmented.mp4");
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
            string final = Path.Combine(dir, "sample-screen-30fps.mp4");
            File.Delete(final);
            Mp4Defragmenter.Convert(path, final);
            File.Delete(path);
            path = final;
            (long durationMs, int w, int h, bool hasAudio, int samples) = RecordingRecovery.Probe(path);
            report["screen"] = new { result = Summary(r), probe = new { durationMs, w, h, hasAudio, samples }, source = video.Description };
            Check("screen-constant-frame-rate", Math.Abs(samples - (durationMs * 30 / 1000.0)) <= 2, $"{samples} frames for {durationMs} ms of real screen (expected one per 1/30 s)");
            Check("screen-recording", r.Finalized && samples >= 140 && hasAudio, $"{w}x{h}, {samples} frames ({r.FramesCaptured} captured, {r.FramesRepeated} repeated), {durationMs} ms, audio track {hasAudio}, devices: {string.Join("; ", r.Notices.DefaultIfEmpty("none reported"))}");
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["screen"] = new { error = ex.ToString() };
            Check("screen-recording", false, ex.Message);
        }

        // 7. The toolbar and outline are not in the recording (owner rule), with a control run proving the check works.
        try
        {
            double excluded = ToolbarAbsence(dir, exclude: true);
            string? coveredExcluded = LastToolbarCovered;
            double control = ToolbarAbsence(dir, exclude: false);
            string? coveredControl = LastToolbarCovered;
            report["toolbarExclusion"] = new { greenWhereToolbarIsWithExclusion = excluded, greenWhereToolbarIsWithoutExclusion = control, coveredExcluded, coveredControl };
            string precondition = coveredExcluded is null && coveredControl is null ? string.Empty : $" — PRECONDITION NOT MET: test area covered by {coveredExcluded ?? coveredControl}";
            Check("toolbar-not-recorded", excluded >= 0.98 && control < 0.6, $"toolbar area is {excluded:P1} background in the recording (control without exclusion: {control:P1}){precondition}");
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["toolbarExclusion"] = new { error = ex.ToString() };
            Check("toolbar-not-recorded", false, ex.Message);
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

        // 8. SnapForge Studio and the recording lifecycle (SF-006, FA-021, FA-022). The runner has no camera: the camera
        //    checks use the SYNTHETIC test camera and say so; the real-device count is reported as UNAVAILABLE.
        var unavailable = new List<string>();
        StudioChecks(dir, report, Check, (name, detail) =>
        {
            Console.WriteLine($"UNAVAILABLE {name}: {detail}");
            unavailable.Add(name);
        });
        report["unavailable"] = unavailable;

        report["failures"] = failures;
        File.WriteAllText(Path.Combine(dir, "report.json"), JsonSerializer.Serialize(report, Json));
        Console.WriteLine(failures.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + string.Join(", ", failures) + ")");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Shows a solid green window with the recording toolbar and outline on top, records that area for 2 s through
    /// Windows.Graphics.Capture, decodes a frame and returns the fraction of the toolbar's rectangle that shows the
    /// green background (1.0 = toolbar absent from the recording).
    /// </summary>
    /// <summary>Precondition result of the last <see cref="ToolbarAbsence"/> run (null = only our windows on the area).</summary>
    private static string? LastToolbarCovered { get; set; }

    private static double ToolbarAbsence(string dir, bool exclude)
    {
        PixelRect? toolbarRect = null;
        var ours = new List<nint>();
        PixelRect area = default;
        var ready = new ManualResetEventSlim();
        var close = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var green = new System.Windows.Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.Lime,
                Left = 60,
                Top = 60,
                Width = 640,
                Height = 360,
                Topmost = true,
                ShowInTaskbar = false,
            };
            green.Show();
            ours.Add(new System.Windows.Interop.WindowInteropHelper(green).Handle);
            area = SnapForge.App.Interop.PhysicalPlacement.GetBounds(green)!.Value;
            var outline = new RecordingOutline(new PixelRect(area.X + 40, area.Y + 40, area.Width - 80, area.Height - 80), exclude);
            outline.Show();
            ours.Add(new System.Windows.Interop.WindowInteropHelper(outline).Handle);
            var bar = new RecordingToolbar("640×360 · 30 FPS", true, true, false, "Shift+F10", "Shift+F9", exclude);
            bar.Show();
            ours.Add(new System.Windows.Interop.WindowInteropHelper(bar).Handle);
            SnapForge.App.Interop.PhysicalPlacement.SetBounds(bar, new PixelRect(area.X + 100, area.Y + 150, 0, 0), resize: false);
            bar.Update(TimeSpan.FromSeconds(83), false, 0.5f, 0);
            bar.UpdateLayout();
            toolbarRect = SnapForge.App.Interop.PhysicalPlacement.GetBounds(bar);
            // Ready only once the toolbar has been drawn: the control run once recorded the area before the toolbar's
            // first frame reached the screen (CI run 119: 99.8 % background without exclusion).
            bar.Activate();
            _ = bar.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
            {
                toolbarRect = SnapForge.App.Interop.PhysicalPlacement.GetBounds(bar);
                ready.Set();
            });
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                if (close.IsSet)
                {
                    bar.Close();
                    outline.Close();
                    green.Close();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(10));
        Thread.Sleep(700);
        LastToolbarCovered = ForeignWindowsOver(area, ours);
        string path = Path.Combine(dir, exclude ? "toolbar-excluded.mp4" : "toolbar-control.mp4");
        try
        {
            nint monitor = MonitorFromPoint(PackPoint(area.X + 5, area.Y + 5), 2);
            using (ScreenRecorder rec = ScreenRecorder.Create(new ScreenFrameSource(monitor, false, (area.X, area.Y, area.Width, area.Height), false), [], Options(30, audio: false), path))
            {
                rec.Start();
                Thread.Sleep(2000);
                rec.Stop();
            }
        }
        finally
        {
            close.Set();
            thread.Join(TimeSpan.FromSeconds(5));
        }

        Core.Imaging.PixelBuffer frame = VideoAnalysis.DecodeFrame(path, 1000) ?? throw new InvalidOperationException("No frame decoded.");
        File.Delete(path);
        PixelRect t = toolbarRect ?? throw new InvalidOperationException("Toolbar not placed.");
        int x0 = t.X - area.X, y0 = t.Y - area.Y;
        int total = 0, greenCount = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(frame.Height, y0 + t.Height); y += 2)
        {
            for (int x = Math.Max(0, x0); x < Math.Min(frame.Width, x0 + t.Width); x += 2)
            {
                int o = ((y * frame.Width) + x) * 4;
                byte b = frame.Pixels[o], g = frame.Pixels[o + 1], r = frame.Pixels[o + 2];
                total++;
                if (g > 200 && r < 60 && b < 60)
                {
                    greenCount++;
                }
            }
        }

        return total == 0 ? 0 : Math.Round(greenCount / (double)total, 4);
    }

    private static long PackPoint(int x, int y) => ((long)y << 32) | (uint)x;

    /// <summary>
    /// Precondition for the screen-based checks: the top-level windows Windows reports over a grid of points in
    /// <paramref name="area"/> that are not <paramref name="ours"/> (e.g. a runner console or notification on top of
    /// the test window). Null when only our windows are there. CI run 132 recorded 0 % of the test background with no
    /// explanation; this names the cause instead.
    /// </summary>
    internal static unsafe string? ForeignWindowsOver(PixelRect area, IReadOnlyCollection<nint> ours)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        char* cls = stackalloc char[128];
        char* title = stackalloc char[128];
        for (int i = 1; i <= 5; i++)
        {
            for (int j = 1; j <= 3; j++)
            {
                int x = area.X + (area.Width * i / 6), y = area.Y + (area.Height * j / 4);
                nint root = GetAncestor(WindowFromPoint(PackPoint(x, y)), 2 /* GA_ROOT */);
                if (root == 0 || ours.Contains(root))
                {
                    continue;
                }

                int nc = GetClassNameW(root, cls, 128), nt = GetWindowTextW(root, title, 128);
                found.Add($"0x{root:X} '{new string(cls, 0, nc)}' \"{new string(title, 0, nt)}\" at {x},{y}");
            }
        }

        return found.Count == 0 ? null : string.Join("; ", found);
    }

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(long point);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetClassNameW(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetWindowTextW(nint hwnd, char* text, int max);

    /// <summary>Records the synthetic flash/beep sources, optionally pausing, then measures sync from the file.</summary>
    /// <summary>Diagnostics of the last synthetic recording: where flash frames were produced and placed.</summary>
    internal static object? LastDiagnostics { get; private set; }

    /// <summary>
    /// True synchronisation error per flash/beep pair: (decoded beep − decoded flash) − (produced beep − produced flash),
    /// both produced times mapped through the recording clock. 0 = audio and video exactly as captured.
    /// </summary>
    internal sealed record SyncError(IReadOnlyList<double> ErrorsMs, double MeanMs, double MaxAbsMs, double DriftMsPerMinute, IReadOnlyList<double> VideoErrorsMs, IReadOnlyList<double> AudioErrorsMs);

    internal static SyncError? LastSyncError { get; private set; }

    private static SyncError ComputeSyncError(SyncReport decoded, IReadOnlyList<double> producedFlashesMs, IReadOnlyList<double> producedBeepsMs)
    {
        static double Nearest(IReadOnlyList<double> xs, double v) => xs.Count == 0 ? double.NaN : xs.MinBy(x => Math.Abs(x - v));
        var errors = new List<double>();
        var times = new List<double>();
        var videoErrors = new List<double>();
        var audioErrors = new List<double>();
        foreach (double f in decoded.FlashesMs)
        {
            double b = decoded.BeepsMs.Count == 0 ? double.NaN : decoded.BeepsMs.MinBy(x => Math.Abs(x - f));
            if (double.IsNaN(b) || Math.Abs(b - f) > 500)
            {
                continue;
            }

            double pf = Nearest(producedFlashesMs, f), pb = Nearest(producedBeepsMs, b);
            if (double.IsNaN(pf) || double.IsNaN(pb))
            {
                continue;
            }

            videoErrors.Add(Math.Round(f - pf, 2));
            audioErrors.Add(Math.Round(b - pb, 2));
            errors.Add(Math.Round((b - f) - (pb - pf), 2));
            times.Add(f);
        }

        double slope = 0;
        if (times.Count > 1)
        {
            double mx = times.Average(), my = errors.Average();
            double num = times.Zip(errors).Sum(p => (p.First - mx) * (p.Second - my));
            double den = times.Sum(t => (t - mx) * (t - mx));
            slope = den == 0 ? 0 : num / den;
        }

        return new SyncError(errors, errors.Count == 0 ? double.NaN : Math.Round(errors.Average(), 2), errors.Count == 0 ? double.NaN : errors.Max(Math.Abs), Math.Round(slope * 60_000, 3), videoErrors, audioErrors);
    }

    /// <summary>Sync measured on the fragmented recording file, before conversion (diagnostics).</summary>
    internal static SyncReport? LastFragmentedSync { get; private set; }

    internal static (RecordingResult Result, SyncReport Sync) RecordSynthetic(string path, int w, int h, int fps, TimeSpan length, (TimeSpan At, TimeSpan For)? pauseAt)
    {
        var placed = new List<(long Source, long Media)>();
        List<double> producedFlashes = [], producedBeeps = [];
        string fragmented = Path.ChangeExtension(path, ".fragmented.mp4");
        const long flashEvery = 10_000_000; // 1 s
        var video = new SyntheticFrameSource(w, h, fps, flashEvery);
        var audio = new SyntheticAudioSource(() => video.StartTime, flashEvery);
        RecordingResult result;
        using (ScreenRecorder rec = ScreenRecorder.Create(video, [audio], Options(fps) with { RecordSystemAudio = true }, fragmented))
        {
            // The synthetic source is named differently from the system/mic names: enable it explicitly.
            rec.FrameScheduled = (source, media) =>
            {
                lock (placed)
                {
                    placed.Add((source, media));
                }
            };
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
            producedFlashes = [.. video.FlashTimes.Select(rec.MapSourceTime).OfType<long>().Select(m => m / 10_000.0)];
            producedBeeps = [.. audio.BeepTimes.Select(rec.MapSourceTime).OfType<long>().Select(m => m / 10_000.0)];
            long clockStart = rec.ClockStart;
            long videoStart = video.StartTime;
            var flashSet = video.FlashTimes.ToHashSet();
            LastDiagnostics = new
            {
                clockStartMinusVideoStartMs = (clockStart - videoStart) / 10_000.0,
                producedFlashesMs = video.FlashTimes.Select(f => Math.Round((f - videoStart) / 10_000.0, 1)),
                placedFlashesMediaMs = placed.Where(p => flashSet.Contains(p.Source)).Select(p => new { sourceMs = Math.Round((p.Source - clockStart) / 10_000.0, 1), placedMs = Math.Round(p.Media / 10_000.0, 1) }),
                firstFramesPlaced = placed.Take(12).Select(p => new { sourceMs = Math.Round((p.Source - clockStart) / 10_000.0, 1), placedMs = Math.Round(p.Media / 10_000.0, 1) }),
                framesPlaced = placed.Count,
            };
        }

        // As in the app: the fragmented recording becomes the final progressive MP4; sync is measured on that.
        LastFragmentedSync = VideoAnalysis.MeasureSync(fragmented, 1000);
        File.Delete(path);
        Mp4Defragmenter.Convert(fragmented, path);
        LastFragmentedPath = fragmented;
        SyncReport final = VideoAnalysis.MeasureSync(path, 1000);
        LastSyncError = ComputeSyncError(final, producedFlashes, producedBeeps);
        return (result, final);
    }

    internal static string? LastFragmentedPath { get; private set; }

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
        bool modeChanged = false;
        try
        {
            if (source == "screen")
            {
                modeChanged = TrySetDisplayMode(w, h);
                Thread.Sleep(modeChanged ? 2000 : 0);
            }

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
                int fragments = Mp4Inspector.Inspect(path).CompleteFragments;
                string final = Path.ChangeExtension(path, ".final.mp4");
                var conv = Stopwatch.StartNew();
                Mp4Defragmenter.Convert(path, final);
                long finalizeMs = conv.ElapsedMilliseconds;
                object? sync = source == "synthetic" ? SyncSummary(VideoAnalysis.MeasureSync(final, 1000)) : null;
                Mp4Structure s = Mp4Inspector.Inspect(final);
                row = new { name, status = "measured", result = Summary(r), sync, valid = s.IsComplete && !s.IsFragmented, fragmentsWhileRecording = fragments, finalizeMs, mbPerMinute = Math.Round(r.Bytes / 1048576.0 / (r.DurationMs / 60000.0), 1) };
                File.Delete(path);
                File.Delete(final);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            row = new { name, status = "failed", reason = ex.Message };
        }
        finally
        {
            if (modeChanged)
            {
                _ = ChangeDisplaySettingsW(0, 0); // back to the registry mode
            }
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

    /// <summary>The timestamps as stored in the file's fragments (independent of the Windows decoder).</summary>
    private static object StoredTiming(string path)
    {
        Mp4TimingReport t = Mp4Timing.Read(path);
        return new
        {
            tracks = t.Tracks.Select(kv => new { id = kv.Key, handler = kv.Value.Handler, timescale = kv.Value.Timescale }),
            discontinuities = t.Tracks.Keys.ToDictionary(k => k.ToString(System.Globalization.CultureInfo.InvariantCulture), k => t.Discontinuities(k).Select(d => $"frag {d.Fragment}: expected {d.Expected}, stored {d.Actual}")),
            firstFragments = t.Fragments.Take(24).Select(f => $"#{f.Fragment} trk{f.TrackId} base={f.BaseDecodeTime} n={f.Samples} dur={f.TotalDuration} cto={f.MinCompositionOffset}..{f.MaxCompositionOffset}"),
        };
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
        s.FirstVideoMs,
        s.NonMonotonicTimestamps,
        s.VideoDurationMs,
        s.AudioDurationMs,
        flashes = s.FlashesMs.Count,
        beeps = s.BeepsMs.Count,
        offsets = s.OffsetsMs.Select(o => Math.Round(o, 1)),
    };
}
