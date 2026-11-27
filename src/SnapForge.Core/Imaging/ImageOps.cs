using System.Runtime.InteropServices;
using SnapForge.Core.Geometry;

namespace SnapForge.Core.Imaging;

/// <summary>
/// Whole-image and region operations on <see cref="PixelBuffer"/>s (straight, non-premultiplied BGRA).
/// Every method returns a new buffer and leaves its inputs untouched.
/// Operations that average pixels (resize, blur, pixelate, shadows) work on premultiplied values internally, so
/// the color of fully transparent pixels never bleeds into visible ones.
/// </summary>
public static class ImageOps
{
    /// <summary>Largest supported width or height of a result image.</summary>
    public const int MaxDimension = 32768;

    /// <summary>Largest supported pixel count (width × height) of a result image.</summary>
    public const long MaxPixelCount = 400_000_000;

    /// <summary>Largest supported blur radius.</summary>
    public const int MaxBlurRadius = 1024;

    // ───────────────────────────── Lossless transforms ─────────────────────────────

    /// <summary>Rotates the image a quarter turn clockwise. The result is <c>Height × Width</c>.</summary>
    public static PixelBuffer Rotate90Clockwise(PixelBuffer source) => Transform(source, ImageTransform.Rotate90);

    /// <summary>Rotates the image a quarter turn counter-clockwise. The result is <c>Height × Width</c>.</summary>
    public static PixelBuffer Rotate90CounterClockwise(PixelBuffer source) => Transform(source, ImageTransform.Rotate270);

    /// <summary>Rotates the image a half turn.</summary>
    public static PixelBuffer Rotate180(PixelBuffer source) => Transform(source, ImageTransform.Rotate180);

    /// <summary>Mirrors the image left to right.</summary>
    public static PixelBuffer FlipHorizontal(PixelBuffer source) => Transform(source, ImageTransform.FlipHorizontal);

    /// <summary>Mirrors the image top to bottom.</summary>
    public static PixelBuffer FlipVertical(PixelBuffer source) => Transform(source, ImageTransform.FlipVertical);

