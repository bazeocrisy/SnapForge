using System.Diagnostics;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Tests;

public class GifEncoderTests
{
    /// <summary>Deterministic xorshift generator (tests must not depend on System.Random's algorithm).</summary>
    internal sealed class TestRng(ulong seed)
    {
        private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15ul : seed;

        public uint Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return (uint)(_state >> 16);
        }

        public int Next(int maxExclusive) => (int)(Next() % (uint)maxExclusive);
    }

    private static PixelBuffer Solid(int w, int h, ArgbColor c)
    {
        var b = new PixelBuffer(w, h);
        b.FillRect(b.Bounds, c);
        return b;
    }

    private static ArgbColor Rgb(int c) => new(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c);

    private static byte[] Encode(int w, int h, GifEncoderOptions options, params (PixelBuffer Frame, int Delay)[] frames)
    {
        using var ms = new MemoryStream();
        using (var encoder = new GifEncoder(ms, w, h, options, leaveOpen: true))
        {
            foreach ((PixelBuffer frame, int delay) in frames)
            {
                encoder.AddFrame(frame, delay);
            }

            encoder.Finish();
        }

        return ms.ToArray();
    }

    private static void AssertSameRgb(PixelBuffer expected, PixelBuffer actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int i = 0; i < expected.Pixels.Length; i += 4)
        {
            if (expected.Pixels[i] != actual.Pixels[i] || expected.Pixels[i + 1] != actual.Pixels[i + 1] || expected.Pixels[i + 2] != actual.Pixels[i + 2])
            {
                int p = i / 4;
                Assert.Fail($"Pixel ({p % expected.Width},{p / expected.Width}) expected {expected.GetPixel(p % expected.Width, p / expected.Width)} got {actual.GetPixel(p % expected.Width, p / expected.Width)}");
            }
        }
    }

    private static PixelBuffer RandomFromPalette(int w, int h, int[] palette, TestRng rng)
    {
        var b = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                b.SetPixel(x, y, Rgb(palette[rng.Next(palette.Length)]));
            }
        }

        return b;
    }

    private static int[] DistinctColors(int count, TestRng rng)
    {
        var set = new HashSet<int>();
        while (set.Count < count)
        {
            set.Add((int)(rng.Next() & 0xFFFFFF));
        }

        return [.. set];
    }

    [Fact]
    public void Solid_frames_decode_to_their_colors()
    {
        ArgbColor[] colors = [ArgbColor.FromRgb(255, 0, 0), ArgbColor.FromRgb(0, 200, 30), ArgbColor.FromRgb(12, 34, 250)];
        byte[] gif = Encode(17, 9, new GifEncoderOptions(), [.. colors.Select(c => (Solid(17, 9, c), 10))]);
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.Equal(17, d.Width);
        Assert.Equal(9, d.Height);
        Assert.Equal(3, d.Frames.Count);
        for (int k = 0; k < 3; k++)
        {
            AssertSameRgb(Solid(17, 9, colors[k]), d.Frames[k].Canvas);
            Assert.Equal(10, d.Frames[k].Delay);
            Assert.Equal(1, d.Frames[k].Disposal);
        }
    }

    [Theory]
    [InlineData(GifDithering.None)]
    [InlineData(GifDithering.OrderedBayer4x4)]
    [InlineData(GifDithering.FloydSteinberg)]
    public void Two_color_image_is_pixel_exact_with_two_colors(GifDithering dithering)
    {
        var rng = new TestRng(7);
        PixelBuffer image = RandomFromPalette(37, 23, [0x000000, 0xFFFFFF], rng);
        byte[] gif = Encode(37, 23, new GifEncoderOptions { MaxColors = 2, Dithering = dithering }, (image, 5));
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        GifTestDecoder.DecodedFrame frame = Assert.Single(d.Frames);
        Assert.Equal(2, frame.PaletteSize);
        AssertSameRgb(image, frame.Canvas);
    }

    [Theory]
    [InlineData(GifPaletteMode.LocalPerFrame)]
    [InlineData(GifPaletteMode.GlobalFromFirstFrame)]
    public void Gradient_with_256_distinct_colors_is_exact(GifPaletteMode mode)
    {
        var image = new PixelBuffer(256, 12);
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                image.SetPixel(x, y, ArgbColor.FromRgb((byte)x, (byte)(255 - x), (byte)(x * 7)));
            }
        }

        byte[] gif = Encode(256, 12, new GifEncoderOptions { PaletteMode = mode, Dithering = GifDithering.FloydSteinberg }, (image, 4));
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.Equal(mode == GifPaletteMode.GlobalFromFirstFrame, d.HasGlobalTable);
        Assert.Equal(256, d.Frames[0].PaletteSize);
        AssertSameRgb(image, d.Frames[0].Canvas);
    }

    [Theory]
    [InlineData(GifPaletteMode.LocalPerFrame)]
    [InlineData(GifPaletteMode.GlobalFromFirstFrame)]
    public void Sub_rectangle_frames_composite_back_to_the_originals(GifPaletteMode mode)
    {
        var rng = new TestRng(42);
        int[] palette = DistinctColors(200, rng);
        int w = 120, h = 80;
        var frames = new List<PixelBuffer> { RandomFromPalette(w, h, palette, rng) };
        PixelRect[] changes = [new(10, 5, 20, 7), new(0, 0, 1, 1), new(119, 79, 1, 1), new(40, 30, 60, 50)];
        foreach (PixelRect change in changes)
        {
            PixelBuffer next = frames[^1].Clone();
            for (int y = change.Y; y < change.Bottom; y++)
            {
                for (int x = change.X; x < change.Right; x++)
                {
                    next.SetPixel(x, y, Rgb(palette[rng.Next(palette.Length)]));
                }
            }

            frames.Add(next);
        }

        // An unchanged frame (alpha differs only) must merge into the previous delay.
        PixelBuffer same = frames[^1].Clone();
        same.Pixels[3] = 0;
        frames.Add(same);

        byte[] gif = Encode(w, h, new GifEncoderOptions { PaletteMode = mode }, [.. frames.Select((f, k) => (f, 10 + k))]);
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.Equal(frames.Count - 1, d.Frames.Count);
        for (int k = 0; k < d.Frames.Count; k++)
        {
            AssertSameRgb(frames[k], d.Frames[k].Canvas);
        }

        Assert.Equal((0, 0, w, h), (d.Frames[0].X, d.Frames[0].Y, d.Frames[0].W, d.Frames[0].H));
        for (int k = 0; k < changes.Length; k++)
        {
            GifTestDecoder.DecodedFrame f = d.Frames[k + 1];

            // The encoded rectangle lies within the changed area (random replacement can leave edge pixels equal).
            Assert.True(changes[k].Intersect(new PixelRect(f.X, f.Y, f.W, f.H)) == new PixelRect(f.X, f.Y, f.W, f.H), $"frame {k + 1} rect {f.X},{f.Y} {f.W}x{f.H}");
        }

        Assert.Equal(10 + 4 + 10 + 5, d.Frames[^1].Delay);
    }

    [Fact]
    public void Unoptimized_frames_are_written_in_full()
    {
        PixelBuffer a = Solid(8, 8, ArgbColor.White);
        byte[] gif = Encode(8, 8, new GifEncoderOptions { OptimizeFrames = false }, (a, 3), (a, 3));
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.Equal(2, d.Frames.Count);
        Assert.All(d.Frames, f => Assert.Equal((0, 0, 8, 8), (f.X, f.Y, f.W, f.H)));
    }

    [Fact]
    public void Delay_overflowing_65535_uses_a_transparent_1x1_frame()
    {
        PixelBuffer a = Solid(4, 3, ArgbColor.FromRgb(9, 8, 7));
        byte[] gif = Encode(4, 3, new GifEncoderOptions(), (a, 60000), (a, 60000), (a, 1));
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.Equal(2, d.Frames.Count);
        Assert.Equal(60000, d.Frames[0].Delay);
        Assert.Equal(60002, d.Frames[1].Delay); // 60000 + clamped 1 → 2
        Assert.True(d.Frames[1].Transparent);
        Assert.Equal((1, 1), (d.Frames[1].W, d.Frames[1].H));
        AssertSameRgb(a, d.Frames[1].Canvas);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-5, 2)]
    [InlineData(1, 2)]
    [InlineData(7, 7)]
    [InlineData(100000, 65535)]
    public void Delays_are_clamped(int requested, int written)
    {
        byte[] gif = Encode(2, 2, new GifEncoderOptions(), (Solid(2, 2, ArgbColor.Black), requested));
        Assert.Equal(written, GifTestDecoder.Decode(gif).Frames[0].Delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Loop_extension_and_trailer(int loopCount)
    {
        byte[] gif = Encode(3, 3, new GifEncoderOptions { LoopCount = loopCount }, (Solid(3, 3, ArgbColor.White), 10));
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.Equal(loopCount < 0 ? null : loopCount, d.LoopCount);
        Assert.True(d.HasTrailer);
        Assert.Equal(0x3B, gif[^1]);
        Assert.Equal("GIF89a"u8.ToArray(), gif[..6]);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 4095)]
    [InlineData(2, 200_000)]
    [InlineData(4, 77_777)]
    [InlineData(8, 1)]
    [InlineData(8, 300_000)]
    public void Lzw_round_trips_random_and_repetitive_data(int minCodeSize, int length)
    {
        var rng = new TestRng((ulong)(minCodeSize * 1000 + length));
        var data = new byte[length];
        int alphabet = 1 << minCodeSize;
        for (int i = 0; i < length; i++)
        {
            // Alternate random stretches and long runs so the table fills, resets and also grows long strings.
            data[i] = (i / 5000 % 2 == 0) ? (byte)rng.Next(alphabet) : (byte)((i / 300) % alphabet);
        }

        using var ms = new MemoryStream();
        new GifLzwEncoder().Encode(ms, data, minCodeSize);
        byte[] blocks = ms.ToArray();
        var compressed = new List<byte>();
        int p = 0;
        while (blocks[p] != 0)
        {
            Assert.InRange(blocks[p], 1, 255);
            compressed.AddRange(blocks.AsSpan(p + 1, blocks[p]).ToArray());
            p += blocks[p] + 1;
        }

        Assert.Equal(blocks.Length - 1, p);
        Assert.Equal(data, GifTestDecoder.LzwDecode([.. compressed], minCodeSize, length));
    }

    [Fact]
    public void Large_noisy_image_with_256_colors_survives_many_clear_codes()
    {
        var rng = new TestRng(99);
        int[] palette = DistinctColors(256, rng);
        PixelBuffer image = RandomFromPalette(700, 500, palette, rng);
        byte[] gif = Encode(700, 500, new GifEncoderOptions(), (image, 10));
        AssertSameRgb(image, GifTestDecoder.Decode(gif).Frames[0].Canvas);
    }

    [Theory]
    [InlineData(GifDithering.None, 16)]
    [InlineData(GifDithering.OrderedBayer4x4, 16)]
    [InlineData(GifDithering.FloydSteinberg, 16)]
    [InlineData(GifDithering.None, 256)]
    [InlineData(GifDithering.FloydSteinberg, 2)]
    public void Many_color_image_is_quantized_with_reasonable_error(GifDithering dithering, int maxColors)
    {
        var image = new PixelBuffer(200, 150);
        for (int y = 0; y < 150; y++)
        {
            for (int x = 0; x < 200; x++)
            {
                image.SetPixel(x, y, ArgbColor.FromRgb((byte)(x * 255 / 199), (byte)(y * 255 / 149), (byte)((x + y) * 255 / 348)));
            }
        }

        byte[] gif = Encode(200, 150, new GifEncoderOptions { MaxColors = maxColors, Dithering = dithering }, (image, 10));
        GifTestDecoder.DecodedFrame frame = GifTestDecoder.Decode(gif).Frames[0];
        Assert.True(frame.PaletteSize <= Math.Max(2, maxColors));

        // Average over 4×4 blocks: dithering trades per-pixel error for a correct local average.
        double totalError = 0;
        int blocks = 0;
        for (int by = 0; by < 148; by += 4)
        {
            for (int bx = 0; bx < 200; bx += 4)
            {
                double[] e = new double[3];
                for (int y = by; y < by + 4; y++)
                {
                    for (int x = bx; x < bx + 4; x++)
                    {
                        ArgbColor a = image.GetPixel(x, y), b = frame.Canvas.GetPixel(x, y);
                        e[0] += a.R - b.R;
                        e[1] += a.G - b.G;
                        e[2] += a.B - b.B;
                    }
                }

                totalError += Math.Sqrt(e[0] * e[0] + e[1] * e[1] + e[2] * e[2]) / 16;
                blocks++;
            }
        }

        double mean = totalError / blocks;
        double limit = maxColors switch { 2 => 100, 16 => 28, _ => 6 };
        Assert.True(mean < limit, $"mean block error {mean:F1} (limit {limit})");
    }

    [Fact]
    public void Global_palette_maps_later_colors_to_the_nearest_entry()
    {
        PixelBuffer first = Solid(10, 10, ArgbColor.Black);
        first.FillRect(new PixelRect(0, 0, 5, 10), ArgbColor.White);
        PixelBuffer second = first.Clone();
        second.FillRect(new PixelRect(6, 6, 2, 2), ArgbColor.FromRgb(240, 240, 240));
        byte[] gif = Encode(10, 10, new GifEncoderOptions { PaletteMode = GifPaletteMode.GlobalFromFirstFrame }, (first, 5), (second, 5));
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.True(d.HasGlobalTable);
        Assert.All(d.Frames, f => Assert.False(f.LocalTable));
        Assert.Equal(ArgbColor.White, d.Frames[1].Canvas.GetPixel(6, 6));
        Assert.Equal(ArgbColor.Black, d.Frames[1].Canvas.GetPixel(9, 9));
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        using var ms = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => new GifEncoder(null!, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(ms, 0, 1, leaveOpen: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(ms, 1, 65536, leaveOpen: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(ms, 1, 1, new GifEncoderOptions { MaxColors = 1 }, leaveOpen: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(ms, 1, 1, new GifEncoderOptions { MaxColors = 257 }, leaveOpen: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(ms, 1, 1, new GifEncoderOptions { LoopCount = -2 }, leaveOpen: true));
        using (var readOnly = new MemoryStream(new byte[4], writable: false))
        {
            Assert.Throws<ArgumentException>(() => new GifEncoder(readOnly, 1, 1, leaveOpen: true));
        }

        var encoder = new GifEncoder(ms, 4, 4, leaveOpen: true);
        Assert.Throws<InvalidOperationException>(encoder.Finish);
        Assert.Throws<ArgumentException>(() => encoder.AddFrame(new PixelBuffer(4, 5), 10));
        Assert.Throws<ArgumentNullException>(() => encoder.AddFrame(null!, 10));
        encoder.AddFrame(new PixelBuffer(4, 4), 10);
        encoder.Finish();
        encoder.Finish(); // idempotent
        Assert.Throws<InvalidOperationException>(() => encoder.AddFrame(new PixelBuffer(4, 4), 10));
        encoder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => encoder.AddFrame(new PixelBuffer(4, 4), 10));
        Assert.True(ms.CanWrite); // leaveOpen

        var owned = new MemoryStream();
        new GifEncoder(owned, 1, 1).Dispose();
        Assert.False(owned.CanWrite);
    }

    [Fact]
    public void Maximum_width_is_accepted()
    {
        byte[] gif = Encode(65535, 1, new GifEncoderOptions(), (Solid(65535, 1, ArgbColor.FromRgb(1, 2, 3)), 10));
        GifTestDecoder d = GifTestDecoder.Decode(gif);
        Assert.Equal(65535, d.Width);
        Assert.Equal(ArgbColor.FromRgb(1, 2, 3), d.Frames[0].Canvas.GetPixel(65534, 0));
    }

    [Fact]
    public void Thirty_screen_sized_frames_encode_quickly_with_bounded_output()
    {
        int w = 640, h = 360;
        var frame = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                frame.SetPixel(x, y, ArgbColor.FromRgb((byte)(x * 255 / w), (byte)(y * 255 / h), 128));
            }
        }

        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        using (var encoder = new GifEncoder(ms, w, h, new GifEncoderOptions { Dithering = GifDithering.OrderedBayer4x4 }, leaveOpen: true))
        {
            for (int k = 0; k < 30; k++)
            {
                // A moving "cursor" and a typing line, like a typical screen recording.
                frame.FillRect(new PixelRect(20 + k * 15, 100, 12, 18), ArgbColor.Black);
                frame.FillRect(new PixelRect(40, 120, 10 + k * 8, 14), ArgbColor.FromRgb(30, 30, 200));
                encoder.AddFrame(frame, 6);
            }

            encoder.Finish();
        }

        sw.Stop();
        // Loose bound: CI runs Core tests under coverage instrumentation (pixel loops ~10–50x slower, DEF-RC-04);
        // uninstrumented speed is checked on the packaged build by the feature self-test (video-gif).
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"encoding took {sw.Elapsed}");
        GifTestDecoder d = GifTestDecoder.Decode(ms.ToArray());
        Assert.Equal(30, d.Frames.Count);
        Assert.All(d.Frames.Skip(1), f => Assert.True(f.W * f.H < w * h / 4));
    }
}
