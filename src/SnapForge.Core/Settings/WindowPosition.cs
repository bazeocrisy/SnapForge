using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.Core.Settings;

/// <summary>A remembered window position: top-left in physical virtual-desktop pixels plus the monitor it was on.</summary>
public sealed record WindowPosition(string MonitorDeviceName, int X, int Y)
{
    /// <summary>
    /// Where a window of the given size should open. A remembered position is kept if the window still fits on a
    /// monitor's work area; otherwise it is moved fully onto the nearest work area (or the primary monitor if its
    /// monitor is gone). Null = centre of the primary work area.
    /// </summary>
    public static PixelRect Clamp(ScreenLayout layout, WindowPosition? saved, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(layout);
        MonitorInfo primary = layout.Monitors[0];
        if (saved is null)
        {
            return Fit(primary.WorkArea, primary.WorkArea.X + ((primary.WorkArea.Width - width) / 2), primary.WorkArea.Y + ((primary.WorkArea.Height - height) / 2), width, height);
        }

        MonitorInfo target = layout.Monitors.FirstOrDefault(m => string.Equals(m.DeviceName, saved.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
            ?? layout.MonitorAt(saved.X, saved.Y)
            ?? primary;
        return Fit(target.WorkArea, saved.X, saved.Y, width, height);
    }

    private static PixelRect Fit(PixelRect area, int x, int y, int width, int height)
    {
        int w = Math.Min(width, area.Width);
        int h = Math.Min(height, area.Height);
        return new PixelRect(Math.Clamp(x, area.X, area.Right - w), Math.Clamp(y, area.Y, area.Bottom - h), w, h);
    }
}
