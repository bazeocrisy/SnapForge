using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Rendering;

internal static class BitmapConversion
{
    /// <summary>
    /// 96 DPI means 1 WPF unit = 1 image pixel. The editor and the exporter both rely on this so annotation
    /// coordinates, rendering and saved output agree exactly.
    /// </summary>
    public const double ImageDpi = 96;

    public static BitmapSource ToFrozenBitmap(PixelBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        BitmapSource bitmap = BitmapSource.Create(buffer.Width, buffer.Height, ImageDpi, ImageDpi, PixelFormats.Bgra32, null, buffer.Pixels, buffer.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static WriteableBitmap ToWriteableBitmap(PixelBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var bitmap = new WriteableBitmap(buffer.Width, buffer.Height, ImageDpi, ImageDpi, PixelFormats.Bgra32, null);
        CopyInto(buffer, bitmap);
        return bitmap;
    }

    public static void CopyInto(PixelBuffer buffer, WriteableBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(bitmap);
        bitmap.WritePixels(new Int32Rect(0, 0, buffer.Width, buffer.Height), buffer.Pixels, buffer.Stride, 0);
    }

    public static PixelBuffer FromBitmapSource(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var buffer = new PixelBuffer(bgra.PixelWidth, bgra.PixelHeight);
        bgra.CopyPixels(buffer.Pixels, buffer.Stride, 0);
        return buffer;
    }

    public static Color ToMediaColor(ArgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
