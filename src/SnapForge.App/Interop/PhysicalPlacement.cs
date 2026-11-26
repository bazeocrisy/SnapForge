using System.Windows;
using System.Windows.Interop;
using SnapForge.App.Capture;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Interop;

/// <summary>Window positions in physical pixels (virtual-desktop coordinates), independent of WPF's DIP mapping.</summary>
internal static class PhysicalPlacement
{
    public static ScreenLayout Layout() => ScreenCaptureService.GetScreenLayout();

    public static PixelRect? GetBounds(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != 0 && NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r)
            ? PixelRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom)
            : null;
    }

    /// <summary>Moves (and optionally resizes) without activating or changing the Z order.</summary>
    public static void SetBounds(Window window, PixelRect bounds, bool resize)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        uint flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | (resize ? 0 : NativeMethods.SWP_NOSIZE);
        NativeMethods.SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
    }

    public static MonitorInfo MonitorFor(ScreenLayout layout, PixelRect bounds) =>
        layout.MonitorAt(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2))
        ?? layout.MonitorAt(bounds.X, bounds.Y)
        ?? layout.Monitors[0];

    /// <summary>Cursor position in physical pixels.</summary>
    public static (int X, int Y) Cursor() =>
        NativeMethods.GetCursorPos(out NativeMethods.POINT p) ? (p.X, p.Y) : (0, 0);
}