    /// <summary>Applies a lossless transform: every source pixel appears exactly once in the result, unchanged.</summary>
    public static PixelBuffer Transform(PixelBuffer source, ImageTransform transform)
    {
        ArgumentNullException.ThrowIfNull(source);
        int w = source.Width, h = source.Height;
        PixelSize size = TransformedSize(w, h, transform);
        var result = new PixelBuffer(size.Width, size.Height);
        ReadOnlySpan<uint> src = MemoryMarshal.Cast<byte, uint>(source.Pixels.AsSpan());
        Span<uint> dst = MemoryMarshal.Cast<byte, uint>(result.Pixels.AsSpan());
        switch (transform)
        {
            case ImageTransform.Rotate90:
                // (x, y) -> (h - 1 - y, x); destination width is h.
                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w, dstCol = h - 1 - y;
                    for (int x = 0; x < w; x++)
                    {
                        dst[x * h + dstCol] = src[srcRow + x];
                    }
                }

                break;
            case ImageTransform.Rotate270:
                // (x, y) -> (y, w - 1 - x); destination width is h.
                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        dst[(w - 1 - x) * h + y] = src[srcRow + x];
                    }
                }

                break;
            case ImageTransform.Rotate180:
                for (int y = 0; y < h; y++)
                {
                    ReadOnlySpan<uint> row = src.Slice(y * w, w);
                    Span<uint> target = dst.Slice((h - 1 - y) * w, w);
                    row.CopyTo(target);
                    target.Reverse();
                }

                break;
            case ImageTransform.FlipHorizontal:
                for (int y = 0; y < h; y++)
                {
                    Span<uint> target = dst.Slice(y * w, w);
                    src.Slice(y * w, w).CopyTo(target);
                    target.Reverse();
                }

                break;
            case ImageTransform.FlipVertical:
                for (int y = 0; y < h; y++)
                {
                    src.Slice(y * w, w).CopyTo(dst.Slice((h - 1 - y) * w, w));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transform), transform, "Unknown image transform.");
        }

        return result;
    }

    /// <summary>Size of a <paramref name="width"/> × <paramref name="height"/> image after <paramref name="transform"/>.</summary>
    public static PixelSize TransformedSize(int width, int height, ImageTransform transform) =>
        transform is ImageTransform.Rotate90 or ImageTransform.Rotate270 ? new PixelSize(height, width) : new PixelSize(width, height);

    /// <summary>
    /// Maps a point in continuous image coordinates (pixel (x, y) spans [x, x+1) × [y, y+1)) of a
    /// <paramref name="width"/> × <paramref name="height"/> image to the same content position after <paramref name="transform"/>.
    /// </summary>
    public static PointD TransformPoint(PointD point, int width, int height, ImageTransform transform) => transform switch
    {
        ImageTransform.Rotate90 => new PointD(height - point.Y, point.X),
        ImageTransform.Rotate180 => new PointD(width - point.X, height - point.Y),
        ImageTransform.Rotate270 => new PointD(point.Y, width - point.X),
        ImageTransform.FlipHorizontal => new PointD(width - point.X, point.Y),
        ImageTransform.FlipVertical => new PointD(point.X, height - point.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(transform), transform, "Unknown image transform."),
    };

    /// <summary>Maps a rectangle exactly (all supported transforms keep rectangles axis-aligned).</summary>
    public static RectD TransformRect(RectD rect, int width, int height, ImageTransform transform) =>
        RectD.FromPoints(
            TransformPoint(new PointD(rect.X, rect.Y), width, height, transform),
            TransformPoint(new PointD(rect.Right, rect.Bottom), width, height, transform));

    /// <summary>Maps a pixel rectangle exactly: the result covers precisely the transformed pixels.</summary>
    public static PixelRect TransformRect(PixelRect rect, int width, int height, ImageTransform transform)
    {
        RectD r = TransformRect(new RectD(rect.X, rect.Y, rect.Width, rect.Height), width, height, transform);
        return new PixelRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
    }

    // ───────────────────────────── Resize ─────────────────────────────

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> unless a <paramref name="width"/> × <paramref name="height"/>
    /// result is supported: 1..<see cref="MaxDimension"/> per side and at most <see cref="MaxPixelCount"/> pixels.
    /// </summary>
    public static void ValidateSize(long width, long height)
    {
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension || width * height > MaxPixelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Image size {width}×{height} is not supported: each side must be 1 to {MaxDimension} pixels and the image at most {MaxPixelCount:N0} pixels in total.");
        }
    }

    /// <summary>Dimensions scaled by <paramref name="percent"/> (100 = unchanged), rounded, at least 1×1.</summary>
    public static PixelSize ScaleDimensions(int width, int height, double percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (!double.IsFinite(percent) || percent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "Scale percentage must be a positive number.");
        }

        return new PixelSize(RoundDimension(width * percent / 100), RoundDimension(height * percent / 100));
    }

    /// <summary>
    /// Keeps the aspect ratio of <paramref name="width"/> × <paramref name="height"/>.
    /// With only <paramref name="targetWidth"/>: that width and the proportional height.
    /// With both targets: the largest size that fits inside <paramref name="targetWidth"/> × <paramref name="targetHeight"/>
    /// (may be larger than the original). Results are rounded and at least 1×1.
    /// </summary>
    public static PixelSize FitWithAspect(int width, int height, int targetWidth, int? targetHeight = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetWidth, 1);
        if (targetHeight is not { } th)
        {
            return new PixelSize(targetWidth, RoundDimension((double)height * targetWidth / width));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(th, 1, nameof(targetHeight));
        double scale = Math.Min((double)targetWidth / width, (double)th / height);
        return new PixelSize(
            Math.Min(targetWidth, RoundDimension(width * scale)),
            Math.Min(th, RoundDimension(height * scale)));
    }

    /// <summary>
    /// High-quality resize. Per axis: area averaging (box filter, every source pixel contributes by the area it
    /// covers) when shrinking, bilinear interpolation (pixel-center aligned, edge-clamped) when enlarging, an exact
    /// copy when the size is unchanged. Averaging is done on premultiplied alpha.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The new size is outside the supported range (<see cref="ValidateSize"/>).</exception>
    public static PixelBuffer Resize(PixelBuffer source, int newWidth, int newHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSize(newWidth, newHeight);
        if (newWidth == source.Width && newHeight == source.Height)
        {
            return source.Clone();
        }

        AxisWeights wx = AxisWeights.Build(source.Width, newWidth);
        AxisWeights wy = AxisWeights.Build(source.Height, newHeight);
        var result = new PixelBuffer(newWidth, newHeight);
        int bands = Math.Clamp(Math.Min(Environment.ProcessorCount, newHeight / 64), 1, 32);
        int rowsPerBand = (newHeight + bands - 1) / bands;
        Parallel.For(0, bands, band =>
        {
            int y0 = band * rowsPerBand;
            int y1 = Math.Min(newHeight, y0 + rowsPerBand);
            if (y0 < y1)
            {
                ResizeRows(source, result, wx, wy, y0, y1);
            }
        });
        return result;
    }

    /// <summary>
    /// The pixels of a <paramref name="newWidth"/> × <paramref name="newHeight"/> <see cref="Resize"/> result that receive any
    /// contribution from <paramref name="sourceRegion"/> (clipped to the source). Used to keep redaction covers
    /// complete after a resize.
    /// </summary>
    internal static PixelRect ResizeFootprint(PixelRect sourceRegion, int sourceWidth, int sourceHeight, int newWidth, int newHeight)
    {
        PixelRect r = sourceRegion.Intersect(new PixelRect(0, 0, sourceWidth, sourceHeight));
        if (r.IsEmpty)
        {
            return PixelRect.Empty;
        }

        (int x0, int x1) = AxisWeights.Build(sourceWidth, newWidth).Footprint(r.X, r.Right);
        (int y0, int y1) = AxisWeights.Build(sourceHeight, newHeight).Footprint(r.Y, r.Bottom);
        return PixelRect.FromLtrb(x0, y0, x1, y1);
    }

    private static void ResizeRows(PixelBuffer source, PixelBuffer result, AxisWeights wx, AxisWeights wy, int y0, int y1)
    {
        int newWidth = result.Width;
        int rowFloats = newWidth * 4;
        int ringSize = wy.MaxCount;
        var ring = new float[ringSize][];
        var ringRow = new int[ringSize];
        for (int i = 0; i < ringSize; i++)
        {
            ring[i] = new float[rowFloats];
            ringRow[i] = -1;
        }

        var premultiplied = new float[source.Width * 4];
        var acc = new float[rowFloats];
        byte[] dst = result.Pixels;
        for (int y = y0; y < y1; y++)
        {
            Array.Clear(acc);
            int start = wy.Start[y], count = wy.Count[y], wOffset = y * wy.MaxCount;
            for (int k = 0; k < count; k++)
            {
                int sy = start + k;
                int slot = sy % ringSize;
                if (ringRow[slot] != sy)
                {
                    ResampleRow(source, sy, wx, premultiplied, ring[slot]);
                    ringRow[slot] = sy;
                }

                float weight = wy.Weights[wOffset + k];
                float[] row = ring[slot];
                for (int i = 0; i < rowFloats; i++)
                {
                    acc[i] += weight * row[i];
                }
            }

            int o = y * result.Stride;
            for (int i = 0; i < rowFloats; i += 4, o += 4)
            {
                WriteUnpremultiplied(dst, o, acc[i], acc[i + 1], acc[i + 2], acc[i + 3]);
            }
        }
    }

    private static void ResampleRow(PixelBuffer source, int sy, AxisWeights wx, float[] premultiplied, float[] output)
    {
        byte[] p = source.Pixels;
        int s = sy * source.Stride;
        for (int i = 0; i < premultiplied.Length; i += 4, s += 4)
        {
            float a = p[s + 3];
            premultiplied[i] = p[s] * a / 255f;
            premultiplied[i + 1] = p[s + 1] * a / 255f;
            premultiplied[i + 2] = p[s + 2] * a / 255f;
            premultiplied[i + 3] = a;
        }

        int newWidth = output.Length / 4;
        for (int x = 0; x < newWidth; x++)
        {
            int start = wx.Start[x], count = wx.Count[x], wOffset = x * wx.MaxCount;
            float b = 0, g = 0, r = 0, a = 0;
            for (int k = 0; k < count; k++)
            {
                float weight = wx.Weights[wOffset + k];
                int i = (start + k) * 4;
                b += weight * premultiplied[i];
                g += weight * premultiplied[i + 1];
                r += weight * premultiplied[i + 2];
                a += weight * premultiplied[i + 3];
            }

            int o = x * 4;
            output[o] = b;
            output[o + 1] = g;
            output[o + 2] = r;
            output[o + 3] = a;
        }
    }

    private static void WriteUnpremultiplied(byte[] dst, int o, float b, float g, float r, float a)
    {
        if (a < 0.5f)
        {
            dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0;
            return;
        }

        float scale = 255f / a;
        dst[o] = ToByte(b * scale);
        dst[o + 1] = ToByte(g * scale);
        dst[o + 2] = ToByte(r * scale);
        dst[o + 3] = ToByte(a);
    }

    private static byte ToByte(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5f);

    private static int RoundDimension(double v) => (int)Math.Max(1, Math.Min(int.MaxValue, Math.Round(v, MidpointRounding.AwayFromZero)));

    /// <summary>Per-output-pixel source contributors along one axis.</summary>
    private sealed class AxisWeights
    {
        private AxisWeights(int length, int maxCount)
        {
            Start = new int[length];
            Count = new int[length];
            MaxCount = maxCount;
            Weights = new float[length * maxCount];
        }

        public int[] Start { get; }

        public int[] Count { get; }

        public float[] Weights { get; }

        public int MaxCount { get; }

        public static AxisWeights Build(int sourceLength, int targetLength)
        {
            if (targetLength == sourceLength)
            {
                var identity = new AxisWeights(targetLength, 1);
                for (int j = 0; j < targetLength; j++)
                {
                    identity.Start[j] = j;
                    identity.Count[j] = 1;
                    identity.Weights[j] = 1;
                }

                return identity;
            }

            if (targetLength > sourceLength)
            {
                // Bilinear, pixel centers aligned: u = (j + 0.5) * src / dst - 0.5, clamped to the edge pixels.
                var up = new AxisWeights(targetLength, 2);
                double ratio = (double)sourceLength / targetLength;
                for (int j = 0; j < targetLength; j++)
                {
                    double u = Math.Clamp((j + 0.5) * ratio - 0.5, 0, sourceLength - 1);
                    int i0 = (int)Math.Floor(u);
                    double f = u - i0;
                    up.Start[j] = i0;
                    if (f <= 1e-9 || i0 + 1 >= sourceLength)
                    {
                        up.Count[j] = 1;
                        up.Weights[j * 2] = 1;
                    }
                    else
                    {
                        up.Count[j] = 2;
                        up.Weights[j * 2] = (float)(1 - f);
                        up.Weights[(j * 2) + 1] = (float)f;
                    }
                }

                return up;
            }

            // Area averaging: target pixel j covers source interval [j * scale, (j + 1) * scale).
            double scale = (double)sourceLength / targetLength;
            var down = new AxisWeights(targetLength, (int)Math.Ceiling(scale) + 1);
            for (int j = 0; j < targetLength; j++)
            {
                double a = j * scale, b = Math.Min(sourceLength, (j + 1) * scale);
                int first = (int)Math.Floor(a + 1e-9);
                int last = Math.Min(sourceLength - 1, (int)Math.Ceiling(b - 1e-9) - 1);
                down.Start[j] = first;
                down.Count[j] = last - first + 1;
                double total = 0;
                for (int i = first; i <= last; i++)
                {
                    total += Math.Min(b, i + 1) - Math.Max(a, i);
                }

                for (int i = first; i <= last; i++)
                {
                    down.Weights[(j * down.MaxCount) + i - first] = (float)((Math.Min(b, i + 1) - Math.Max(a, i)) / total);
                }
            }

            return down;
        }

        /// <summary>Target index range [lo, hi) whose contributors intersect source range [from, to).</summary>
        public (int Lo, int Hi) Footprint(int from, int to)
        {
            int lo = -1, hi = -1;
            for (int j = 0; j < Start.Length; j++)
            {
                if (Start[j] < to && Start[j] + Count[j] > from)
                {
                    if (lo < 0)
                    {
                        lo = j;
                    }

                    hi = j + 1;
                }
            }

            return lo < 0 ? (0, 0) : (lo, hi);
        }
    }

    // ───────────────────────────── Canvas ─────────────────────────────

    /// <summary>
    /// Changes the canvas size without scaling: the image is placed according to <paramref name="anchor"/> (centered
    /// positions round toward zero) and new area is filled with <paramref name="fill"/> (alpha kept as given). A
    /// smaller canvas cuts the image off.
    /// </summary>
    public static CanvasResult ResizeCanvas(PixelBuffer source, int newWidth, int newHeight, CanvasAnchor anchor, ArgbColor fill)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSize(newWidth, newHeight);
        (int ox, int oy) = AnchorOffset(source.Width, source.Height, newWidth, newHeight, anchor);
        var result = new PixelBuffer(newWidth, newHeight);
        Fill(result, fill);
        result.Blit(source, ox, oy);
        return new CanvasResult(result, ox, oy);
    }

    /// <summary>Position of an old image's top-left corner on a resized canvas.</summary>
    public static (int X, int Y) AnchorOffset(int oldWidth, int oldHeight, int newWidth, int newHeight, CanvasAnchor anchor)
    {
        int dx = newWidth - oldWidth, dy = newHeight - oldHeight;
        int column = anchor switch
        {
            CanvasAnchor.TopLeft or CanvasAnchor.MiddleLeft or CanvasAnchor.BottomLeft => 0,
            CanvasAnchor.TopCenter or CanvasAnchor.Center or CanvasAnchor.BottomCenter => 1,
            CanvasAnchor.TopRight or CanvasAnchor.MiddleRight or CanvasAnchor.BottomRight => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown canvas anchor."),
        };
        int row = anchor switch
        {
            CanvasAnchor.TopLeft or CanvasAnchor.TopCenter or CanvasAnchor.TopRight => 0,
            CanvasAnchor.MiddleLeft or CanvasAnchor.Center or CanvasAnchor.MiddleRight => 1,
            _ => 2,
        };
        return (column == 0 ? 0 : column == 1 ? dx / 2 : dx, row == 0 ? 0 : row == 1 ? dy / 2 : dy);
    }

    /// <summary>Surrounds the image with a <paramref name="thickness"/>-pixel frame; the result grows by 2 × thickness per axis.</summary>
    public static PixelBuffer AddBorder(PixelBuffer source, int thickness, ArgbColor color)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(thickness);
        ValidateSize(source.Width + (2L * thickness), source.Height + (2L * thickness));
        var result = new PixelBuffer(source.Width + (2 * thickness), source.Height + (2 * thickness));
        Fill(result, color);
        result.Blit(source, thickness, thickness);
        return result;
    }

    /// <summary>
    /// Adds a drop shadow. The shadow is the image's alpha silhouette (a rectangle for opaque screenshots) tinted
    /// with <paramref name="shadowColor"/> (its alpha sets the shadow strength), moved by
    /// (<paramref name="offsetX"/>, <paramref name="offsetY"/>) and softened with <see cref="BoxBlur"/>'s
    /// three-pass box blur of <paramref name="blurRadius"/>, which spreads it by 3 × radius pixels. The canvas grows
    /// just enough to hold the image and the whole shadow; it is filled with <paramref name="background"/> (may be
    /// transparent), then the shadow and finally the image are blended on top (source-over), so the image itself is
    /// unchanged. The result tells where the image's top-left corner landed.
    /// </summary>
    public static CanvasResult AddDropShadow(PixelBuffer source, int offsetX, int offsetY, int blurRadius, ArgbColor shadowColor, ArgbColor background)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(blurRadius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blurRadius, MaxBlurRadius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs((long)offsetX), MaxDimension, nameof(offsetX));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs((long)offsetY), MaxDimension, nameof(offsetY));
        int w = source.Width, h = source.Height, spread = 3 * blurRadius;
        long minX = Math.Min(0, offsetX - spread), maxX = Math.Max(w, (long)offsetX + w + spread);
        long minY = Math.Min(0, offsetY - spread), maxY = Math.Max(h, (long)offsetY + h + spread);
        ValidateSize(maxX - minX, maxY - minY);
        int cw = (int)(maxX - minX), ch = (int)(maxY - minY);
        int imageX = (int)-minX, imageY = (int)-minY;

        // Shadow coverage (0..255): the source alpha at the shadow position, then blurred.
        var mask = new int[cw * ch];
        byte[] sp = source.Pixels;
        for (int y = 0; y < h; y++)
        {
            int m = ((imageY + offsetY + y) * cw) + imageX + offsetX;
            int s = (y * source.Stride) + 3;
            for (int x = 0; x < w; x++, s += 4)
            {
                mask[m + x] = sp[s];
            }
        }

        if (blurRadius > 0)
        {
            BlurPlanes(mask, cw, ch, 1, blurRadius);
        }

        var result = new PixelBuffer(cw, ch);
        Fill(result, background);
        byte[] dp = result.Pixels;
        for (int i = 0, o = 0; i < mask.Length; i++, o += 4)
        {
            int coverage = mask[i];
            if (coverage > 0)
            {
                BlendOver(dp, o, shadowColor.B, shadowColor.G, shadowColor.R, shadowColor.A * coverage / (255f * 255f));
            }
        }

        DrawOver(result, source, imageX, imageY, 1);
        return new CanvasResult(result, imageX, imageY);
    }

    /// <summary>
    /// Lays images out side by side (<see cref="CombineDirection.Horizontal"/>) or stacked
    /// (<see cref="CombineDirection.Vertical"/>) with <paramref name="spacing"/> pixels between them, aligned on the
    /// cross axis by <paramref name="alignment"/>. Uncovered area is <paramref name="background"/>; image pixels are
    /// copied as they are (not blended).
    /// </summary>
    public static PixelBuffer Combine(IReadOnlyList<PixelBuffer> images, CombineDirection direction, int spacing, ArgbColor background, CombineAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("At least one image is required.", nameof(images));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(spacing);
        bool horizontal = direction switch
        {
            CombineDirection.Horizontal => true,
            CombineDirection.Vertical => false,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown combine direction."),
        };
        long main = (long)spacing * (images.Count - 1);
        int cross = 0;
        foreach (PixelBuffer image in images)
        {
            if (image is null)
            {
                throw new ArgumentException("Images must not be null.", nameof(images));
            }

            main += horizontal ? image.Width : image.Height;
            cross = Math.Max(cross, horizontal ? image.Height : image.Width);
        }

        if (horizontal)
        {
            ValidateSize(main, cross);
        }
        else
        {
            ValidateSize(cross, main);
        }

        var result = horizontal ? new PixelBuffer((int)main, cross) : new PixelBuffer(cross, (int)main);
        Fill(result, background);
        int position = 0;
        foreach (PixelBuffer image in images)
        {
            int extent = horizontal ? image.Height : image.Width;
            int offset = alignment switch
            {
                CombineAlignment.Start => 0,
                CombineAlignment.Center => (cross - extent) / 2,
                CombineAlignment.End => cross - extent,
                _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unknown alignment."),
            };
            if (horizontal)
            {
                result.Blit(image, position, offset);
                position += image.Width + spacing;
            }
            else
            {
                result.Blit(image, offset, position);
                position += image.Height + spacing;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a copy of <paramref name="target"/> with <paramref name="overlay"/> blended on top at (x, y) using
    /// straight-alpha source-over: with <c>s = overlay alpha × opacity</c>, <c>out alpha = s + d × (1 − s)</c> and
    /// <c>out color = (src × s + dst × d × (1 − s)) / out alpha</c>. Parts outside the target are clipped.
    /// <paramref name="target"/> itself is not modified.
    /// </summary>
    public static PixelBuffer DrawImage(PixelBuffer target, PixelBuffer overlay, int x, int y, double opacity)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(overlay);
        if (double.IsNaN(opacity) || opacity < 0 || opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be between 0 and 1.");
        }

        PixelBuffer result = target.Clone();
        DrawOver(result, overlay, x, y, (float)opacity);
        return result;
    }

    // ───────────────────────────── Region effects ─────────────────────────────

    /// <summary>
    /// Returns a copy with <paramref name="region"/> (clipped to the image) blurred: a separable box blur of
    /// <paramref name="radius"/> applied three times, which closely approximates a Gaussian. Sampling is clamped to
    /// the region's edges, so pixels outside the region neither change nor influence the result.
    /// Not a redaction: blurred text can sometimes be recovered.
    /// </summary>
    public static PixelBuffer BoxBlur(PixelBuffer source, PixelRect region, int radius)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, MaxBlurRadius);
        PixelBuffer result = source.Clone();
        PixelRect r = region.Intersect(source.Bounds);
        if (r.IsEmpty || radius == 0)
        {
            return result;
        }

        int w = r.Width, h = r.Height;
        int[] planes = ToPremultipliedInts(source, r);
        BlurPlanes(planes, w, h, 4, radius);
        byte[] dst = result.Pixels;
        for (int y = 0; y < h; y++)
        {
            int o = ((r.Y + y) * source.Stride) + (r.X * 4);
            int p = y * w * 4;
            for (int x = 0; x < w; x++, o += 4, p += 4)
            {
                WritePremultipliedInt(dst, o, planes[p], planes[p + 1], planes[p + 2], planes[p + 3]);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a copy with <paramref name="region"/> (clipped to the image) pixelated: it is divided into
    /// <paramref name="blockSize"/> squares starting at the clipped region's top-left corner (blocks at the right and
    /// bottom edges may be smaller) and every pixel of a block gets the block's average color (premultiplied).
    /// Not a redaction: pixelated text can sometimes be recovered.
    /// </summary>
    public static PixelBuffer Pixelate(PixelBuffer source, PixelRect region, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        PixelBuffer result = source.Clone();
        PixelRect r = region.Intersect(source.Bounds);
        if (r.IsEmpty || blockSize == 1)
        {
            return result;
        }

        byte[] p = result.Pixels;
        int stride = result.Stride;
        for (int by = r.Y; by < r.Bottom; by += blockSize)
        {
            int bh = Math.Min(blockSize, r.Bottom - by);
            for (int bx = r.X; bx < r.Right; bx += blockSize)
            {
                int bw = Math.Min(blockSize, r.Right - bx);
                long sb = 0, sg = 0, sr = 0, sa = 0;
                for (int y = by; y < by + bh; y++)
                {
                    int i = (y * stride) + (bx * 4);
                    for (int x = 0; x < bw; x++, i += 4)
                    {
                        int a = p[i + 3];
                        sb += p[i] * a;
                        sg += p[i + 1] * a;
                        sr += p[i + 2] * a;
                        sa += a;
                    }
                }

                long n = (long)bw * bh;
                byte ob = 0, og = 0, orr = 0, oa = (byte)((sa + (n / 2)) / n);
                if (sa > 0 && oa > 0)
                {
                    ob = (byte)Math.Min(255, (sb + (sa / 2)) / sa);
                    og = (byte)Math.Min(255, (sg + (sa / 2)) / sa);
                    orr = (byte)Math.Min(255, (sr + (sa / 2)) / sa);
                }
                else
                {
                    oa = 0;
                }

                for (int y = by; y < by + bh; y++)
                {
                    int i = (y * stride) + (bx * 4);
                    for (int x = 0; x < bw; x++, i += 4)
                    {
                        p[i] = ob;
                        p[i + 1] = og;
                        p[i + 2] = orr;
                        p[i + 3] = oa;
                    }
                }
            }
        }

        return result;
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    /// <summary>Overwrites every pixel with <paramref name="color"/>, alpha included.</summary>
    private static void Fill(PixelBuffer buffer, ArgbColor color)
    {
        uint value = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
        Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(buffer.Pixels.AsSpan());
        if (BitConverter.IsLittleEndian)
        {
            pixels.Fill(value);
        }
        else
        {
            pixels.Fill(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value));
        }
    }

    private static void DrawOver(PixelBuffer target, PixelBuffer overlay, int x, int y, float opacity)
    {
        PixelRect dest = new PixelRect(x, y, overlay.Width, overlay.Height).Intersect(target.Bounds);
        if (dest.IsEmpty || opacity <= 0)
        {
            return;
        }

        byte[] dp = target.Pixels, sp = overlay.Pixels;
        for (int row = dest.Y; row < dest.Bottom; row++)
        {
            int d = (row * target.Stride) + (dest.X * 4);
            int s = ((row - y) * overlay.Stride) + ((dest.X - x) * 4);
            for (int col = 0; col < dest.Width; col++, d += 4, s += 4)
            {
                int a = sp[s + 3];
                if (a == 255 && opacity >= 1)
                {
                    dp[d] = sp[s];
                    dp[d + 1] = sp[s + 1];
                    dp[d + 2] = sp[s + 2];
                    dp[d + 3] = 255;
                }
                else if (a > 0)
                {
                    BlendOver(dp, d, sp[s], sp[s + 1], sp[s + 2], a * opacity / 255f);
                }
            }
        }
    }

    /// <summary>Straight-alpha source-over of one pixel with source coverage <paramref name="sa"/> in [0, 1].</summary>
    private static void BlendOver(byte[] dst, int o, byte b, byte g, byte r, float sa)
    {
        if (sa <= 0)
        {
            return;
        }

        float da = dst[o + 3] / 255f;
        float rest = da * (1 - sa);
        float oa = sa + rest;
        if (oa <= 0)
        {
            return;
        }

        dst[o] = ToByte(((b * sa) + (dst[o] * rest)) / oa);
        dst[o + 1] = ToByte(((g * sa) + (dst[o + 1] * rest)) / oa);
        dst[o + 2] = ToByte(((r * sa) + (dst[o + 2] * rest)) / oa);
        dst[o + 3] = ToByte(oa * 255);
    }

    /// <summary>Region as interleaved premultiplied ints: color × alpha and alpha × 255 (both 0..65025).</summary>
    private static int[] ToPremultipliedInts(PixelBuffer source, PixelRect r)
    {
        var planes = new int[r.Width * r.Height * 4];
        byte[] sp = source.Pixels;
        for (int y = 0; y < r.Height; y++)
        {
            int s = ((r.Y + y) * source.Stride) + (r.X * 4);
            int p = y * r.Width * 4;
            for (int x = 0; x < r.Width; x++, s += 4, p += 4)
            {
                int a = sp[s + 3];
                planes[p] = sp[s] * a;
                planes[p + 1] = sp[s + 1] * a;
                planes[p + 2] = sp[s + 2] * a;
                planes[p + 3] = a * 255;
            }
        }

        return planes;
    }

    private static void WritePremultipliedInt(byte[] dst, int o, int b, int g, int r, int a255)
    {
        int a = (a255 + 127) / 255;
        if (a == 0)
        {
            dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0;
            return;
        }

        int half = a255 / 2;
        dst[o] = (byte)Math.Min(255, ((b * 255) + half) / a255);
        dst[o + 1] = (byte)Math.Min(255, ((g * 255) + half) / a255);
        dst[o + 2] = (byte)Math.Min(255, ((r * 255) + half) / a255);
        dst[o + 3] = (byte)Math.Min(255, a);
    }

    /// <summary>Three passes of a separable, edge-clamped box blur over interleaved int channels.</summary>
    private static void BlurPlanes(int[] data, int width, int height, int channels, int radius)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            Parallel.For(
                0,
                height,
                () => new long[width * channels],
                (y, _, line) =>
                {
                    BlurLine(data, y * width * channels, channels, width, radius, channels, line);
                    return line;
                },
                _ => { });
            Parallel.For(
                0,
                width,
                () => new long[height * channels],
                (x, _, line) =>
                {
                    BlurLine(data, x * channels, width * channels, height, radius, channels, line);
                    return line;
                },
                _ => { });
        }
    }

    /// <summary>Box-blurs <paramref name="n"/> pixels starting at <paramref name="offset"/>, <paramref name="step"/> ints apart.</summary>
    private static void BlurLine(int[] data, int offset, int step, int n, int radius, int channels, long[] line)
    {
        for (int i = 0, idx = offset; i < n; i++, idx += step)
        {
            for (int c = 0; c < channels; c++)
            {
                line[(i * channels) + c] = data[idx + c];
            }
        }

        long divisor = (2L * radius) + 1, half = divisor / 2;
        for (int c = 0; c < channels; c++)
        {
            long sum = (radius + 1) * line[c];
            for (int k = 1; k <= radius; k++)
            {
                sum += line[(Math.Min(k, n - 1) * channels) + c];
            }

            for (int i = 0, idx = offset + c; i < n; i++, idx += step)
            {
                data[idx] = (int)((sum + half) / divisor);
                sum += line[(Math.Min(i + radius + 1, n - 1) * channels) + c] - line[(Math.Max(i - radius, 0) * channels) + c];
            }
        }
    }
}
