using System.Buffers.Binary;
using System.IO.Compression;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Storage;

/// <summary>
/// Lossless pixel container used inside SnapForge captures ("SFPX"): 4-byte magic, uint16 version, uint32 width,
/// uint32 height, then zlib-compressed BGRA32 rows (top-down). Platform independent; no image codec needed.
/// </summary>
public static class PixelCodec
{
    public const ushort Version = 1;
    private const int HeaderSize = 14;
    private const long MaxPixels = 32_000L * 32_000L;
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

    public static PixelBuffer Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderSize || !data.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a SnapForge pixel file.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported pixel file version {version}.");
        }

        uint width = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6));
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10));
        if (width == 0 || height == 0 || (long)width * height > MaxPixels)
        {
            throw new InvalidDataException($"Invalid image size {width}×{height}.");
        }

        var image = new PixelBuffer((int)width, (int)height);
        using var input = new MemoryStream(data, HeaderSize, data.Length - HeaderSize, writable: false);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        z.ReadExactly(image.Pixels);
        if (z.ReadByte() != -1)
        {
            throw new InvalidDataException("Pixel data is longer than declared.");
        }

        return image;
    }
}
