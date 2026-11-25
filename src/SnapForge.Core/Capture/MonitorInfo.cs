using SnapForge.Core.Geometry;

namespace SnapForge.Core.Capture;

/// <summary>A display as seen by a per-monitor-DPI-aware process. All rectangles are physical pixels.</summary>
/// <param name="DeviceName">Win32 device name, e.g. \\.\DISPLAY1.</param>
/// <param name="Bounds">Monitor rectangle in virtual-desktop coordinates.</param>
/// <param name="WorkArea">Monitor rectangle minus taskbars/app bars.</param>
/// <param name="IsPrimary">True for the primary display (origin 0,0).</param>
/// <param name="DpiScale">Effective DPI / 96 (1.0 = 100 %, 1.5 = 150 %).</param>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, double DpiScale)
{
    public string DisplayLabel(int index) =>
        $"Display {index + 1}{(IsPrimary ? " (primary)" : string.Empty)} — {Bounds.Width}×{Bounds.Height} at {DpiScale * 100:0}%";
}
