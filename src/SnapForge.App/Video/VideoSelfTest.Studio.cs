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
            (StudioOverlayResult o, double studioGreen, string? studioError) = ScreenPlusSyntheticCamera(dir);
            report["studioScreenPlusCamera"] = new { synthetic = true, o.InsideShare, o.OtherCorners, o.GreenElsewhere, o.Layout, result = Summary(o.Result), studioWindowGreen = studioGreen, studioError };
            double dropped = o.Result.FramesWritten == 0 ? 1 : o.Result.FramesDropped / (double)(o.Result.FramesWritten + o.Result.FramesDropped);
            check("studio-screen-plus-camera-synthetic",
                o.Result.Finalized && o.InsideShare >= 0.9 && o.OtherCorners.All(c => c <= 0.02) && o.GreenElsewhere >= 0.9 && dropped < 0.01,
                $"SYNTHETIC camera in the bottom-right {o.Layout}: {o.InsideShare:P1} of the overlay shows the synthetic pattern, other corners {string.Join(" / ", o.OtherCorners.Select(c => c.ToString("P1", System.Globalization.CultureInfo.InvariantCulture)))}, screen elsewhere {o.GreenElsewhere:P1} unchanged; {o.Result.FramesWritten} frames, {dropped:P2} dropped");
            check("studio-window-not-recorded", studioError is null && studioGreen >= 0.98,
                studioError ?? $"the Studio window's area is {studioGreen:P1} background in the recording (Studio is excluded from capture)");
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
    private static (StudioOverlayResult Overlay, double StudioGreen, string? StudioError) ScreenPlusSyntheticCamera(string dir)
    {
        PixelRect area = default;
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
                // Over the left part of the lime area, away from the bottom-right camera overlay.
                SnapForge.App.Interop.PhysicalPlacement.SetBounds(studio, new PixelRect(area.X + 20, area.Y + 20, 300, 250), resize: true);
                studio.UpdateLayout();
                studioRect = SnapForge.App.Interop.PhysicalPlacement.GetBounds(studio);
                if (!studio.Excluded)
                {
                    studioError = "Windows did not confirm the Studio window's exclusion from capture.";
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
        string path = Path.Combine(dir, "studio-screen-plus-camera-synthetic.mp4");
        RecordingResult result;
        OverlayLayout layout;
        try
        {
            nint monitor = MonitorFromPoint(PackPoint(area.X + 5, area.Y + 5), 2);
            var camera = new SyntheticCameraSource(640, 360, 30);
            camera.Start();
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

        File.Delete(path);
        return (new StudioOverlayResult(result, Math.Round(inside, 4), [.. others.Select(o => Math.Round(o, 4))], Math.Round(green, 4), $"{lw}x{lh} at {layout.X},{layout.Y} of {w}x{h}"), Math.Round(studioGreen, 4), studioError);
    }

    /// <summary>Full red with blue or green: the SYNTHETIC camera's magenta / yellow (the test screen is lime: no red).</summary>
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
