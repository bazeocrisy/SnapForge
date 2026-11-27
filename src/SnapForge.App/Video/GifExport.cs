using System.IO;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Video;

/// <summary>
/// Animated GIF export of part of a video: frames are decoded and sampled at the chosen frame rate, scaled to the
/// chosen width and streamed into the GIF encoder (only one frame in memory at a time). Written to a temporary file
/// and renamed when complete.
/// </summary>
internal static class GifExport
{
    /// <summary>Writes [startMs, endMs) of <paramref name="source"/> to <paramref name="target"/>; returns the file size.</summary>
    public static long Write(string source, string target, long startMs, long endMs, GifExportSettings settings, IProgress<double>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string temp = target + ".partial";
        try
        {
            using (FileStream file = File.Create(temp))
            {
                GifEncoder? encoder = null;
                try
                {
                    int delayCs = Math.Max(GifEncoder.MinDelayCentiseconds, (int)Math.Round(100.0 / settings.FramesPerSecond));
                    long span = Math.Max(1, endMs - startMs);
                    foreach ((PixelBuffer frame, long ms) in VideoExporter.SampleFrames(source, startMs, endMs, settings.FramesPerSecond, settings.MaxWidth, cancel))
                    {
                        encoder ??= new GifEncoder(file, frame.Width, frame.Height, new GifEncoderOptions
                        {
                            // 0 = repeat forever (NETSCAPE loop extension); without looping the animation plays once.
                            LoopCount = settings.Loop ? 0 : -1, // 0 = forever; -1 = no loop extension (plays once)
                            Dithering = settings.Dither ? GifDithering.FloydSteinberg : GifDithering.None,
                        }, leaveOpen: true);
                        encoder.AddFrame(frame, delayCs);
                        progress?.Report(Math.Clamp((ms - startMs) / (double)span, 0, 0.99));
                    }

                    if (encoder is null)
                    {
                        throw new InvalidDataException("No frames were found in the chosen range.");
                    }

                    encoder.Finish();
                }
                finally
                {
                    encoder?.Dispose();
                }
            }

            File.Move(temp, target, overwrite: true);
            progress?.Report(1);
            return new FileInfo(target).Length;
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }
}
