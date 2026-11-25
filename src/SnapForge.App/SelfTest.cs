using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SnapForge.App.Capture;
using SnapForge.App.Rendering;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Export;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.App;

/// <summary>
/// Non-interactive diagnostic: <c>SnapForge.exe --self-test report.txt</c>. Used by CI to prove the
/// portable build starts and works on a machine with no .NET installation, and usable by testers.
/// It reads the screen once (in memory only), writes only the report file, and never uses the network.
/// Exit code 0 = all checks passed.
/// </summary>
internal static class SelfTest
{
    public static int Run(string reportPath)
    {
        var lines = new List<string> { $"SnapForge self-test {DateTime.Now:O}" };
        bool ok = true;

        void Check(string name, Func<string> check)
        {
            try
            {
                lines.Add($"PASS {name}: {check()}");
            }
#pragma warning disable CA1031 // Every failure is reported in the file and the exit code.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ok = false;
                lines.Add($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Check("runtime", () =>
            $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}; {RuntimeInformation.OSDescription}; " +
            $"exe={Environment.ProcessPath}");

        Check("self-contained runtime", () =>
        {
            // A self-contained app must not load the runtime from a machine-wide .NET installation.
            string[] modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Select(m => m.FileName ?? string.Empty).ToArray();
            string[] shared = modules.Where(p => p.Contains(@"\dotnet\shared\", StringComparison.OrdinalIgnoreCase)
                || p.Contains(@"\dotnet\host\fxr\", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (shared.Length > 0)
            {
                throw new InvalidOperationException("Loaded from a shared .NET install: " + string.Join(", ", shared));
            }

            string[] wpfNative = modules.Where(p => p.EndsWith("_cor3.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
            return $"no shared-runtime modules; WPF native modules: {string.Join(", ", wpfNative)}";
        });

        Check("screen capture (GDI)", () =>
        {
            CaptureSnapshot snapshot = ScreenCaptureService.TakeSnapshot();
            string result = $"{snapshot.Layout.Monitors.Count} monitor(s), desktop {snapshot.Layout.VirtualBounds}, {snapshot.Windows.Count} windows";
            Array.Clear(snapshot.Image.Pixels);
            return result;
        });

        Check("main window + edit + redact + export", () =>
        {
            var window = new MainWindow();
            try
            {
                window.Show();
                Pump();
                var image = new PixelBuffer(320, 200);
                image.FillRect(image.Bounds, ArgbColor.White);
                window.LoadImage(image, DateTime.Now);
                Pump();
                EditorDocument doc = window.Document!;
                doc.Execute(new AddAnnotationCommand(new ArrowAnnotation(new PointD(10, 10), new PointD(150, 100))));
                doc.Execute(new RedactCommand(new PixelRect(200, 50, 80, 40), ArgbColor.Black));
                Pump();
                PixelBuffer final = window.RenderFinalImage();
                if (final.GetPixel(240, 70) != ArgbColor.Black)
                {
                    throw new InvalidOperationException("Redacted pixel is not black in the export.");
                }

                int png = ImageExporter.Encode(final, ImageFileFormat.Png).Length;
                int jpeg = ImageExporter.Encode(final, ImageFileFormat.Jpeg).Length;
                doc.MarkSaved();
                return $"rendered {final.Width}x{final.Height}; PNG {png} bytes; JPEG {jpeg} bytes";
            }
            finally
            {
                window.Document?.MarkSaved();
                window.Close();
            }
        });

        lines.Add(ok ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllLines(reportPath, lines);
        return ok ? 0 : 1;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
