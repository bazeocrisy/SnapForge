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
/// GPS, comments, colour profile names) is discarded. MP4 videos are copied into the Library as recordings after
/// Windows' decoder has confirmed they play (H.264/AAC MP4; streamed, never loaded into memory). The original file
/// is never modified.
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
                if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ImportOutcome(path, await ImportVideoAsync(services, path).ConfigureAwait(true), null));
                    continue;
                }

                CaptureRecord record = await services.Writer.RunAsync(() =>
                {
                    PixelBuffer image = Decode(path);
                    return services.Library.Create(new CaptureContent(image, []), image, DateTime.UtcNow, ImageImport.TitleFor(path));
                }).ConfigureAwait(true);
                results.Add(new ImportOutcome(path, record, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or Video.Interop.NativeCallException)
            {
                results.Add(new ImportOutcome(path, null, ex.Message));
            }
        }

        return results;
    }

    /// <summary>Copies an MP4 into the Library as a video item (after checking that Windows can decode it).</summary>
    private static async Task<CaptureRecord> ImportVideoAsync(AppServices services, string path)
    {
        Video.VideoFileInfo info;
        try
        {
            info = await Task.Run(() => Video.VideoExporter.Inspect(path)).ConfigureAwait(true);
        }
        catch (Video.Interop.NativeCallException ex)
        {
            throw new InvalidDataException("Windows cannot play this video (only MP4 with H.264 video is supported).", ex);
        }

        if (info.Width <= 0 || info.Height <= 0 || info.DurationMs <= 0)
        {
            throw new InvalidDataException("The file has no playable video.");
        }

        Core.Imaging.PixelBuffer? thumb = await Task.Run(() => Video.VideoAnalysis.DecodeFrame(path, Math.Min(1000, info.DurationMs / 2))).ConfigureAwait(true);
        return await services.Writer.RunAsync(() =>
        {
            (Guid id, _) = services.Library.BeginVideo(DateTime.UtcNow);
            try
            {
                File.Copy(path, services.Library.VideoPath(id));
                return services.Library.CompleteVideo(id, new VideoInfo(info.Width, info.Height, info.FramesPerSecond, info.DurationMs, info.HasAudio),
                    thumb, DateTime.UtcNow, Path.GetFileNameWithoutExtension(path));
            }
            catch
            {
                services.Library.AbandonVideo(id);
                throw;
            }
        }).ConfigureAwait(true);
    }
}
