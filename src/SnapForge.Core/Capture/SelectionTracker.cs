using SnapForge.Core.Geometry;

namespace SnapForge.Core.Capture;

/// <summary>
/// The capture-overlay selection logic, independent of any UI toolkit, so it can be unit tested.
/// All coordinates are physical virtual-desktop pixels (what GetCursorPos returns in a per-monitor-v2 process).
/// </summary>
/// <remarks>
/// Regression guard: in the first MVP build the overlay window released mouse capture before evaluating
/// mouse-up. Releasing capture raises "capture lost" synchronously, and the old logic treated that as an
/// abandoned drag, so a region selection could never complete. This tracker is order-independent: losing
/// capture after the button was released completes the drag exactly like a button-up does, and only a
/// capture loss with the button still held (Alt+Tab, a system dialog) abandons it.
/// </remarks>
public sealed class SelectionTracker
{
    /// <summary>Smallest region (in pixels, each side) accepted as a deliberate drag.</summary>
    public const int MinimumRegionSize = 2;

    private readonly CaptureMode _mode;
    private readonly ScreenLayout _layout;
    private readonly IReadOnlyList<WindowCandidate> _windows;
    private (int X, int Y)? _dragStart;

    public SelectionTracker(CaptureMode mode, ScreenLayout layout, IReadOnlyList<WindowCandidate> windows)
    {
        if (mode == CaptureMode.AllScreens)
        {
            throw new ArgumentException("All-screens capture has no interactive selection.", nameof(mode));
        }

        _mode = mode;
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
    }

    /// <summary>Raised once when the selection completes or is cancelled.</summary>
    public event EventHandler? Finished;

    public CaptureMode Mode => _mode;

    /// <summary>The rectangle to show highlighted (drag rectangle, hovered window or hovered display).</summary>
    public PixelRect? Highlight { get; private set; }

    public bool IsDragging => _dragStart is not null;

    public bool IsFinished { get; private set; }

    /// <summary>The chosen rectangle once finished; null if cancelled.</summary>
    public PixelRect? Result { get; private set; }

    public void PointerDown(int x, int y)
    {
        if (IsFinished)
        {
            return;
        }

        if (_mode == CaptureMode.Region)
        {
            _dragStart = (x, y);
        }

        Update(x, y);
    }

    public void PointerMove(int x, int y)
    {
        if (!IsFinished)
        {
            Update(x, y);
        }
    }

    public void PointerUp(int x, int y)
    {
        if (IsFinished)
        {
            return;
        }

        Update(x, y);
        switch (_mode)
        {
            case CaptureMode.Region:
                if (_dragStart is not null && Highlight is { } region && region.Width >= MinimumRegionSize && region.Height >= MinimumRegionSize)
                {
                    Finish(region);
                }
                else
                {
                    // A click without a drag: stay open and let the user try again.
                    _dragStart = null;
                    Update(x, y);
                }

                break;
            default:
                if (Highlight is { } picked)
                {
                    Finish(picked);
                }

                break;
        }
    }

    /// <summary>The overlay lost mouse capture (released by itself, Alt+Tab, a dialog, ...).</summary>
    /// <param name="x">Cursor X at the time.</param>
    /// <param name="y">Cursor Y at the time.</param>
    /// <param name="buttonStillPressed">Whether the left button is still held down.</param>
    public void CaptureLost(int x, int y, bool buttonStillPressed)
    {
        if (IsFinished || _dragStart is null)
        {
            return;
        }

        if (buttonStillPressed)
        {
            // Something else took the mouse mid-drag: abandon rather than guess.
            _dragStart = null;
            Update(x, y);
        }
        else
        {
            // Capture was lost because the button went up: that is a completed drag.
            PointerUp(x, y);
        }
    }

    public void Cancel()
    {
        if (!IsFinished)
        {
            Finish(null);
        }
    }

    private void Update(int x, int y)
    {
        PixelRect? highlight = _mode switch
        {
            CaptureMode.Region when _dragStart is { } s =>
                PixelRect.FromLtrb(Math.Min(s.X, x), Math.Min(s.Y, y), Math.Max(s.X, x) + 1, Math.Max(s.Y, y) + 1)
                    .Intersect(_layout.VirtualBounds),
            CaptureMode.Window => WindowPicker.WindowAt(_windows, x, y) is { } w ? WindowPicker.CaptureRegionFor(w, _layout) : null,
            CaptureMode.SingleScreen => _layout.MonitorAt(x, y)?.Bounds,
            _ => null,
        };
        Highlight = highlight is { IsEmpty: false } ? highlight : null;
    }

    private void Finish(PixelRect? result)
    {
        IsFinished = true;
        _dragStart = null;
        Result = result;
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
