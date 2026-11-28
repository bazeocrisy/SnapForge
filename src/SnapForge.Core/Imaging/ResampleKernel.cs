namespace SnapForge.Core.Imaging;

/// <summary>
/// One-dimensional resampling weights for scaling a span of source pixels onto <c>n</c> output pixels (used by the
/// camera overlay). Downscaling uses an <b>area average</b> (box filter as wide as one output pixel): every source
/// pixel contributes by how much of it the output pixel covers, so fine detail averages to its mean instead of
/// aliasing into grain the way two-tap bilinear sampling does when it skips source pixels. Upscaling (or 1:1) uses
/// bilinear interpolation (an area average there would look blocky). Weights are fixed point and sum to exactly
/// <see cref="One"/> for every output pixel, so a flat colour stays exactly that colour.
/// </summary>
public sealed class ResampleKernel
{
    /// <summary>Fixed-point 1.0 of a weight (12 bits).</summary>
    public const int One = 4096;

    private ResampleKernel(int[] start, int[] count, int[] index, int[] weight, bool area)
    {
        Start = start;
        Count = count;
        Index = index;
        Weight = weight;
        IsAreaAverage = area;
    }

    /// <summary>Offset of output pixel i's taps in <see cref="Index"/> / <see cref="Weight"/>.</summary>
    public int[] Start { get; }

    /// <summary>Number of taps of output pixel i.</summary>
    public int[] Count { get; }

    /// <summary>Source pixel of each tap (0 ≤ index &lt; limit).</summary>
    public int[] Index { get; }

    /// <summary>Weight of each tap (fixed point, <see cref="One"/> = 1).</summary>
    public int[] Weight { get; }

    /// <summary>True when the kernel averages areas (downscaling), false for bilinear (upscaling or 1:1).</summary>
    public bool IsAreaAverage { get; }

    public int Length => Start.Length;

    /// <summary>The largest number of taps of any output pixel.</summary>
    public int MaxTaps => Count.Length == 0 ? 0 : Count.Max();

    /// <summary>
    /// Weights mapping source coordinates [<paramref name="start"/>, <paramref name="start"/> + <paramref name="length"/>)
    /// (pixel units, may be fractional) onto <paramref name="n"/> output pixels; <paramref name="limit"/> is the source
    /// size (taps are clamped inside it). <paramref name="mirror"/> reverses the output order (mirrored camera).
    /// </summary>
    public static ResampleKernel Create(int n, double start, double length, int limit, bool mirror)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        double scale = length / n;
        bool area = scale > 1.0 + 1e-9;
        var starts = new int[n];
        var counts = new int[n];
        var index = new List<int>(n * (area ? (int)Math.Ceiling(scale) + 1 : 2));
        var weight = new List<int>(index.Capacity);
        var raw = new List<(int Index, double Weight)>();
        for (int i = 0; i < n; i++)
        {
            int k = mirror ? n - 1 - i : i;
            raw.Clear();
            if (area)
            {
                double a = start + (k * scale), b = a + scale;
                int first = (int)Math.Floor(a), last = (int)Math.Ceiling(b) - 1;
                for (int s = first; s <= last; s++)
                {
                    double covered = Math.Min(b, s + 1) - Math.Max(a, s);
                    if (covered > 1e-9)
                    {
                        raw.Add((Math.Clamp(s, 0, limit - 1), covered));
                    }
                }
            }
            else
            {
                double s = Math.Clamp(start + ((k + 0.5) * scale) - 0.5, 0, limit - 1);
                int a = (int)Math.Floor(s);
                double f = s - a;
                raw.Add((a, 1 - f));
                if (f > 1e-9)
                {
                    raw.Add((Math.Min(limit - 1, a + 1), f));
                }
            }

            starts[i] = index.Count;
            AddNormalized(raw, index, weight);
            counts[i] = index.Count - starts[i];
        }

        return new ResampleKernel(starts, counts, [.. index], [.. weight], area);
    }

    /// <summary>Merges taps on the same source pixel and quantizes the weights so they sum to exactly <see cref="One"/>.</summary>
    private static void AddNormalized(List<(int Index, double Weight)> raw, List<int> index, List<int> weight)
    {
        var merged = raw.GroupBy(t => t.Index).Select(g => (Index: g.Key, Weight: g.Sum(t => t.Weight))).Where(t => t.Weight > 0).OrderBy(t => t.Index).ToList();
        double total = merged.Sum(t => t.Weight);
        int first = index.Count;
        int sum = 0, largest = first;
        foreach ((int i, double w) in merged)
        {
            int q = (int)Math.Round(w / total * One);
            index.Add(i);
            weight.Add(q);
            sum += q;
            if (q > weight[largest])
            {
                largest = weight.Count - 1;
            }
        }

        weight[largest] += One - sum; // exact sum: a flat colour stays exactly flat
    }
}
