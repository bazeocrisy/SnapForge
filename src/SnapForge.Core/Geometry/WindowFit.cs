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

    /// <summary>True if the title-bar strip (top <paramref name="captionHeight"/> pixels) is fully inside the work area.</summary>
    public static bool CaptionVisible(PixelRect window, PixelRect workArea, int captionHeight) =>
        window.X >= workArea.X && window.Right <= workArea.Right && window.Y >= workArea.Y && window.Y + captionHeight <= workArea.Bottom;
}
