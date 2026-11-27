using System.Buffers.Binary;
using System.IO.Compression;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>FA-018: decoded images and image operations stay within a documented memory budget.</summary>
public class ImageBudgetTests
{
    private static byte[] Header(uint width, uint height, ushort version = PixelCodec.Version)
    {
        byte[] h = new byte[PixelCodec.HeaderSize];
        "SFPX"u8.CopyTo(h);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(6), width);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(10), height);
        return h;
    }

    private static byte[] Zlib(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(raw);
        }

        return ms.ToArray();
    }

    private static long AllocatedDuring(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void FA018_budget_is_one_GiB_of_BGRA()
    {
        Assert.Equal(268_435_456, ImageBudget.MaxPixels);
        Assert.Equal(1L << 30, ImageBudget.MaxBytes);
        Assert.True(ImageBudget.Fits(16384, 16384));
        Assert.False(ImageBudget.Fits(16384, 16385));
        Assert.False(ImageBudget.Fits(32000, 32000));
        Assert.False(ImageBudget.Fits(0, 10));
        Assert.False(ImageBudget.Fits(uint.MaxValue, uint.MaxValue));
    }

    [Theory]
    [InlineData(32000u, 32000u)] // ≈ 4 GB BGRA: accepted before FA-018
    [InlineData(16384u, 16385u)]
    [InlineData(uint.MaxValue, 1u)]
    [InlineData(1u, uint.MaxValue)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    [InlineData(0u, 5u)]
    [InlineData(5u, 0u)]
    public void FA018_malformed_or_huge_dimension_headers_are_rejected_before_allocating(uint w, uint h)
    {
        byte[] data = [.. Header(w, h), .. Zlib(new byte[64])];
        long allocated = AllocatedDuring(() => Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(data)));
        Assert.True(allocated < 1_000_000, $"{allocated} bytes allocated while rejecting a {w}×{h} header");
    }

    [Fact]
    public void FA018_payload_too_small_for_declared_size_is_rejected_before_allocating()
    {
        // A compressed-bomb-like file: declares the full 1 GiB budget but carries a few bytes. DEFLATE cannot reach
        // the ratio needed, so it is rejected from the header alone, without allocating the gigabyte.
        byte[] data = [.. Header(16384, 16384), .. Zlib(new byte[4096])];
        long allocated = AllocatedDuring(() => Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(data)));
        Assert.True(allocated < 1_000_000, $"{allocated} bytes allocated");
    }

    [Fact]
    public void FA018_payload_shorter_or_longer_than_declared_is_invalid_data()
    {
        byte[] shortData = [.. Header(64, 64), .. Zlib(new byte[(64 * 64 * 4) - 4])];
        Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(shortData));
        byte[] longData = [.. Header(8, 8), .. Zlib(new byte[(8 * 8 * 4) + 1])];
        Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(longData));
        byte[] garbage = [.. Header(8, 8), 1, 2, 3, 4, 5, 6, 7, 8];
        Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(garbage));
        Assert.Throws<InvalidDataException>(() => PixelCodec.Decode(Header(8, 8, version: 2)));
    }

    [Fact]
    public void FA018_highly_compressible_real_images_still_decode()
    {
        // A uniform 4K frame compresses extremely well (well beyond 100:1) and must still be accepted.
        var uniform = new PixelBuffer(3840, 2160);
        uniform.FillRect(uniform.Bounds, ArgbColor.White);
        byte[] encoded = PixelCodec.Encode(uniform, CompressionLevel.SmallestSize);
        Assert.Equal(uniform.Pixels, PixelCodec.Decode(encoded).Pixels);

        var wide = new PixelBuffer(5000, 3000);
        wide.FillRect(new PixelRect(0, 0, 5000, 1500), ArgbColor.Black);
        Assert.Equal(wide.Pixels, PixelCodec.Decode(PixelCodec.Encode(wide)).Pixels);
    }

    [Fact]
    public void FA018_pixel_buffer_creation_is_bounded_with_checked_arithmetic()
    {
        // Before: width*height*4 overflowed int (OverflowException) or allocated gigabytes.
        long allocated = AllocatedDuring(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(16384, 16385));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(int.MaxValue, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(65536, 65536));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(-1, 10));
        });
        Assert.True(allocated < 1_000_000, $"{allocated} bytes allocated");
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(65536, 65536, new byte[16]));
        Assert.Equal(3840 * 2160 * 4, new PixelBuffer(3840, 2160).Pixels.Length);
    }

    [Fact]
    public void FA018_image_operations_reject_results_beyond_the_budget_before_allocating()
    {
        var small = new PixelBuffer(4, 4);
        long allocated = AllocatedDuring(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Resize(small, 16385, 16384));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.ResizeCanvas(small, 20000, 20000, CanvasAnchor.Center, ArgbColor.White));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.AddBorder(small, 8191, ArgbColor.White)); // 16386²
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.AddBorder(small, int.MaxValue / 2, ArgbColor.White));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.AddDropShadow(small, 32768, 32768, 0, ArgbColor.Black, ArgbColor.White));
        });
        Assert.True(allocated < 1_000_000, $"{allocated} bytes allocated");

        // Combine: the summed extent overflows the budget although each input is small.
        var strip = new PixelBuffer(16384, 1);
        PixelBuffer[] many = [.. Enumerable.Repeat(strip, 3)];
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Combine(many, CombineDirection.Vertical, 16384, ArgbColor.White, CombineAlignment.Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Combine(many, CombineDirection.Horizontal, int.MaxValue, ArgbColor.White, CombineAlignment.Start));
    }

    [Fact]
    public void FA018_scrolling_capture_limit_of_120M_pixels_is_within_budget()
    {
        _ = new ScrollStitcher(new ScrollStitcherOptions { MaxOutputPixels = 120_000_000, MaxLength = 30_000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollStitcher(new ScrollStitcherOptions { MaxOutputPixels = ImageBudget.MaxPixels + 1 }));
        Assert.True(ImageBudget.Fits(4000, 30_000)); // 120M pixels
    }
}
