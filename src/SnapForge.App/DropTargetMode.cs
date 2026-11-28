using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SnapForge.App.Interop;
using SnapForge.Core.Geometry;

namespace SnapForge.App;

/// <summary>
/// <c>SnapForge.exe --drop-target &lt;dir&gt;</c>: a plain drop target in its <b>own process</b> for drag-and-drop tests
/// (owner requirement: external MP4 drag validated with a separate-process drop target). Writes <c>ready.json</c> with
/// its screen rectangle, and on drop copies nothing — it records each dropped file's name, size and SHA-256 at once
/// (before the sender's temporary copies expire) into <c>dropped.json</c>, then exits.
/// </summary>
internal static class DropTargetMode
{
    public static void Run(Application app, string dir)
    {
        Directory.CreateDirectory(dir);
        var window = new Window
        {
            Title = "SnapForge drop-target test",
            Width = 260,
            Height = 180,
            Left = 620,
            Top = 120,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Topmost = true,
            AllowDrop = true,
            Background = Brushes.LightSteelBlue,
            ShowActivated = false,
        };
        window.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        window.Drop += (_, e) =>
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
            var rows = files.Select(f => new
            {
                name = Path.GetFileName(f),
                bytes = new FileInfo(f).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))),
            }).ToList();
            File.WriteAllText(Path.Combine(dir, "dropped.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, files = rows }));
            e.Handled = true;
            app.Shutdown(0);
        };
        window.Loaded += (_, _) =>
        {
            PixelRect? r = PhysicalPlacement.GetBounds(window);
            if (r is { } b)
            {
                File.WriteAllText(Path.Combine(dir, "ready.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, x = b.X + (b.Width / 2), y = b.Y + (b.Height / 2) }));
            }
        };
        _ = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
    }
}
