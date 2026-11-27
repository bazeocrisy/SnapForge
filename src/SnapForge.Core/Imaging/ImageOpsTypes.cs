namespace SnapForge.Core.Imaging;

/// <summary>Image dimensions in pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}

/// <summary>Where the existing image is placed when the canvas is resized (<see cref="ImageOps.ResizeCanvas"/>).</summary>
public enum CanvasAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>
/// Result of an operation that changes the canvas: the new image and the position of the old image's top-left
/// corner in it (negative when the old image was cut off on that side).
/// </summary>
public readonly record struct CanvasResult(PixelBuffer Image, int OffsetX, int OffsetY);

/// <summary>How <see cref="ImageOps.Combine"/> lays images out.</summary>
public enum CombineDirection
{
    /// <summary>Side by side, left to right.</summary>
    Horizontal,

    /// <summary>Stacked, top to bottom.</summary>
    Vertical,
}

/// <summary>Cross-axis alignment for <see cref="ImageOps.Combine"/> (top/middle/bottom when horizontal, left/center/right when vertical).</summary>
public enum CombineAlignment
{
    Start,
    Center,
    End,
}
