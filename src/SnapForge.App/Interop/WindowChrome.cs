using System.Windows;
using System.Windows.Interop;
using static SnapForge.App.Interop.NativeMethods;

namespace SnapForge.App.Interop;

internal static unsafe class WindowChrome
{
    /// <summary>Dark title bar to match the app theme (Windows 10 20H1+ / Windows 11; ignored elsewhere).</summary>
    public static void UseDarkTitleBar(Window window, bool dark = true)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        int enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &enabled, sizeof(int));
    }

    /// <summary>
    /// Disables the DWM show/hide animation so the main window is gone from the screen immediately before a
    /// capture and overlays appear without a fade.
    /// </summary>
    public static void DisableTransitions(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        int disabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, &disabled, sizeof(int));
    }
}
