namespace SnapForge.Core.Capture;

public enum CaptureMode
{
    /// <summary>Every monitor, composed into one image of the virtual desktop.</summary>
    AllScreens,

    /// <summary>One monitor chosen by clicking it.</summary>
    SingleScreen,

    /// <summary>A dragged rectangle; may span monitors.</summary>
    Region,

    /// <summary>A top-level window chosen by clicking it.</summary>
    Window,

    /// <summary>The window that was in front when the capture started (falls back to the topmost other window).</summary>
    ActiveWindow,
}
