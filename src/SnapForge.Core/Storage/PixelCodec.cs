using System.Buffers.Binary;
using System.IO.Compression;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Storage;

/// <summary>
/// Lossless pixel container used inside SnapForge captures ("SFPX"): 4-byte magic, uint16 version, uint32 width,
/// uint32 height, then zlib-compressed BGRA32 rows (top-down). Platform independent; no image codec needed.
/// Decoding is bounded (FA-018): the declared size must fit the <see cref="ImageBudget"/> and the compressed payload
/// must be large enough to possibly hold it (DEFLATE cannot compress better than about 1032:1), both checked before
/// anything is allocated, so a damaged or hostile header cannot trigger a huge allocation.
/// </summary>
public static class PixelCodec
{
    public const ushort Version = 1;
    public const int HeaderSize = 14;

    /// <summary>
    /// Bound used for the "payload too small for the declared size" check. DEFLATE's best possible ratio is about
    /// 1032:1 (a 258-byte match in 2 bits); a slightly larger divisor keeps the check strictly conservative.
    /// </summary>
    internal const int MaxCompressionRatio = 1100;

    private static ReadOnlySpan<byte> Magic => "SFPX"u8;

    public static byte[] Encode(PixelBuffer image, CompressionLevel level = CompressionLevel.Fastest)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[6..], (uint)image.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(header[10..], (uint)image.Height);
        output.Write(header);
        using (var z = new ZLibStream(output, level, leaveOpen: true))
        {
            z.Write(image.Pixels);
        }

        return output.ToArray();
    }

    /// <summary>Reads only the header: the declared size, validated against the budget. Throws <see cref="InvalidDataException"/>.</summary>
    public static (int Width, int Height) ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a SnapForge pixel file.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported pixel file version {version}.");
        }

        uint width = BinaryPrimitives.ReadUInt32LittleEndian(data[6..]);
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        if (!ImageBudget.Fits(width, height))
        {
            throw new InvalidDataException($"Invalid image size {width}×{height} (the limit is {ImageBudget.MaxPixels:N0} pixels).");
        }

        return ((int)width, (int)height);
    }

    public static PixelBuffer Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        (int width, int height) = ReadHeader(data);
        long raw = (long)width * height * PixelBuffer.BytesPerPixel;
        long payload = data.Length - HeaderSize;
        if (payload < 2 || payload < raw / MaxCompressionRatio)
        {
            throw new InvalidDataException($"Pixel data is too short for a {width}×{height} image (the file is damaged).");
        }

        var image = new PixelBuffer(width, height);
        try
        {
            using var input = new MemoryStream(data, HeaderSize, data.Length - HeaderSize, writable: false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            z.ReadExactly(image.Pixels);
            if (z.ReadByte() != -1)
            {
                throw new InvalidDataException("Pixel data is longer than declared.");
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Pixel data is shorter than declared (the file is damaged).", ex);
        }

        return image;
    }
}
