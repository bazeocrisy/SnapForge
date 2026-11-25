using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SnapForge.App.Interop;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using static SnapForge.App.Interop.NativeMethods;

namespace SnapForge.App.Capture;

/// <summary>
/// GDI-based screen capture. BitBlt from the screen DC returns the DWM-composed desktop, works on every
/// supported Windows version, needs no capture permission prompt and draws no capture border.
/// The process is per-monitor-DPI-aware (see app.manifest), so all coordinates are physical pixels.
/// </summary>
internal static unsafe class ScreenCaptureService
{
    private const int MaxWindows = 4096;

    public static CaptureSnapshot TakeSnapshot()
    {
        ScreenLayout layout = GetScreenLayout();
        IReadOnlyList<WindowCandidate> windows = EnumerateWindows(Environment.ProcessId);
        PixelBuffer image = CaptureRegion(layout.VirtualBounds);

        // Parts of the virtual desktop not covered by any monitor have undefined content; make them black.
        foreach (PixelRect gap in layout.UncoveredAreas())
        {
            image.FillRect(layout.ToBitmapSpace(gap), ArgbColor.Black);
        }

        return new CaptureSnapshot(layout, image, windows, DateTime.Now);
    }

    public static ScreenLayout GetScreenLayout()
    {
        var monitors = new List<MonitorInfo>();
        GCHandle handle = GCHandle.Alloc(monitors);
        try
        {
            if (!EnumDisplayMonitors(0, 0, &MonitorEnumProc, GCHandle.ToIntPtr(handle)))
            {
                throw new Win32Exception("EnumDisplayMonitors failed.");
            }
        }
        finally
        {
            handle.Free();
        }

        if (monitors.Count == 0)
        {
            // Should not happen, but never leave the user without a capture: fall back to the virtual-screen metrics.
            var bounds = new PixelRect(
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_CYVIRTUALSCREEN));
            monitors.Add(new MonitorInfo("DISPLAY", bounds, bounds, true, 1.0));
        }

        return new ScreenLayout(monitors);
    }

    /// <summary>Copies a rectangle of the screen (virtual-desktop coordinates) into an opaque BGRA buffer.</summary>
    public static PixelBuffer CaptureRegion(PixelRect screenRect)
    {
        if (screenRect.IsEmpty)
        {
            throw new ArgumentException("Capture region is empty.", nameof(screenRect));
        }

        int width = screenRect.Width, height = screenRect.Height;
        nint screenDc = 0, memDc = 0, bitmap = 0, previous = 0;
        try
        {
            screenDc = GetDC(0);
            if (screenDc == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not access the screen (GetDC).");
            }

            memDc = CreateCompatibleDC(screenDc);
            if (memDc == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateCompatibleDC failed.");
            }

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // negative = top-down rows, matching PixelBuffer
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };
            void* bits;
            bitmap = CreateDIBSection(screenDc, &header, DIB_RGB_COLORS, &bits, 0, 0);
            if (bitmap == 0 || bits == null)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not allocate a {width}×{height} capture bitmap.");
            }

            previous = SelectObject(memDc, bitmap);

            // CAPTUREBLT includes layered (translucent) windows in the copy.
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, screenRect.X, screenRect.Y, SRCCOPY | CAPTUREBLT))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "BitBlt from the screen failed.");
            }

            GdiFlush();
            var buffer = new PixelBuffer(width, height);
            new ReadOnlySpan<byte>(bits, buffer.Pixels.Length).CopyTo(buffer.Pixels);
            buffer.ForceOpaque();
            return buffer;
        }
        finally
        {
            if (previous != 0)
            {
                SelectObject(memDc, previous);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            if (memDc != 0)
            {
                DeleteDC(memDc);
            }

            if (screenDc != 0)
            {
                _ = ReleaseDC(0, screenDc);
            }
        }
    }

    /// <summary>Visible, uncloaked, non-minimized top-level windows, front to back, excluding our own process.</summary>
    public static IReadOnlyList<WindowCandidate> EnumerateWindows(int excludeProcessId)
    {
        var result = new List<WindowCandidate>();
        int z = 0;
        for (nint hwnd = GetTopWindow(0); hwnd != 0 && z < MaxWindows; hwnd = GetWindow(hwnd, GW_HWNDNEXT), z++)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd))
            {
                continue;
            }

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)excludeProcessId)
            {
                continue;
            }

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TRANSPARENT) != 0)
            {
                // Click-through overlays (game/GPU overlays, notifications) would otherwise swallow every pick.
                continue;
            }

            PixelRect bounds = GetVisibleBounds(hwnd);
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                continue;
            }

            result.Add(new WindowCandidate(hwnd, GetTitle(hwnd), bounds, z));
        }

        return result;
    }

    public static (int X, int Y) GetCursorPosition()
    {
        return GetCursorPos(out POINT p) ? (p.X, p.Y) : (0, 0);
    }

    private static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static PixelRect GetVisibleBounds(nint hwnd)
    {
        RECT r;
        // Extended frame bounds exclude the invisible resize borders Windows 10/11 add around windows.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT)) != 0 && !GetWindowRect(hwnd, out r))
        {
            return PixelRect.Empty;
        }

        return PixelRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom);
    }

    private static string GetTitle(nint hwnd)
    {
        const int capacity = 256;
        char* buffer = stackalloc char[capacity];
        int length = GetWindowText(hwnd, buffer, capacity);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    [UnmanagedCallersOnly]
    private static int MonitorEnumProc(nint hMonitor, nint hdc, RECT* clip, nint data)
    {
        try
        {
            var monitors = (List<MonitorInfo>)GCHandle.FromIntPtr(data).Target!;
            var info = new MONITORINFOEXW { cbSize = sizeof(MONITORINFOEXW) };
            if (!GetMonitorInfo(hMonitor, &info))
            {
                return 1; // skip, keep enumerating
            }

            double scale = 1.0;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / 96.0;
            }

            string device = new(info.szDevice);
            monitors.Add(new MonitorInfo(
                device,
                PixelRect.FromLtrb(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                PixelRect.FromLtrb(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                scale));
            return 1;
        }
#pragma warning disable CA1031 // An exception must never unwind through a native callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Debug.WriteLine(ex);
            return 0;
        }
    }
}
