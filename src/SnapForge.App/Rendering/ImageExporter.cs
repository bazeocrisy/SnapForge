using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Core.Annotations;
using SnapForge.Core.Export;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Rendering;

/// <summary>Produces the final flattened image and writes it to files or the clipboard.</summary>
internal static class ImageExporter
{
    /// <summary>
    /// Flattens annotations onto the (already redacted) pixel buffer. Annotations are rendered onto a transparent
    /// layer and blended by <see cref="ImageCompositor"/>, so every untouched pixel is bit-identical to the
    /// capture and redacted pixels remain exactly the redaction color.
    /// </summary>
    public static PixelBuffer Flatten(PixelBuffer image, IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(annotations);
        if (annotations.Count == 0)
        {
            return image.Clone();
        }

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            foreach (Annotation annotation in annotations)
            {
                AnnotationRenderer.Draw(dc, annotation);
            }
        }

        var target = new RenderTargetBitmap(image.Width, image.Height, BitmapConversion.ImageDpi, BitmapConversion.ImageDpi, PixelFormats.Pbgra32);
        target.Render(visual);
        byte[] overlay = new byte[image.Pixels.Length];
        target.CopyPixels(overlay, image.Stride, 0);
        return ImageCompositor.ComposeOver(image, overlay);
    }

    /// <summary>Encodes to PNG or JPEG. No metadata (EXIF, location, user name) is written.</summary>
    public static byte[] Encode(PixelBuffer image, ImageFileFormat format) => Encode(image, format, ExportRules.JpegQuality);

    /// <summary>Encodes with an explicit JPEG quality (1–100; ignored for PNG). No metadata is written.</summary>
    public static byte[] Encode(PixelBuffer image, ImageFileFormat format, int jpegQuality)
    {
        ArgumentNullException.ThrowIfNull(image);
        BitmapSource source = new FormatConvertedBitmap(BitmapConversion.ToFrozenBitmap(image), PixelFormats.Bgr24, null, 0);
        BitmapEncoder encoder = format switch
        {
            ImageFileFormat.Png => new PngBitmapEncoder(),
            ImageFileFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes via a temporary file in the same folder and then renames it, so a failure (disk full, permission)
    /// never leaves a truncated image behind or destroys an existing file.
    /// </summary>
    public static async Task SaveAsync(PixelBuffer image, string path, ImageFileFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = Encode(image, format);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new IOException($"Invalid path: {path}");
        string temp = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(true);
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Places the image on the clipboard as a device-independent bitmap (for Office, Paint, chat apps) and as PNG
    /// (for browsers and apps that prefer lossless). Retries briefly because another app may hold the clipboard.
    /// </summary>
    public static async Task CopyToClipboardAsync(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        BitmapSource bitmap = BitmapConversion.ToFrozenBitmap(image);
        var data = new DataObject();
        data.SetImage(bitmap);
        data.SetData("PNG", new MemoryStream(Encode(image, ImageFileFormat.Png)), autoConvert: false);

        const int attempts = 6;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (COMException) when (attempt < attempts)
            {
                await Task.Delay(60 * attempt).ConfigureAwait(true);
            }
        }
    }
}
