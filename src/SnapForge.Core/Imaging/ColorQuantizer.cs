using SnapForge.Core.Geometry;

namespace SnapForge.Core.Imaging;

/// <summary>
/// Reduces the colors of a BGRA region to a palette of at most N entries (used by <see cref="GifEncoder"/>).
/// </summary>
/// <remarks>
/// <para>
/// If the region has no more distinct colors than allowed, the palette is exactly those colors, so
/// mapping is lossless. Otherwise a variance-driven median cut runs over a 5-5-5-bit histogram: the box
/// with the largest squared error is split on its widest-variance axis at the weighted median, until the
/// palette is full or no box can be split. Each palette entry is the pixel-weighted mean of its box,
/// computed from the full 8-bit colors, so it is not limited to 5-bit precision.
/// </para>
/// <para>Alpha is ignored: palettes and mapping consider RGB only (screen frames are opaque).</para>
/// Colors are packed as 0xRRGGBB integers.
/// </remarks>
internal sealed class ColorQuantizer
{
    private const int HistogramSize = 1 << 15;

    private readonly long[] _count = new long[HistogramSize];
    private readonly long[] _sumR = new long[HistogramSize];
    private readonly long[] _sumG = new long[HistogramSize];
    private readonly long[] _sumB = new long[HistogramSize];
    private readonly ExactColorSet _distinct = new();

    /// <summary>Builds a palette for the given region of a BGRA buffer.</summary>
    /// <param name="exact">True when every color in the region is present in the palette.</param>
    public int[] BuildPalette(byte[] bgra, int stride, PixelRect rect, int maxColors, out bool exact)
    {
        if (TryCollectDistinct(bgra, stride, rect, maxColors, out int[]? distinct))
        {
            exact = true;
            return distinct;
        }

        exact = false;
        return MedianCut(bgra, stride, rect, maxColors);
    }

    private bool TryCollectDistinct(byte[] bgra, int stride, PixelRect rect, int maxColors, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out int[]? colors)
    {
        _distinct.Clear();
        int lastColor = -1;
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int i = y * stride + rect.X * 4;
            int end = i + rect.Width * 4;
            for (; i < end; i += 4)
            {
                int c = (bgra[i + 2] << 16) | (bgra[i + 1] << 8) | bgra[i];
                if (c == lastColor)
                {
                    continue;
                }

                lastColor = c;
                if (_distinct.Add(c) && _distinct.Count > maxColors)
                {
                    colors = null;
                    return false;
                }
            }
        }

