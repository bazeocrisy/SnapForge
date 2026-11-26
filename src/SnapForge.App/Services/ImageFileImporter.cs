using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.App.Services;

/// <summary>Result of importing one file.</summary>
internal sealed record ImportOutcome(string Path, CaptureRecord? Record, string? Error);

/// <summary>
/// Imports PNG, JPEG and BMP files into the Capture Library. Only decoded pixels are stored: file metadata (EXIF,
/// GPS, comments, colour profile names) is discarded. The original file is never modified.
/// </summary>
internal static class ImageFileImporter
{
    /// <summary>Decodes a file into an opaque capture image (UI or background thread).</summary>
    public static PixelBuffer Decode(string path)
    {
        if (!ImageImport.IsSupportedPath(path))
        {
            throw new InvalidDataException("Only PNG, JPEG and BMP images can be imported.");
        }

        using FileStream stream = File.OpenRead(path);
        BitmapDecoder decoder;
        try
        {
            // IgnoreColorProfile keeps pixels as stored; OnLoad reads everything so the file is released.
            decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new InvalidDataException("The file is not a readable image.", ex);
        }

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("The file contains no image.");
        }

        BitmapSource frame = decoder.Frames[0];
        if (ImageImport.CheckSize(frame.PixelWidth, frame.PixelHeight) is { } problem)
        {
            throw new InvalidDataException(problem);
        }

        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        bgra.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return ImageImport.FromStraightBgra(frame.PixelWidth, frame.PixelHeight, pixels);
    }

    /// <summary>Imports each file as a new capture (serialized with other library writes).</summary>
    public static async Task<IReadOnlyList<ImportOutcome>> ImportAsync(AppServices services, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        var results = new List<ImportOutcome>();
        foreach (string path in paths)
        {
            try
            {
                CaptureRecord record = await services.Writer.RunAsync(() =>
                {
                    PixelBuffer image = Decode(path);
                    return services.Library.Create(new CaptureContent(image, []), image, DateTime.UtcNow, ImageImport.TitleFor(path));
                }).ConfigureAwait(true);
                results.Add(new ImportOutcome(path, record, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                results.Add(new ImportOutcome(path, null, ex.Message));
            }
        }

        return results;
    }
}
