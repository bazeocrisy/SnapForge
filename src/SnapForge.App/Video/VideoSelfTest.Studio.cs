using System.Diagnostics;
using System.IO;
using SnapForge.App.Services;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Video;

/// <summary>
/// Self-test checks for SnapForge Studio and the recording lifecycle (SF-006, FA-021, FA-022). The CI runner has no
/// camera, GPU or audio device: camera checks use <see cref="SyntheticCameraSource"/> and every line says SYNTHETIC;
/// the real camera count is reported as UNAVAILABLE when there is none (never as PASS).
/// </summary>
internal static partial class VideoSelfTest
{
    private static void StudioChecks(string dir, Dictionary<string, object?> report, Action<string, bool, string> check, Action<string, string> unavailable)
    {
        // Screen + SYNTHETIC camera composited into a real screen recording; Studio on screen at the same time.
        try
        {
            var preview = new PreviewEvidence();
            (StudioOverlayResult o, double studioGreen, string? studioError, string? covered, string diagnostics, double loadedWorkMs, double limeVisibleMs) = ScreenPlusSyntheticCamera(dir, preview);
            report["studioScreenPlusCamera"] = new { synthetic = true, o.InsideShare, o.OtherCorners, o.GreenElsewhere, o.Layout, result = Summary(o.Result), studioWindowGreen = studioGreen, studioError, coveredBy = covered, diagnostics };
            report["cameraPreview"] = new { synthetic = true, preview.Excluded, preview.FramesShown, rect = preview.Rect?.ToString(), preview.SyntheticInRecording, preview.Caption, preview.Error };
            string precondition = covered is not null ? $" — PRECONDITION NOT MET: the test area was covered by {covered}"
                : limeVisibleMs < 0 ? " — PRECONDITION NOT MET: the test window was not painted within 5 s" : string.Empty;
            double dropped = o.Result.FramesWritten == 0 ? 1 : o.Result.FramesDropped / (double)(o.Result.FramesWritten + o.Result.FramesDropped);
            check("studio-screen-plus-camera-synthetic",
                o.Result.Finalized && o.InsideShare >= 0.9 && o.OtherCorners.All(c => c <= 0.02) && o.GreenElsewhere >= 0.9 && dropped < 0.01,
                $"SYNTHETIC camera in the bottom-right {o.Layout}: {o.InsideShare:P1} of the overlay shows the synthetic pattern, other corners {string.Join(" / ", o.OtherCorners.Select(c => c.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)))}, screen elsewhere {o.GreenElsewhere:P1} unchanged; {o.Result.FramesWritten} frames, {dropped:P2} dropped{precondition} — {diagnostics}");
            check("studio-window-not-recorded", studioError is null && studioGreen >= 0.98,
                studioError ?? $"the Studio window's area is {studioGreen:P1} background in the recording (Studio is excluded from capture){precondition}");
            // RC3: the floating camera preview (showing the SAME synthetic camera) sits over the recorded area, away from
            // the overlay corner. It must be live, excluded from capture, and the camera must appear in the video once.
            check("camera-preview-live-and-not-recorded",
                preview.Error is null && preview.Excluded && preview.FramesShown > 0 && preview.SyntheticInRecording <= 0.02 && o.InsideShare >= 0.9,
                preview.Error ?? $"SYNTHETIC camera preview over the recorded area at {preview.Rect}: {preview.FramesShown} live frames shown, excluded from capture: {preview.Excluded}, " +
                $"synthetic pattern where the preview was: {preview.SyntheticInRecording:P1} (camera composed once, in the overlay: {o.InsideShare:P1}); caption \"{preview.Caption}\"{precondition}");
            // The app must not freeze while Studio looks for cameras (DEF-RC2-07 investigation).
            check("studio-ui-not-blocked", loadedWorkMs >= 0 && loadedWorkMs <= 500,
                $"opening SnapForge Studio blocked its UI thread for {loadedWorkMs:0} ms (limit 500 ms; includes listing cameras)");
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["studioScreenPlusCamera"] = new { error = ex.ToString() };
            check("studio-screen-plus-camera-synthetic", false, ex.Message);
        }

        // Camera only, SYNTHETIC 1280×720 camera: the video is the camera feed at its own size.
        try
        {
            string path = Path.Combine(dir, "studio-camera-only-synthetic.mp4");
            var camera = new SyntheticCameraSource(1280, 720, 30);
            camera.Start();
            RecordingResult r;
            var sw = Stopwatch.StartNew();
            using (ScreenRecorder rec = ScreenRecorder.Create(new CameraFrameSource(camera, mirror: true), [], Options(30, audio: false), path))
            {
                rec.Start();
                Thread.Sleep(3000);
                r = rec.Stop();
            }

            (long durationMs, int w, int h, bool _, int samples) = RecordingRecovery.Probe(path);
            report["studioCameraOnly"] = new { synthetic = true, durationMs, w, h, samples, result = Summary(r), cameraReleased = !camera.IsRunning };
            check("studio-camera-only-synthetic", r.Finalized && w == 1280 && h == 720 && Math.Abs(durationMs - 3000) <= 200 && Math.Abs(samples - (durationMs * 30 / 1000.0)) <= 2 && !camera.IsRunning,
                $"SYNTHETIC camera only: {w}x{h}, {durationMs} ms for 3 s, {samples} frames, camera released after stop: {!camera.IsRunning}");
            File.Delete(path);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["studioCameraOnly"] = new { error = ex.ToString() };
            check("studio-camera-only-synthetic", false, ex.Message);
        }

        // Real cameras: listed without being opened (the camera light stays off).
        try
        {
            IReadOnlyList<Interop.Mf.VideoDevice> devices = MediaFoundationCameraSource.Enumerate();
            report["cameraEnumeration"] = new { count = devices.Count, names = devices.Select(d => d.Name) };
            if (devices.Count == 0)
            {
                unavailable("camera-enumeration", "0 video capture devices on this machine (expected on the CI runner). Real cameras are tested on a physical PC (docs/VIDEO-ACCEPTANCE.md).");
            }
            else
            {
                check("camera-enumeration", true, $"{devices.Count} camera(s): {string.Join("; ", devices.Select(d => d.Name))} (listed, not opened)");
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report["cameraEnumeration"] = new { error = ex.ToString() };
            check("camera-enumeration", false, "MFEnumDeviceSources failed: " + ex.Message);
        }

        // Several Stop calls at once: one finalization, the same result for every caller.
        try
        {
            string path = Path.Combine(dir, "lifecycle-double-stop.mp4");
            using ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(320, 240, 30, 0), [], Options(30, audio: false), path);
            rec.Start();
            Thread.Sleep(1500);
            var results = new RecordingResult[4];
            Parallel.For(0, results.Length, i => results[i] = rec.Stop());
            bool same = results.All(x => ReferenceEquals(x, results[0])) && ReferenceEquals(rec.Stop(), results[0]);
            bool cancelIgnored = !rec.Cancel();
            bool complete = Mp4Inspector.Inspect(path).IsComplete;
            check("lifecycle-double-stop", same && cancelIgnored && results[0].Finalized && complete && !ScreenRecorder.IsRecording,
                $"{results.Length} concurrent stops → one result (same: {same}), finalized {results[0].Finalized}, file complete {complete}, later cancel ignored {cancelIgnored}");
            File.Delete(path);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            check("lifecycle-double-stop", false, ex.Message);
        }

        // A stalled encoder: Stop returns within the bound, reports it, and the file is recoverable.
        try
        {
            string path = Path.Combine(dir, "lifecycle-stall.mp4");
            using var gate = new ManualResetEventSlim();
            TimeSpan timeout = TimeSpan.FromSeconds(2);
            var hooks = new RecorderHooks
            {
                JoinTimeout = timeout,
                BeforeVideoWrite = written =>
                {
                    if (written == 90)
                    {
                        gate.Wait(TimeSpan.FromSeconds(60)); // injected stall after 3 s of video
                    }
                },
            };
            ScreenRecorder rec = ScreenRecorder.Create(new SyntheticFrameSource(320, 240, 30, 0), [], Options(30, audio: false), path, hooks);
            rec.Start();
            Thread.Sleep(4000);
            var sw = Stopwatch.StartNew();
            RecordingResult r = rec.Stop();
            sw.Stop();
            bool released = !ScreenRecorder.IsRecording;
            bool stillRunning = !rec.ThreadsExited.IsCompleted;
            rec.Dispose();
            gate.Set();
            bool exited = rec.ThreadsExited.Wait(TimeSpan.FromSeconds(15));
            Mp4Structure? s = Mp4Inspector.Inspect(path) is { IsComplete: true } done ? done : Mp4Inspector.Recover(path);
            int samples = s is null ? 0 : RecordingRecovery.Probe(path).VideoSamples;
            report["lifecycleStall"] = new { stopMs = sw.ElapsedMilliseconds, r.Stalled, r.Finalized, released, stillRunning, exited, samples, r.Notices };
            check("lifecycle-stall-timeout", r.Stalled && !r.Finalized && sw.Elapsed < (timeout * 3) + TimeSpan.FromSeconds(2) && released && stillRunning && exited && samples >= 60,
                $"encoder stalled (injected): Stop returned after {sw.ElapsedMilliseconds} ms (bound {timeout.TotalSeconds:0} s per thread), stalled reported {r.Stalled}, new recording allowed {released}; after the stall ended the threads exited {exited} and the file holds {samples} recoverable frames");
            File.Delete(path);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            check("lifecycle-stall-timeout", false, ex.Message);
        }

        // FA-022: save outside the Library — verified temp copy + atomic replace; a failure changes nothing.
        try
        {
            string folder = Path.Combine(dir, "save-outside");
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "library-video.mp4");
            string target = Path.Combine(folder, "chosen.mp4");
            var data = new byte[3 * 1024 * 1024];
            new Random(7).NextBytes(data);
            File.WriteAllBytes(source, data);
            File.WriteAllBytes(target, [1, 2, 3]);
            bool failedSafely;
            try
            {
                VerifiedFileCopy.Copy(source, target, s => new FullDiskStream(s, 1024 * 1024));
                failedSafely = false;
            }
            catch (IOException)
            {
                failedSafely = File.ReadAllBytes(target).SequenceEqual(new byte[] { 1, 2, 3 }) && File.Exists(source);
            }

            VerifiedFileCopy.Copy(source, target);
            bool copied = File.ReadAllBytes(target).AsSpan().SequenceEqual(data);
            bool noTemp = !Directory.GetFiles(folder).Any(f => VerifiedFileCopy.IsTempName(Path.GetFileName(f)));
            check("save-outside-library-atomic", failedSafely && copied && noTemp,
                $"disk full while copying left the existing file and the Library copy intact: {failedSafely}; verified copy replaced the destination: {copied}; no temporary file left: {noTemp}");
            Directory.Delete(folder, recursive: true);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            check("save-outside-library-atomic", false, ex.Message);
        }
    }

    private sealed record StudioOverlayResult(RecordingResult Result, double InsideShare, IReadOnlyList<double> OtherCorners, double GreenElsewhere, string Layout);

    /// <summary>
    /// Shows a lime window and SnapForge Studio over part of it, records the lime area with the SYNTHETIC camera
    /// composited in the bottom-right corner, decodes a frame and measures where the synthetic pattern appears.
    /// </summary>
    /// <summary>What the camera-preview part of <see cref="ScreenPlusSyntheticCamera"/> observed.</summary>
    private sealed class PreviewEvidence
    {
        public bool Excluded { get; set; }

        public int FramesShown { get; set; }

        public PixelRect? Rect { get; set; }

        public double SyntheticInRecording { get; set; } = 1;

        public string? Caption { get; set; }

        public string? Error { get; set; }
    }

    private static (StudioOverlayResult Overlay, double StudioGreen, string? StudioError, string? Covered, string Diagnostics, double LoadedWorkMs, double LimeVisibleMs) ScreenPlusSyntheticCamera(string dir, PreviewEvidence previewEvidence)
    {
        // One SYNTHETIC camera, as in the app: the preview shows the camera the recorder composes.
        var camera = new SyntheticCameraSource(640, 360, 30);
        camera.Start();
        CameraPreviewWindow? previewWindow = null;
        PixelRect area = default;
        var ours = new List<nint>();
        System.Windows.Window? limeWindow = null;
        StudioWindow? studioWindow = null;
        PixelRect? studioRect = null;
        string? studioError = null;
        var ready = new ManualResetEventSlim();
        var close = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var lime = new System.Windows.Window
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
            lime.Show();
            limeWindow = lime;
            ours.Add(new System.Windows.Interop.WindowInteropHelper(lime).Handle);
            area = SnapForge.App.Interop.PhysicalPlacement.GetBounds(lime)!.Value;
            StudioWindow? studio = null;
            AppServices? services = null;
            try
            {
                string settings = Path.Combine(dir, "studio-selftest", "settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
                services = AppServices.Create(settings, Path.Combine(dir, "studio-selftest", "library"));
                studio = new StudioWindow(services) { Topmost = true, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual };
                studio.Show();
                studioWindow = studio;
                ours.Add(new System.Windows.Interop.WindowInteropHelper(studio).Handle);
                // Over the left part of the lime area, away from the bottom-right camera overlay.
                SnapForge.App.Interop.PhysicalPlacement.SetBounds(studio, new PixelRect(area.X + 20, area.Y + 20, 300, 250), resize: true);
                studio.UpdateLayout();
                studioRect = SnapForge.App.Interop.PhysicalPlacement.GetBounds(studio);
                if (!studio.Excluded)
                {
                    studioError = "Windows did not confirm the Studio window's exclusion from capture.";
                }

                try
                {
                    var preview = new CameraPreviewWindow();
                    preview.SetShape(CameraOverlayShape.Circle, mirror: true);
                    preview.SetState("In the recording · bottom right", inRecording: true);
                    preview.Show();
                    preview.SetSource(camera);
                    previewWindow = preview;
                    ours.Add(new System.Windows.Interop.WindowInteropHelper(preview).Handle);
                    // After its own first placement (Loaded): put it over the top-right part of the recorded area.
                    _ = preview.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
                    {
                        if (SnapForge.App.Interop.PhysicalPlacement.GetBounds(preview) is { } pb)
                        {
                            SnapForge.App.Interop.PhysicalPlacement.SetBounds(preview, new PixelRect(area.Right - pb.Width - 10, area.Y + 6, pb.Width, pb.Height), resize: false);
                        }
                    });
                }
#pragma warning disable CA1031
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    previewEvidence.Error = "The camera preview could not be shown: " + ex.Message;
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                studioError = "Studio could not be shown: " + ex.Message;
            }

            ready.Set();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                if (close.IsSet)
                {
                    previewWindow?.Close();
                    studio?.Close();
                    lime.Close();
                    services?.Dispose();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(15));
        Thread.Sleep(700);
        // Precondition: nothing else on top of the test area. One attempt to bring the test windows back on top.
        string? covered = ForeignWindowsOver(area, ours);
        if (covered is not null && limeWindow is { } lw0)
        {
            lw0.Dispatcher.Invoke(() =>
            {
                lw0.Topmost = false;
                lw0.Topmost = true;
                lw0.Activate();
            });
            Thread.Sleep(700);
            string? still = ForeignWindowsOver(area, ours);
            covered = still is null ? null : $"{still} (still on top after re-raising the test windows; first seen: {covered})";
        }

        // Precondition (DEF-RC2-07, CI run 138): the test window must actually be painted before recording. Wait up
        // to 5 s, checking through a direct GDI capture (not the recorder's path); report how long it took.
        var visible = Stopwatch.StartNew();
        double limeBefore = 0, limeVisibleMs = -1;
        while (visible.ElapsedMilliseconds < 5000)
        {
            PixelBuffer before = SnapForge.App.Capture.ScreenCaptureService.CaptureRegion(area);
            limeBefore = Share(before, before.Width / 2, 0, (before.Width / 2) - 140, before.Height / 2, Lime); // same region as the recorded check
            if (limeBefore >= 0.9)
            {
                limeVisibleMs = visible.ElapsedMilliseconds;
                break;
            }

            Thread.Sleep(100);
        }

        (double loadedWorkMs, double cameraListMs) = studioWindow is { } sw0 ? sw0.Dispatcher.Invoke(() => (sw0.LoadedWorkMs, sw0.CameraListMs)) : (-1, -1);
        string path = Path.Combine(dir, "studio-screen-plus-camera-synthetic.mp4");
        RecordingResult result;
        OverlayLayout layout;
        try
        {
            nint monitor = MonitorFromPoint(PackPoint(area.X + 5, area.Y + 5), 2);
            if (previewWindow is { } pw)
            {
                (previewEvidence.Excluded, previewEvidence.Rect, previewEvidence.Caption) =
                    pw.Dispatcher.Invoke(() => (pw.Excluded, SnapForge.App.Interop.PhysicalPlacement.GetBounds(pw), pw.Describe()));
            }

            var composite = new CompositeFrameSource(new ScreenFrameSource(monitor, false, (area.X, area.Y, area.Width, area.Height), false), camera,
                CameraOverlayShape.Circle, CameraOverlaySize.Medium, CameraCorner.BottomRight, mirror: true);
            layout = composite.Layout;
            using ScreenRecorder rec = ScreenRecorder.Create(composite, [], Options(30, audio: false), path);
            rec.Start();
            Thread.Sleep(3000);
            result = rec.Stop();
        }
        finally
        {
            if (previewWindow is { } pw2)
            {
                previewEvidence.FramesShown = pw2.Dispatcher.Invoke(() => pw2.FramesShown);
            }

            close.Set();
            thread.Join(TimeSpan.FromSeconds(5));
            try
            {
                Directory.Delete(Path.Combine(dir, "studio-selftest"), recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        PixelBuffer frame = VideoAnalysis.DecodeFrame(path, 1500) ?? throw new InvalidOperationException("No frame decoded.");
        // Kept with the sample recordings (CI artifact) so a failure can be inspected as an image.
        File.WriteAllBytes(Path.Combine(dir, "studio-screen-plus-camera-frame.png"), Rendering.ImageExporter.Encode(frame, Core.Export.ImageFileFormat.Png));
        int w = frame.Width, h = frame.Height, lw = layout.Width, lh = layout.Height;
        double inside = Share(frame, layout.X + (lw / 4), layout.Y + (lh / 4), lw / 2, lh / 2, Synthetic);
        double[] others =
        [
            Share(frame, OverlayLayout.Margin(w, h), OverlayLayout.Margin(w, h), lw, lh, Synthetic),
            Share(frame, layout.X, OverlayLayout.Margin(w, h), lw, lh, Synthetic),
            Share(frame, OverlayLayout.Margin(w, h), layout.Y, lw, lh, Synthetic),
        ];
        double green = Share(frame, w / 2, 0, (w / 2) - lw - 30, h / 2, Lime); // top-right quarter minus the overlay column
        double studioGreen = 0;
        if (studioRect is { } s && studioError is null)
        {
            studioGreen = Share(frame, s.X - area.X, s.Y - area.Y, Math.Min(s.Width, layout.X - (s.X - area.X)), Math.Min(s.Height, h - (s.Y - area.Y)), Lime);
        }

        if (previewEvidence.Rect is { } pr && previewEvidence.Error is null)
        {
            int px = Math.Clamp(pr.X - area.X, 0, w - 1), py = Math.Clamp(pr.Y - area.Y, 0, h - 1);
            int pwid = Math.Min(pr.Width, w - px), phgt = Math.Min(Math.Min(pr.Height, h - py), Math.Max(1, layout.Y - py - 4)); // never the overlay itself
            previewEvidence.SyntheticInRecording = Math.Round(Share(frame, px, py, pwid, phgt, Synthetic), 4);
        }

        File.Delete(path);
        string diagnostics = $"test window painted after {(limeVisibleMs < 0 ? "more than 5000" : limeVisibleMs.ToString("0", System.Globalization.CultureInfo.InvariantCulture))} ms ({limeBefore:P0} lime in a direct screen capture); Studio's Loaded work blocked its UI thread {loadedWorkMs:0} ms (camera listing {cameraListMs:0} ms); recorded frame colours (4x3 grid, R,G,B): {ColourGrid(frame)}";
        return (new StudioOverlayResult(result, Math.Round(inside, 4), [.. others.Select(o => Math.Round(o, 4))], Math.Round(green, 4), $"{lw}x{lh} at {layout.X},{layout.Y} of {w}x{h}"), Math.Round(studioGreen, 4), studioError, covered, diagnostics, loadedWorkMs, limeVisibleMs);
    }

    /// <summary>Smallest and largest step between consecutive decoded video timestamps (ms).</summary>
    private static (double Min, double Max) VideoFrameGapsMs(string path)
    {
        var times = new List<long>();
        nint reader = VideoAnalysis.OpenDecoding(path, video: true, audio: false);
        try
        {
            while (Interop.Mf.ReadSample(reader, Interop.Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                times.Add(ts);
                Interop.Com.Release(ref sample);
            }
        }
        finally
        {
            Interop.Com.Release(ref reader);
        }

        var gaps = times.Zip(times.Skip(1), (a, b) => (b - a) / 10_000.0).ToList();
        return gaps.Count == 0 ? (0, 0) : (Math.Round(gaps.Min(), 1), Math.Round(gaps.Max(), 1));
    }

    /// <summary>Full red with blue or green: the SYNTHETIC camera's magenta / yellow (the test screen is lime: no red).</summary>
    /// <summary>Average colour of each cell of a 4×3 grid (diagnostics in failure messages).</summary>
    private static string ColourGrid(PixelBuffer frame)
    {
        var cells = new List<string>();
        for (int gy = 0; gy < 3; gy++)
        {
            for (int gx = 0; gx < 4; gx++)
            {
                long r = 0, g = 0, b = 0, n = 0;
                int x0 = frame.Width * gx / 4, x1 = frame.Width * (gx + 1) / 4, y0 = frame.Height * gy / 3, y1 = frame.Height * (gy + 1) / 3;
                for (int y = y0; y < y1; y += 4)
                {
                    for (int x = x0; x < x1; x += 4)
                    {
                        int o = ((y * frame.Width) + x) * 4;
                        b += frame.Pixels[o];
                        g += frame.Pixels[o + 1];
                        r += frame.Pixels[o + 2];
                        n++;
                    }
                }

                cells.Add(n == 0 ? "-" : $"{r / n},{g / n},{b / n}");
            }
        }

        return string.Join(" | ", cells);
    }

    private static bool Synthetic(byte b, byte g, byte r) => r > 180 && (b > 150 || g > 150);

    private static bool Lime(byte b, byte g, byte r) => g > 200 && r < 60 && b < 60;

    private static double Share(PixelBuffer frame, int x0, int y0, int w, int h, Func<byte, byte, byte, bool> test)
    {
        int total = 0, hits = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(frame.Height, y0 + h); y += 2)
        {
            for (int x = Math.Max(0, x0); x < Math.Min(frame.Width, x0 + w); x += 2)
            {
                int o = ((y * frame.Width) + x) * 4;
                total++;
                if (test(frame.Pixels[o], frame.Pixels[o + 1], frame.Pixels[o + 2]))
                {
                    hits++;
                }
            }
        }

        return total == 0 ? 0 : hits / (double)total;
    }

    /// <summary>Throws like a full disk after a number of bytes (save-outside fault injection).</summary>
    private sealed class FullDiskStream(Stream inner, long limit) : Stream
    {
        private long _written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_written + count > limit)
            {
                throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
            }

            inner.Write(buffer, offset, count);
            _written += count;
        }
    }
}
