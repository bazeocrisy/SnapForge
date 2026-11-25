using SnapForge.Core.Geometry;

namespace SnapForge.Core.Capture;

/// <summary>Chooses which window the cursor is over, using the z-order captured with the screenshot.</summary>
public static class WindowPicker
{
    public static WindowCandidate? WindowAt(IEnumerable<WindowCandidate> windows, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(windows);
        return windows
            .Where(w => !w.Bounds.IsEmpty && w.Bounds.Contains(x, y))
            .OrderBy(w => w.ZOrder)
            .FirstOrDefault();
    }

    /// <summary>
    /// The region of the frozen screenshot to keep for a picked window: its frame clipped to the
    /// virtual desktop (maximized windows overhang the monitor by a few pixels; off-screen parts do not exist).
    /// </summary>
    public static PixelRect CaptureRegionFor(WindowCandidate window, ScreenLayout layout)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);
        return window.Bounds.Intersect(layout.VirtualBounds);
    }
}