        colors = _distinct.ToArray();
        Array.Sort(colors);
        return true;
    }

    private int[] MedianCut(byte[] bgra, int stride, PixelRect rect, int maxColors)
    {
        Array.Clear(_count);
        Array.Clear(_sumR);
        Array.Clear(_sumG);
        Array.Clear(_sumB);
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int i = y * stride + rect.X * 4;
            int end = i + rect.Width * 4;
            for (; i < end; i += 4)
            {
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                int bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                _count[bin]++;
                _sumR[bin] += r;
                _sumG[bin] += g;
                _sumB[bin] += b;
            }
        }

        var bins = new List<int>();
        for (int bin = 0; bin < HistogramSize; bin++)
        {
            if (_count[bin] != 0)
            {
                bins.Add(bin);
            }
        }

        int[] binArray = bins.ToArray();
        var boxes = new List<Box> { MakeBox(binArray, 0, binArray.Length) };
        while (boxes.Count < maxColors)
        {
            int best = -1;
            double bestError = 0;
            for (int k = 0; k < boxes.Count; k++)
            {
                if (boxes[k].Length > 1 && boxes[k].Error > bestError)
                {
                    bestError = boxes[k].Error;
                    best = k;
                }
            }

            if (best < 0)
            {
                break;
            }

            Box box = boxes[best];
            (Box first, Box second) = Split(binArray, box);
            boxes[best] = first;
            boxes.Add(second);
        }

        var palette = new int[boxes.Count];
        for (int k = 0; k < boxes.Count; k++)
        {
            Box box = boxes[k];
            long n = 0, sr = 0, sg = 0, sb = 0;
            for (int j = box.Start; j < box.Start + box.Length; j++)
            {
                int bin = binArray[j];
                n += _count[bin];
                sr += _sumR[bin];
                sg += _sumG[bin];
                sb += _sumB[bin];
            }

            int r = (int)((sr + n / 2) / n), g = (int)((sg + n / 2) / n), b = (int)((sb + n / 2) / n);
            palette[k] = (r << 16) | (g << 8) | b;
        }

        return palette;
    }

    private (Box First, Box Second) Split(int[] bins, Box box)
    {
        int axis = box.Axis;
        var span = bins.AsSpan(box.Start, box.Length);
        Comparison<int> byAxis = axis switch
        {
            0 => (a, b) => MeanR(a).CompareTo(MeanR(b)),
            1 => (a, b) => MeanG(a).CompareTo(MeanG(b)),
            _ => (a, b) => MeanB(a).CompareTo(MeanB(b)),
        };
        span.Sort(byAxis);

        long total = 0;
        foreach (int bin in span)
        {
            total += _count[bin];
        }

        long running = 0;
        int cut = 1;
        for (int j = 0; j < span.Length - 1; j++)
        {
            running += _count[span[j]];
            cut = j + 1;
            if (running * 2 >= total)
            {
                break;
            }
        }

        return (MakeBox(bins, box.Start, cut), MakeBox(bins, box.Start + cut, box.Length - cut));
    }

    private double MeanR(int bin) => (double)_sumR[bin] / _count[bin];
    private double MeanG(int bin) => (double)_sumG[bin] / _count[bin];
    private double MeanB(int bin) => (double)_sumB[bin] / _count[bin];

    private Box MakeBox(int[] bins, int start, int length)
    {
        // Squared error of the box around its mean, using each bin's mean color as its representative.
        double n = 0, sr = 0, sg = 0, sb = 0, qr = 0, qg = 0, qb = 0;
        for (int j = start; j < start + length; j++)
        {
            int bin = bins[j];
            double c = _count[bin];
            double r = MeanR(bin), g = MeanG(bin), b = MeanB(bin);
            n += c;
            sr += c * r;
            sg += c * g;
            sb += c * b;
            qr += c * r * r;
            qg += c * g * g;
            qb += c * b * b;
        }

        double vr = qr - sr * sr / n, vg = qg - sg * sg / n, vb = qb - sb * sb / n;
        int axis = vr >= vg && vr >= vb ? 0 : vg >= vb ? 1 : 2;
        return new Box(start, length, vr + vg + vb, axis);
    }

    private readonly record struct Box(int Start, int Length, double Error, int Axis);

    /// <summary>A tiny open-addressing set of 24-bit colors, sized for up to 257 entries.</summary>
    private sealed class ExactColorSet
    {
        private const int Capacity = 1024;
        private readonly int[] _slots = new int[Capacity];
        private readonly List<int> _items = new(257);

        public int Count => _items.Count;

        public void Clear()
        {
            Array.Clear(_slots);
            _items.Clear();
        }

        /// <summary>Adds a color; returns true if it was not present.</summary>
        public bool Add(int color)
        {
            int key = color + 1;
            int slot = (int)(((uint)color * 2654435761u) >> 22);
            while (true)
            {
                int existing = _slots[slot];
                if (existing == key)
                {
                    return false;
                }

                if (existing == 0)
                {
                    _slots[slot] = key;
                    _items.Add(color);
                    return true;
                }

                slot = (slot + 1) & (Capacity - 1);
            }
        }

        public int[] ToArray() => _items.ToArray();
    }
}

/// <summary>Maps 0xRRGGBB colors to the nearest entry of a palette, with a direct-mapped result cache.</summary>
internal sealed class PaletteMapper
{
    private const int CacheBits = 16;
    private readonly int[] _cacheKeys = new int[1 << CacheBits];
    private readonly byte[] _cacheValues = new byte[1 << CacheBits];
    private int[] _palette = [];

    public int[] Palette => _palette;

    public void SetPalette(int[] palette)
    {
        if (palette.Length is 0 or > 256)
        {
            throw new ArgumentException("A palette has 1 to 256 colors.", nameof(palette));
        }

        _palette = palette;
        Array.Clear(_cacheKeys);
    }

    /// <summary>Returns the index of the palette entry nearest to the color (exact when present).</summary>
    public byte Map(int color)
    {
        int slot = (int)(((uint)color * 2654435761u) >> (32 - CacheBits));
        int key = color | (1 << 24);
        if (_cacheKeys[slot] == key)
        {
            return _cacheValues[slot];
        }

        byte index = FindNearest(color);
        _cacheKeys[slot] = key;
        _cacheValues[slot] = index;
        return index;
    }

    private byte FindNearest(int color)
    {
        int r = (color >> 16) & 0xFF, g = (color >> 8) & 0xFF, b = color & 0xFF;
        int best = 0;
        int bestDistance = int.MaxValue;
        int[] palette = _palette;
        for (int k = 0; k < palette.Length; k++)
        {
            int p = palette[k];
            int dr = ((p >> 16) & 0xFF) - r, dg = ((p >> 8) & 0xFF) - g, db = (p & 0xFF) - b;

            // Weighted Euclidean distance (green matters most to the eye, blue least).
            int distance = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = k;
                if (distance == 0)
                {
                    break;
                }
            }
        }

        return (byte)best;
    }
}
