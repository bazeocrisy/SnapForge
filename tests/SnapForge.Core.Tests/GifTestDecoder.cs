using SnapForge.Core.Imaging;

namespace SnapForge.Core.Tests;

/// <summary>
/// A minimal, strict GIF decoder for round-trip tests: parses every block, LZW-decodes each image and
/// composites the frames (disposal 0/1/2) onto a canvas. Independent of the encoder's code.
/// </summary>
internal sealed class GifTestDecoder
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int? LoopCount { get; private set; }
    public bool HasTrailer { get; private set; }
    public bool HasGlobalTable { get; private set; }
    public List<DecodedFrame> Frames { get; } = [];

    internal sealed record DecodedFrame(PixelBuffer Canvas, int Delay, int X, int Y, int W, int H, int Disposal, bool Transparent, int PaletteSize, bool LocalTable);

    public static GifTestDecoder Decode(byte[] data)
    {
        var d = new GifTestDecoder();
        d.Parse(data);
        return d;
    }

    private void Parse(byte[] data)
    {
        int p = 0;
        string signature = System.Text.Encoding.ASCII.GetString(data, 0, 6);
        if (signature != "GIF89a")
        {
            throw new InvalidDataException("Bad signature " + signature);
        }

        p = 6;
        Width = U16(data, p);
        Height = U16(data, p + 2);
        int packed = data[p + 4];
        p += 7;
        int[]? global = null;
        if ((packed & 0x80) != 0)
        {
            HasGlobalTable = true;
            global = ReadTable(data, ref p, 2 << (packed & 7));
        }

        var canvas = new PixelBuffer(Width, Height);
        int delay = 0, disposal = 0, transparentIndex = -1;
        while (true)
        {
            byte block = data[p++];
            if (block == 0x3B)
            {
                HasTrailer = true;
                if (p != data.Length)
                {
                    throw new InvalidDataException("Data after trailer");
                }

                return;
            }

            if (block == 0x21)
            {
                byte label = data[p++];
                if (label == 0xF9)
                {
                    if (data[p] != 4)
                    {
                        throw new InvalidDataException("Bad GCE size");
                    }

                    int flags = data[p + 1];
                    disposal = (flags >> 2) & 7;
                    delay = U16(data, p + 2);
                    transparentIndex = (flags & 1) != 0 ? data[p + 4] : -1;
                    p += 5;
                    if (data[p++] != 0)
                    {
                        throw new InvalidDataException("GCE terminator");
                    }
                }
                else if (label == 0xFF)
                {
                    int size = data[p++];
                    string app = System.Text.Encoding.ASCII.GetString(data, p, size);
                    p += size;
                    List<byte> sub = ReadSubBlocks(data, ref p);
                    if (app == "NETSCAPE2.0" && sub.Count == 3 && sub[0] == 1)
                    {
                        LoopCount = sub[1] | (sub[2] << 8);
                    }
                }
                else
                {
                    ReadSubBlocks(data, ref p);
                }

                continue;
            }

            if (block != 0x2C)
            {
                throw new InvalidDataException($"Unexpected block 0x{block:X2} at {p - 1}");
            }

            int x = U16(data, p), y = U16(data, p + 2), w = U16(data, p + 4), h = U16(data, p + 6);
            int ip = data[p + 8];
            p += 9;
            if (x + w > Width || y + h > Height || w == 0 || h == 0)
            {
                throw new InvalidDataException("Frame outside the logical screen");
            }

            if ((ip & 0x40) != 0)
            {
                throw new InvalidDataException("Interlace not expected");
            }

            int[] table = (ip & 0x80) != 0 ? ReadTable(data, ref p, 2 << (ip & 7)) : global ?? throw new InvalidDataException("No color table");
            int minCodeSize = data[p++];
            List<byte> compressed = ReadSubBlocks(data, ref p);
            byte[] indices = LzwDecode(compressed.ToArray(), minCodeSize, w * h);

            PixelBuffer before = canvas.Clone();
            for (int row = 0; row < h; row++)
            {
                for (int col = 0; col < w; col++)
                {
                    int index = indices[row * w + col];
                    if (index == transparentIndex)
                    {
                        continue;
                    }

                    if (index >= table.Length)
                    {
                        throw new InvalidDataException("Index outside color table");
                    }

                    int c = table[index];
                    canvas.SetPixel(x + col, y + row, new ArgbColor(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
                }
            }

            Frames.Add(new DecodedFrame(canvas.Clone(), delay, x, y, w, h, disposal, transparentIndex >= 0, table.Length, (ip & 0x80) != 0));
            if (disposal == 2)
            {
                canvas.FillRect(new Geometry.PixelRect(x, y, w, h), ArgbColor.Black);
            }
            else if (disposal == 3)
            {
                canvas = before;
            }

            delay = 0;
            disposal = 0;
            transparentIndex = -1;
        }
    }

    /// <summary>Textbook GIF LZW decoder (deferred table entries, code width grows when the next code reaches 2^width).</summary>
    public static byte[] LzwDecode(byte[] data, int minCodeSize, int expectedPixels)
    {
        if (minCodeSize is < 2 or > 8)
        {
            throw new InvalidDataException("Bad LZW minimum code size");
        }

        int clear = 1 << minCodeSize, end = clear + 1;
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var length = new int[4096];
        for (int i = 0; i < clear; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            length[i] = 1;
        }

        var output = new List<byte>(expectedPixels);
        int width = minCodeSize + 1;
        int next = end + 1;
        int previous = -1;
        int bitPos = 0;
        bool sawClear = false;
        var stack = new byte[4097];
        while (true)
        {
            if (bitPos + width > data.Length * 8)
            {
                throw new InvalidDataException("LZW data ended without an end code");
            }

            int code = 0;
            for (int b = 0; b < width; b++, bitPos++)
            {
                code |= ((data[bitPos >> 3] >> (bitPos & 7)) & 1) << b;
            }

            if (code == clear)
            {
                sawClear = true;
                width = minCodeSize + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (!sawClear)
            {
                throw new InvalidDataException("LZW stream must start with a clear code");
            }

            if (code == end)
            {
                break;
            }

            int emitCode;
            byte first;
            if (previous == -1)
            {
                if (code >= clear)
                {
                    throw new InvalidDataException("First code after clear must be a literal");
                }

                output.Add((byte)code);
                previous = code;
                continue;
            }

            if (code < next)
            {
                emitCode = code;
            }
            else if (code == next)
            {
                emitCode = previous; // KwKwK case
            }
            else
            {
                throw new InvalidDataException($"LZW code {code} beyond table ({next})");
            }

            int sp = 0;
            int c = emitCode;
            while (c != -1)
            {
                stack[sp++] = suffix[c];
                c = prefix[c];
            }

            first = stack[sp - 1];
            for (int i = sp - 1; i >= 0; i--)
            {
                output.Add(stack[i]);
            }

            if (code == next)
            {
                output.Add(first);
            }

            if (next < 4096)
            {
                prefix[next] = previous;
                suffix[next] = code == next ? first : FirstOf(code, prefix, suffix);
                length[next] = length[previous] + 1;
                next++;
                if (next == (1 << width) && width < 12)
                {
                    width++;
                }
            }

            previous = code;
        }

        if (output.Count != expectedPixels)
        {
            throw new InvalidDataException($"Decoded {output.Count} pixels, expected {expectedPixels}");
        }

        return output.ToArray();
    }

    private static byte FirstOf(int code, int[] prefix, byte[] suffix)
    {
        while (prefix[code] != -1)
        {
            code = prefix[code];
        }

        return suffix[code];
    }

    private static int U16(byte[] d, int p) => d[p] | (d[p + 1] << 8);

    private static int[] ReadTable(byte[] data, ref int p, int entries)
    {
        var table = new int[entries];
        for (int i = 0; i < entries; i++, p += 3)
        {
            table[i] = (data[p] << 16) | (data[p + 1] << 8) | data[p + 2];
        }

        return table;
    }

    private static List<byte> ReadSubBlocks(byte[] data, ref int p)
    {
        var bytes = new List<byte>();
        while (true)
        {
            int size = data[p++];
            if (size == 0)
            {
                return bytes;
            }

            for (int i = 0; i < size; i++)
            {
                bytes.Add(data[p++]);
            }
        }
    }
}
