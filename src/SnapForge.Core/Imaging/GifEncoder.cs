using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SnapForge.Core.Geometry;

namespace SnapForge.Core.Imaging;

/// <summary>How colors that are not in the palette are approximated.</summary>
public enum GifDithering
{
    /// <summary>Each pixel takes the nearest palette color.</summary>
    None,

    /// <summary>Ordered dithering with a 4×4 Bayer matrix (stable between frames, compresses well).</summary>
    OrderedBayer4x4,

    /// <summary>Floyd–Steinberg error diffusion (smoothest gradients, larger files, can shimmer in animation).</summary>
    FloydSteinberg,
}

/// <summary>Where the color table of each frame comes from.</summary>
public enum GifPaletteMode
{
    /// <summary>Every frame gets its own local color table built from the pixels it encodes.</summary>
    LocalPerFrame,

    /// <summary>
    /// One global color table built from the first frame (the sample frame); later frames are mapped onto it.
    /// Smaller files, but colors that first appear later are approximated.
    /// </summary>
    GlobalFromFirstFrame,
}

/// <summary>Settings for <see cref="GifEncoder"/>.</summary>
public sealed record GifEncoderOptions
{
    /// <summary>
    /// NETSCAPE2.0 loop count: 0 loops forever, 1..65535 repeats that many times after the first play
    /// (browser interpretations vary), -1 writes no loop extension (play once).
    /// </summary>
    public int LoopCount { get; init; }

    /// <summary>Dithering used when a frame has more colors than the palette can hold.</summary>
    public GifDithering Dithering { get; init; } = GifDithering.None;

    /// <summary>Maximum palette size, 2..256.</summary>
    public int MaxColors { get; init; } = 256;

    /// <summary>Local palette per frame, or one global palette from the first frame.</summary>
    public GifPaletteMode PaletteMode { get; init; } = GifPaletteMode.LocalPerFrame;

    /// <summary>
    /// When true (the default), frames after the first encode only the bounding rectangle of the pixels that
    /// changed, and a frame identical to the previous one extends the previous frame's delay instead of being
    /// written. When false, every frame is written in full.
    /// </summary>
    public bool OptimizeFrames { get; init; } = true;
}

/// <summary>
/// Streaming animated GIF89a encoder for exporting screen recordings.
/// </summary>
/// <remarks>
/// <para>
/// Memory stays bounded by a few frames regardless of the animation length: each frame is quantized,
/// LZW-compressed and written as soon as the next distinct frame (or <see cref="Finish"/>) arrives. Only the
/// previous source frame (for change detection) and the compressed bytes of the one frame whose delay may
/// still grow are kept.
/// </para>
/// <para>
/// Alpha is ignored: every pixel is treated as opaque, which is what screen captures are. Frames use
/// disposal method 1 ("do not dispose"), so a sub-rectangle frame draws over the previous image.
/// </para>
/// <para>
/// Identical frames: the previous frame's delay is extended. If that delay would exceed the GIF maximum of
/// 65535 cs, the previous frame is written and a 1×1 fully transparent frame (which leaves the canvas
/// untouched) carries the remaining time.
/// </para>
/// <para>
/// Delays are clamped to 2..65535 centiseconds: browsers replace delays below 2 cs (20 ms) with 10 cs, so
/// shorter delays would play slower, not faster.
/// </para>
/// <para>The encoder does not own the stream unless <c>leaveOpen</c> is false (the default), like <see cref="StreamWriter"/>.</para>
/// </remarks>
public sealed class GifEncoder : IDisposable
{
    /// <summary>Largest width or height a GIF can describe.</summary>
    public const int MaxDimension = 65535;

    /// <summary>Shortest delay browsers honour, in centiseconds.</summary>
    public const int MinDelayCentiseconds = 2;

    /// <summary>Longest delay a GIF can store, in centiseconds.</summary>
    public const int MaxDelayCentiseconds = 65535;

