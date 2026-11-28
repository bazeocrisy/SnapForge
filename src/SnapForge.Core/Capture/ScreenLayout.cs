using SnapForge.Core.Geometry;

namespace SnapForge.Core.Capture;

/// <summary>
/// The monitor arrangement at capture time. The virtual desktop is the bounding box of all monitors;
/// its origin can be negative (a monitor to the left of / above the primary).
/// </summary>
public sealed class ScreenLayout
{
    public ScreenLayout(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        // Stable order: primary first, then left-to-right, top-to-bottom. Matches how users count displays.
        Monitors = monitors
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .ToArray();
        VirtualBounds = PixelRect.Union(Monitors.Select(m => m.Bounds));
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public PixelRect VirtualBounds { get; }

    public MonitorInfo? MonitorAt(int x, int y) => Monitors.FirstOrDefault(m => m.Bounds.Contains(x, y));

    /// <summary>Converts a virtual-desktop rectangle into coordinates of a bitmap that captured <see cref="VirtualBounds"/>.</summary>
    public PixelRect ToBitmapSpace(PixelRect screenRect) => screenRect.Offset(-VirtualBounds.X, -VirtualBounds.Y);

    /// <summary>Clips a selection to the virtual desktop and converts it to bitmap space.</summary>
    public PixelRect ClipSelectionToBitmap(PixelRect screenSelection) =>
        ToBitmapSpace(screenSelection.Intersect(VirtualBounds));

    /// <summary>
    /// Virtual-desktop areas not covered by any monitor (non-rectangular arrangements). Their content in a
    /// screen copy is undefined, so they are filled with a neutral color after capture.
    /// </summary>
    public IReadOnlyList<PixelRect> UncoveredAreas()
    {
        // Row-band sweep: split the virtual desktop into horizontal bands at every monitor edge,
        // then within each band find gaps between monitor spans.
        var ys = Monitors.SelectMany(m => new[] { m.Bounds.Y, m.Bounds.Bottom })
            .Append(VirtualBounds.Y).Append(VirtualBounds.Bottom)
            .Distinct().Order().ToArray();
        var result = new List<PixelRect>();
        for (int i = 0; i < ys.Length - 1; i++)
        {
            int top = ys[i], bottom = ys[i + 1];
            var spans = Monitors
                .Where(m => m.Bounds.Y <= top && m.Bounds.Bottom >= bottom)
                .Select(m => (m.Bounds.X, m.Bounds.Right))
                .OrderBy(s => s.X)
                .ToList();
            int cursor = VirtualBounds.X;
            foreach ((int left, int right) in spans)
            {
                if (left > cursor)
                {
                    result.Add(PixelRect.FromLtrb(cursor, top, left, bottom));
                }

                cursor = Math.Max(cursor, right);
            }

            if (cursor < VirtualBounds.Right)
            {
                result.Add(PixelRect.FromLtrb(cursor, top, VirtualBounds.Right, bottom));
            }
        }

        return result;
    }
}
