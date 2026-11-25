using System.Windows;
using SnapForge.App.Capture;
using SnapForge.App.Dialogs;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Services;

/// <summary>Result of one capture.</summary>
internal sealed record CaptureResult(PixelBuffer Image, DateTime CapturedAt, CaptureMode Mode, int MonitorCount);

/// <summary>
/// Runs a capture with the given options: optional countdown, hides SnapForge's own windows, takes the frozen
/// snapshot (optionally with the mouse pointer), runs the selection overlay, and restores the windows.
/// </summary>
internal static class CaptureController
{
    private static readonly TimeSpan HideSettleDelay = TimeSpan.FromMilliseconds(180);
    private static bool _busy;

    public static bool IsBusy => _busy;

    public static async Task<CaptureResult?> CaptureAsync(CaptureOptions options)
    {
        if (_busy)
        {
            return null;
        }

        _busy = true;
        var hidden = new List<(Window Window, WindowState State)>();
        try
        {
            if (options.DelaySeconds > 0 && !await CountdownWindow.RunAsync(options.DelaySeconds).ConfigureAwait(true))
            {
                return null;
            }

            foreach (Window w in Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToList())
            {
                hidden.Add((w, w.WindowState));
                w.Hide();
            }

            await Task.Delay(HideSettleDelay).ConfigureAwait(true);
            CaptureSnapshot snapshot = ScreenCaptureService.TakeSnapshot(options.CaptureCursor);
            PixelRect? selection = options.Mode == CaptureMode.AllScreens
                ? snapshot.Layout.VirtualBounds
                : await OverlaySession.RunAsync(snapshot, options.Mode).ConfigureAwait(true);

            PixelBuffer? image = null;
            if (selection is { } screenRect)
            {
                PixelRect bitmapRect = snapshot.Layout.ClipSelectionToBitmap(screenRect);
                image = bitmapRect == snapshot.Image.Bounds ? snapshot.Image : snapshot.Image.Crop(bitmapRect);
            }

            // The full-desktop frame usually holds far more than the user selected; zero it once it is not the result.
            if (!ReferenceEquals(image, snapshot.Image))
            {
                Array.Clear(snapshot.Image.Pixels);
            }

            return image is null ? null : new CaptureResult(image, snapshot.CapturedAt, options.Mode, snapshot.Layout.Monitors.Count);
        }
        finally
        {
            // Restore windows that were visible (the caller decides whether the Editor should come forward).
            foreach ((Window w, WindowState state) in hidden)
            {
                if (w.IsLoaded)
                {
                    w.Show();
                    w.WindowState = state == WindowState.Minimized ? WindowState.Normal : state;
                }
            }

            _busy = false;
        }
    }
}