    private static readonly int[] Bayer4x4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly GifEncoderOptions _options;
    private readonly ColorQuantizer _quantizer = new();
    private readonly PaletteMapper _mapper = new();
    private readonly GifLzwEncoder _lzw = new();
    private readonly MemoryStream _pending = new();

    private byte[]? _previous;
    private byte[] _indices = [];
    private int[]? _globalPalette;
    private bool _hasPending;
    private int _pendingDelay;
    private bool _pendingTransparent;
    private bool _headerWritten;
    private bool _finished;
    private bool _disposed;

    /// <summary>Creates an encoder that writes a <paramref name="width"/>×<paramref name="height"/> animation to <paramref name="output"/>.</summary>
    /// <param name="output">A writable stream.</param>
    /// <param name="width">Logical screen width, 1..65535.</param>
    /// <param name="height">Logical screen height, 1..65535.</param>
    /// <param name="options">Encoder settings; null uses the defaults.</param>
    /// <param name="leaveOpen">True to keep <paramref name="output"/> open when the encoder is disposed.</param>
    public GifEncoder(Stream output, int width, int height, GifEncoderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The output stream must be writable.", nameof(output));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxDimension);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxDimension);

        options ??= new GifEncoderOptions();
        if (options.MaxColors is < 2 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxColors, "MaxColors must be between 2 and 256.");
        }

