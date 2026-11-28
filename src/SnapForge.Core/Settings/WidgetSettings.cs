using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.Core.Settings;

/// <summary>Screen edge the desktop widget is docked to. Bottom is not offered (taskbar).</summary>
public enum DockEdge
{
    Top,
    Left,
    Right,
}

/// <summary>How the collapsed widget opens.</summary>
public enum WidgetExpandMode
{
    /// <summary>Opens when the pointer rests on it (and on click).</summary>
    Hover,

    /// <summary>Opens only when clicked.</summary>
    Click,
}

/// <summary>
/// Where the widget sits: a monitor (by Win32 device name), an edge and a position along that edge as a fraction
/// 0–1 of the edge length (0.5 = centred). Stored as a fraction so it survives resolution and DPI changes.
/// </summary>
public sealed record WidgetPlacement(string MonitorDeviceName, DockEdge Edge, double Offset);

/// <summary>Desktop capture widget preferences (owner decision 2026-11-25: enabled and topmost by default).</summary>
public sealed record WidgetSettings
{
    public bool Enabled { get; init; } = true;

    public bool Topmost { get; init; } = true;

    public WidgetExpandMode ExpandMode { get; init; } = WidgetExpandMode.Hover;

    /// <summary>When on, the collapsed tab shrinks to a thin strip at the screen edge until the pointer touches it.</summary>
    public bool AutoHide { get; init; }

    /// <summary>Null = default (top centre of the primary monitor).</summary>
    public WidgetPlacement? Placement { get; init; }
}

/// <summary>Pure placement math for the widget (physical pixels, virtual-desktop coordinates).</summary>
public static class WidgetDock
{
    /// <summary>The monitor the placement refers to; falls back to the primary monitor if that display is gone.</summary>
    public static MonitorInfo ResolveMonitor(ScreenLayout layout, WidgetPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return (placement is null ? null : layout.Monitors.FirstOrDefault(m => string.Equals(m.DeviceName, placement.MonitorDeviceName, StringComparison.OrdinalIgnoreCase)))
            ?? layout.Monitors[0]; // ScreenLayout orders the primary first
    }

    /// <summary>
    /// The widget rectangle for a placement. <paramref name="along"/> is the widget's extent along the edge and
    /// <paramref name="across"/> its extent away from the edge, both in physical pixels for the target monitor.
    /// The rectangle is always fully inside that monitor's bounds and touches the docked edge.
    /// </summary>
    public static PixelRect Place(ScreenLayout layout, WidgetPlacement? placement, int along, int across)
    {
        MonitorInfo monitor = ResolveMonitor(layout, placement);
        DockEdge edge = placement?.Edge ?? DockEdge.Top;
        double offset = Math.Clamp(placement?.Offset ?? 0.5, 0, 1);
        if (double.IsNaN(offset))
        {
            offset = 0.5;
        }

        PixelRect b = monitor.Bounds;
        if (edge == DockEdge.Top)
        {
            int w = Math.Min(along, b.Width);
            int h = Math.Min(across, b.Height);
            int x = b.X + (int)Math.Round((offset * b.Width) - (w / 2.0));
            x = Math.Clamp(x, b.X, b.Right - w);
            return new PixelRect(x, b.Y, w, h);
        }

        int ww = Math.Min(across, b.Width);
        int hh = Math.Min(along, b.Height);
        int y = b.Y + (int)Math.Round((offset * b.Height) - (hh / 2.0));
        y = Math.Clamp(y, b.Y, b.Bottom - hh);
        int xx = edge == DockEdge.Left ? b.X : b.Right - ww;
        return new PixelRect(xx, y, ww, hh);
    }

    /// <summary>
    /// The placement for a widget dropped with its centre at (<paramref name="x"/>, <paramref name="y"/>): the
    /// monitor under (or nearest to) the point, its nearest top/left/right edge, and the offset along that edge.
    /// </summary>
    public static WidgetPlacement FromPoint(ScreenLayout layout, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(layout);
        MonitorInfo monitor = layout.MonitorAt(x, y) ?? layout.Monitors.OrderBy(m => DistanceSquared(m.Bounds, x, y)).First();
        PixelRect b = monitor.Bounds;
        int cx = Math.Clamp(x, b.X, b.Right - 1);
        int cy = Math.Clamp(y, b.Y, b.Bottom - 1);
        long toTop = cy - b.Y;
        long toLeft = cx - b.X;
        long toRight = b.Right - 1 - cx;
        DockEdge edge = toTop <= toLeft && toTop <= toRight ? DockEdge.Top : toLeft <= toRight ? DockEdge.Left : DockEdge.Right;
        double offset = edge == DockEdge.Top
            ? (cx - b.X + 0.5) / b.Width
            : (cy - b.Y + 0.5) / b.Height;
        return new WidgetPlacement(monitor.DeviceName, edge, Math.Round(Math.Clamp(offset, 0, 1), 4));
    }

    private static long DistanceSquared(PixelRect r, int x, int y)
    {
        long dx = x < r.X ? r.X - x : x >= r.Right ? x - r.Right + 1 : 0;
        long dy = y < r.Y ? r.Y - y : y >= r.Bottom ? y - r.Bottom + 1 : 0;
        return (dx * dx) + (dy * dy);
    }
}
