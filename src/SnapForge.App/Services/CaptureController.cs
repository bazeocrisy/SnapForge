using System.Windows;
using SnapForge.App.Capture;
using SnapForge.App.Dialogs;
using SnapForge.Core.Capture;
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

    /// <summary>Screen rectangle of the last successful capture (for "Repeat last capture"); memory only.</summary>
    public static PixelRect? LastSelection { get; private set; }

    /// <summary>Options of the last successful capture.</summary>
    public static CaptureOptions? LastOptions { get; private set; }

    /// <param name="options">What to capture and how.</param>
    /// <param name="fixedRegion">Capture exactly this screen rectangle without the selection overlay (repeat last capture).</param>
    public static async Task<CaptureResult?> CaptureAsync(CaptureOptions options, PixelRect? fixedRegion = null)
    {
        if (_busy)
        {
            return null;
        }

        _busy = true;
        var hidden = new List<(Window Window, WindowState State)>();
        // The window the user was working in, recorded before the countdown or SnapForge's windows take focus.
        nint foreground = options.Mode == CaptureMode.ActiveWindow ? ForegroundOtherWindow() : 0;
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
            PixelRect? selection;
            if (fixedRegion is { } fixedRect)
            {
                PixelRect clipped = fixedRect.Intersect(snapshot.Layout.VirtualBounds);
                selection = clipped.IsEmpty ? null : clipped;
            }
            else if (options.Mode == CaptureMode.AllScreens)
            {
                selection = snapshot.Layout.VirtualBounds;
            }
            else if (options.Mode == CaptureMode.ActiveWindow)
            {
                WindowCandidate? target = snapshot.Windows.FirstOrDefault(w => w.Handle == foreground)
                    ?? snapshot.Windows.OrderBy(w => w.ZOrder).FirstOrDefault();
                selection = target is null
                    ? await OverlaySession.RunAsync(snapshot, CaptureMode.Window).ConfigureAwait(true)
                    : WindowPicker.CaptureRegionFor(target, snapshot.Layout);
            }
            else
            {
                selection = await OverlaySession.RunAsync(snapshot, options.Mode).ConfigureAwait(true);
            }

            if (selection is { } chosen)
            {
                LastSelection = chosen;
                LastOptions = options;
            }

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

    /// <summary>The foreground top-level window if it belongs to another program; 0 otherwise.</summary>
    private static nint ForegroundOtherWindow()
    {
        nint hwnd = Interop.NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return 0;
        }

        _ = Interop.NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId ? 0 : hwnd;
    }
}