        if (options.LoopCount is < -1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.LoopCount, "LoopCount must be -1 (no loop) or 0..65535.");
        }

        if (!Enum.IsDefined(options.Dithering) || !Enum.IsDefined(options.PaletteMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown dithering or palette mode.");
        }

        _output = output;
        _leaveOpen = leaveOpen;
        _options = options;
        Width = width;
        Height = height;
    }

    /// <summary>Logical screen width.</summary>
    public int Width { get; }

    /// <summary>Logical screen height.</summary>
    public int Height { get; }

    /// <summary>Number of frames accepted so far (including ones merged into a previous frame's delay).</summary>
    public int FrameCount { get; private set; }

    /// <summary>Number of image blocks actually written or pending.</summary>
    public int EncodedFrameCount { get; private set; }

    /// <summary>
    /// Adds a frame shown for <paramref name="delayCentiseconds"/> (clamped to 2..65535).
    /// The frame's pixels are copied as needed; the buffer may be reused by the caller afterwards.
    /// </summary>
    /// <exception cref="ArgumentException">The frame size differs from the encoder's size.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Finish"/> was already called.</exception>
    public void AddFrame(PixelBuffer frame, int delayCentiseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (_finished)
        {
            throw new InvalidOperationException("The GIF has already been finished.");
        }

        if (frame.Width != Width || frame.Height != Height)
        {
            throw new ArgumentException($"Frame is {frame.Width}×{frame.Height}; the animation is {Width}×{Height}.", nameof(frame));
        }

        int delay = Math.Clamp(delayCentiseconds, MinDelayCentiseconds, MaxDelayCentiseconds);
        byte[] pixels = frame.Pixels;

        if (!_headerWritten)
        {
            WriteHeader(pixels);
        }

        PixelRect rect = Bounds;
        if (_previous is not null && _options.OptimizeFrames)
        {
            rect = ChangedRect(_previous, pixels);
            if (rect.IsEmpty)
            {
                FrameCount++;
                if (_pendingDelay + delay <= MaxDelayCentiseconds)
                {
                    _pendingDelay += delay;
                }
                else
                {
                    FlushPending();
                    EncodeTransparentPixel(delay);
                }

                return;
            }
        }

        FlushPending();
        EncodeFrame(pixels, rect, delay);
        _previous ??= new byte[pixels.Length];
        Buffer.BlockCopy(pixels, 0, _previous, 0, pixels.Length);
        FrameCount++;
    }

    /// <summary>Writes the last frame and the GIF trailer, then flushes the stream. Further frames are rejected.</summary>
    /// <exception cref="InvalidOperationException">No frame was added.</exception>
    public void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
        {
            return;
        }

        if (FrameCount == 0)
        {
            throw new InvalidOperationException("A GIF needs at least one frame.");
        }

        FlushPending();
        _output.WriteByte(0x3B);
        _output.Flush();
        _finished = true;
    }

    /// <summary>
    /// Releases the encoder and, unless <c>leaveOpen</c> was set, the stream. Does not write the trailer:
    /// an animation abandoned without <see cref="Finish"/> stays truncated rather than looking complete.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pending.Dispose();
        if (!_leaveOpen)
        {
            _output.Dispose();
        }
    }

    private PixelRect Bounds => new(0, 0, Width, Height);

    private void WriteHeader(byte[] firstFrame)
    {
        Span<byte> lsd = stackalloc byte[13];
        "GIF89a"u8.CopyTo(lsd);
        BinaryPrimitives.WriteUInt16LittleEndian(lsd[6..], (ushort)Width);
        BinaryPrimitives.WriteUInt16LittleEndian(lsd[8..], (ushort)Height);
        int tableBits = 0;
        if (_options.PaletteMode == GifPaletteMode.GlobalFromFirstFrame)
        {
            _globalPalette = _quantizer.BuildPalette(firstFrame, Width * 4, Bounds, _options.MaxColors, out _);
            tableBits = TableBits(_globalPalette.Length);
            lsd[10] = (byte)(0x80 | ((tableBits - 1) << 4) | (tableBits - 1));
        }
        else
        {
            lsd[10] = 0x70; // no global table; 8-bit color resolution
        }

        lsd[11] = 0; // background color index
        lsd[12] = 0; // no aspect ratio
        _output.Write(lsd);
        if (_globalPalette is not null)
        {
            WriteColorTable(_output, _globalPalette, tableBits);
        }

        if (_options.LoopCount >= 0)
        {
            Span<byte> loop = stackalloc byte[19];
            loop[0] = 0x21;
            loop[1] = 0xFF;
            loop[2] = 11;
            "NETSCAPE2.0"u8.CopyTo(loop[3..]);
            loop[14] = 3;
            loop[15] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(loop[16..], (ushort)_options.LoopCount);
            loop[18] = 0;
            _output.Write(loop);
        }

        _headerWritten = true;
    }

    private void EncodeFrame(byte[] pixels, PixelRect rect, int delay)
    {
        int[] palette;
        bool exact;
        if (_globalPalette is not null)
        {
            palette = _globalPalette;
            if (_mapper.Palette != palette)
            {
                _mapper.SetPalette(palette);
            }

            exact = AllColorsExact(pixels, rect);
        }
        else
        {
            palette = _quantizer.BuildPalette(pixels, Width * 4, rect, _options.MaxColors, out exact);
            _mapper.SetPalette(palette);
        }

        int count = rect.Width * rect.Height;
        if (_indices.Length < count)
        {
            _indices = new byte[count];
        }

        GifDithering dithering = exact ? GifDithering.None : _options.Dithering;
        switch (dithering)
        {
            case GifDithering.OrderedBayer4x4:
                MapOrdered(pixels, rect, palette.Length);
                break;
            case GifDithering.FloydSteinberg:
                MapFloydSteinberg(pixels, rect);
                break;
            default:
                MapNearest(pixels, rect);
                break;
        }

        _pending.SetLength(0);
        bool local = _globalPalette is null;
        int tableBits = local ? TableBits(palette.Length) : TableBits(_globalPalette!.Length);
        WriteImageDescriptor(_pending, rect, local ? tableBits : 0);
        if (local)
        {
            WriteColorTable(_pending, palette, tableBits);
        }

        int minCodeSize = Math.Max(2, tableBits);
        _pending.WriteByte((byte)minCodeSize);
        _lzw.Encode(_pending, _indices.AsSpan(0, count), minCodeSize);
        _hasPending = true;
        _pendingDelay = delay;
        _pendingTransparent = false;
        EncodedFrameCount++;
    }

    /// <summary>A 1×1 frame whose only pixel is transparent: it shows nothing new and only adds time.</summary>
    private void EncodeTransparentPixel(int delay)
    {
        _pending.SetLength(0);
        bool local = _globalPalette is null;
        WriteImageDescriptor(_pending, new PixelRect(0, 0, 1, 1), local ? 1 : 0);
        if (local)
        {
            WriteColorTable(_pending, [0, 0], 1);
        }

        _pending.WriteByte(2);
        _lzw.Encode(_pending, [0], 2);
        _hasPending = true;
        _pendingDelay = delay;
        _pendingTransparent = true;
        EncodedFrameCount++;
    }

    private void FlushPending()
    {
        if (!_hasPending)
        {
            return;
        }

        Span<byte> gce = stackalloc byte[8];
        gce[0] = 0x21;
        gce[1] = 0xF9;
        gce[2] = 4;
        gce[3] = (byte)((1 << 2) | (_pendingTransparent ? 1 : 0)); // disposal 1: do not dispose
        BinaryPrimitives.WriteUInt16LittleEndian(gce[4..], (ushort)_pendingDelay);
        gce[6] = 0; // transparent index (only meaningful when the flag is set)
        gce[7] = 0;
        _output.Write(gce);
        _pending.Position = 0;
        _pending.CopyTo(_output);
        _pending.SetLength(0);
        _hasPending = false;
    }

    private static void WriteImageDescriptor(Stream s, PixelRect rect, int localTableBits)
    {
        Span<byte> d = stackalloc byte[10];
        d[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(d[1..], (ushort)rect.X);
        BinaryPrimitives.WriteUInt16LittleEndian(d[3..], (ushort)rect.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(d[5..], (ushort)rect.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(d[7..], (ushort)rect.Height);
        d[9] = localTableBits > 0 ? (byte)(0x80 | (localTableBits - 1)) : (byte)0;
        s.Write(d);
    }

    private static void WriteColorTable(Stream s, int[] palette, int tableBits)
    {
        var table = new byte[3 << tableBits];
        for (int k = 0; k < palette.Length; k++)
        {
            table[k * 3] = (byte)(palette[k] >> 16);
            table[k * 3 + 1] = (byte)(palette[k] >> 8);
            table[k * 3 + 2] = (byte)palette[k];
        }

        s.Write(table);
    }

    /// <summary>Bits per entry of the smallest GIF color table (2..256 entries, a power of two) that fits.</summary>
    private static int TableBits(int colors)
    {
        int bits = 1;
        while ((1 << bits) < colors)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>Bounding rectangle of pixels whose RGB differs; empty if the frames are identical.</summary>
    private PixelRect ChangedRect(byte[] previous, byte[] current)
    {
        ReadOnlySpan<uint> a = MemoryMarshal.Cast<byte, uint>(previous);
        ReadOnlySpan<uint> b = MemoryMarshal.Cast<byte, uint>(current);
        int w = Width;

        int top = 0;
        while (top < Height && RowEqual(a.Slice(top * w, w), b.Slice(top * w, w)))
        {
            top++;
        }

        if (top == Height)
        {
            return PixelRect.Empty;
        }

        int bottom = Height - 1;
        while (bottom > top && RowEqual(a.Slice(bottom * w, w), b.Slice(bottom * w, w)))
        {
            bottom--;
        }

        int left = w, right = -1;
        for (int y = top; y <= bottom; y++)
        {
            ReadOnlySpan<uint> ra = a.Slice(y * w, w), rb = b.Slice(y * w, w);
            for (int x = 0; x < left; x++)
            {
                if (((ra[x] ^ rb[x]) & 0x00FFFFFFu) != 0)
                {
                    left = x;
                    break;
                }
            }

            for (int x = w - 1; x > right; x--)
            {
                if (((ra[x] ^ rb[x]) & 0x00FFFFFFu) != 0)
                {
                    right = x;
                    break;
                }
            }
        }

        return new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    private static bool RowEqual(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        if (a.SequenceEqual(b))
        {
            return true;
        }

        for (int x = 0; x < a.Length; x++)
        {
            if (((a[x] ^ b[x]) & 0x00FFFFFFu) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool AllColorsExact(byte[] pixels, PixelRect rect)
    {
        int[] palette = _mapper.Palette;
        int stride = Width * 4;
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int i = y * stride + rect.X * 4;
            int end = i + rect.Width * 4;
            for (; i < end; i += 4)
            {
                int c = Rgb(pixels, i);
                if (palette[_mapper.Map(c)] != c)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int Rgb(byte[] p, int i) => (p[i + 2] << 16) | (p[i + 1] << 8) | p[i];

    private void MapNearest(byte[] pixels, PixelRect rect)
    {
        int stride = Width * 4;
        int n = 0;
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int i = y * stride + rect.X * 4;
            for (int x = 0; x < rect.Width; x++, i += 4)
            {
                _indices[n++] = _mapper.Map(Rgb(pixels, i));
            }
        }
    }

    private void MapOrdered(byte[] pixels, PixelRect rect, int paletteSize)
    {
        // Spread of the threshold pattern ≈ the typical distance between palette colors.
        double spread = 255.0 / Math.Cbrt(paletteSize);
        Span<int> offsets = stackalloc int[16];
        for (int k = 0; k < 16; k++)
        {
            offsets[k] = (int)Math.Round((Bayer4x4[k] + 0.5) / 16.0 * spread - spread / 2);
        }

        int stride = Width * 4;
        int n = 0;
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int i = y * stride + rect.X * 4;
            for (int x = rect.X; x < rect.Right; x++, i += 4)
            {
                // Indexed by absolute position so the pattern lines up across sub-rectangle frames.
                int o = offsets[((y & 3) << 2) | (x & 3)];
                int r = Clamp(pixels[i + 2] + o), g = Clamp(pixels[i + 1] + o), b = Clamp(pixels[i] + o);
                _indices[n++] = _mapper.Map((r << 16) | (g << 8) | b);
            }
        }
    }

    private void MapFloydSteinberg(byte[] pixels, PixelRect rect)
    {
        int w = rect.Width;
        int[] palette = _mapper.Palette;

        // Errors in 1/16 units, one padding column on each side.
        var current = new int[(w + 2) * 3];
        var next = new int[(w + 2) * 3];
        int stride = Width * 4;
        int n = 0;
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int i = y * stride + rect.X * 4;
            for (int x = 0; x < w; x++, i += 4)
            {
                int e = (x + 1) * 3;
                int r = Clamp(pixels[i + 2] + (current[e] >> 4));
                int g = Clamp(pixels[i + 1] + (current[e + 1] >> 4));
                int b = Clamp(pixels[i] + (current[e + 2] >> 4));
                byte index = _mapper.Map((r << 16) | (g << 8) | b);
                _indices[n++] = index;
                int p = palette[index];
                int er = r - ((p >> 16) & 0xFF), eg = g - ((p >> 8) & 0xFF), eb = b - (p & 0xFF);
                Diffuse(current, e + 3, er, eg, eb, 7);
                Diffuse(next, e - 3, er, eg, eb, 3);
                Diffuse(next, e, er, eg, eb, 5);
                Diffuse(next, e + 3, er, eg, eb, 1);
            }

            (current, next) = (next, current);
            Array.Clear(next);
        }
    }

    private static void Diffuse(int[] errors, int e, int r, int g, int b, int weight)
    {
        errors[e] += r * weight;
        errors[e + 1] += g * weight;
        errors[e + 2] += b * weight;
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
