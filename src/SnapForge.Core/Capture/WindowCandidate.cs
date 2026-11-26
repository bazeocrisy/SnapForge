using SnapForge.Core.Geometry;

namespace SnapForge.Core.Capture;

/// <summary>A top-level window that can be picked in window-capture mode.</summary>
/// <param name="Handle">Native window handle (opaque to Core).</param>
/// <param name="Title">Window caption, may be empty.</param>
/// <param name="Bounds">Visible frame bounds (DWM extended frame bounds, excluding the invisible resize border) in physical pixels.</param>
/// <param name="ZOrder">0 = topmost. Lower values are in front.</param>
public sealed record WindowCandidate(nint Handle, string Title, PixelRect Bounds, int ZOrder);
