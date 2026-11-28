namespace SnapForge.Core.Geometry;

/// <summary>
/// FA-003 / SF-007: keeps a window's frame (title bar, minimize/maximize/close buttons and borders) inside the usable
/// area of its monitor. All values are physical pixels, so mixed-DPI monitors and Windows scaling (e.g. a 1280×820 DIP
/// window on a 1920×1080 display at 150 % = 1920×1230 px, taller than the 1032-px work area) are handled exactly.
/// </summary>
public static class WindowFit
{
    /// <summary>
    /// Returns the bounds the window should have: no larger than <paramref name="workArea"/> and positioned so it lies
    /// entirely inside it (the top edge, which holds the title bar, wins when both edges cannot fit). Returns the
    /// input unchanged when it already fits.
    /// </summary>
    public static PixelRect Fit(PixelRect window, PixelRect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0 || window.Width <= 0 || window.Height <= 0)
        {
            return window;
        }

        int w = Math.Min(window.Width, workArea.Width);
        int h = Math.Min(window.Height, workArea.Height);
        int x = Math.Clamp(window.X, workArea.X, workArea.Right - w);
        int y = Math.Clamp(window.Y, workArea.Y, workArea.Bottom - h);
        return new PixelRect(x, y, w, h);
    }

    /// <summary>
    /// RC3 camera preview: keeps a small floating window fully inside the usable area of the monitor it mostly lies on
    /// (or, if it is off every monitor, the work area whose centre is nearest), whatever the monitor layout or scaling.
    /// </summary>
    public static PixelRect KeepOnScreen(PixelRect window, IReadOnlyList<PixelRect> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        if (workAreas.Count == 0)
        {
            return window;
        }

        PixelRect best = workAreas[0];
        long bestOverlap = -1;
        foreach (PixelRect area in workAreas)
        {
            long overlap = window.Intersect(area).Area;
            if (overlap > bestOverlap)
            {
                best = area;
                bestOverlap = overlap;
            }
        }

        if (bestOverlap <= 0)
        {
            long cx = window.X + (window.Width / 2), cy = window.Y + (window.Height / 2);
            best = workAreas.OrderBy(a => Square(a.X + (a.Width / 2) - cx) + Square(a.Y + (a.Height / 2) - cy)).First();
        }

        return Fit(window, best);
    }

    /// <summary>Bottom-right corner of a work area, <paramref name="margin"/> pixels in from both edges.</summary>
    public static PixelRect BottomRight(PixelRect workArea, int width, int height, int margin) =>
        Fit(new PixelRect(workArea.Right - width - margin, workArea.Bottom - height - margin, width, height), workArea);

    private static long Square(long v) => v * v;

    /// <summary>True if the title-bar strip (top <paramref name="captionHeight"/> pixels) is fully inside the work area.</summary>
    public static bool CaptionVisible(PixelRect window, PixelRect workArea, int captionHeight) =>
        window.X >= workArea.X && window.Right <= workArea.Right && window.Y >= workArea.Y && window.Y + captionHeight <= workArea.Bottom;
}
