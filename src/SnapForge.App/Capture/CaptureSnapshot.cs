using SnapForge.Core.Capture;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Capture;

/// <summary>
/// Everything captured at one instant: the whole virtual desktop, the monitor layout and the visible
/// windows in z-order. Region/window/monitor selection then works on this frozen frame, so what the user
/// selects is exactly what they saw.
/// </summary>
internal sealed record CaptureSnapshot(ScreenLayout Layout, PixelBuffer Image, IReadOnlyList<WindowCandidate> Windows, DateTime CapturedAt);
