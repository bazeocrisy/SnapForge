using System.IO;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Video;

/// <summary>Basic facts about a video file, read with Media Foundation.</summary>
internal sealed record VideoFileInfo(int Width, int Height, int FramesPerSecond, long DurationMs, bool HasAudio);

/// <summary>
/// Video editing exports built on Windows' own decoder and encoder (Media Foundation): trimmed / cut MP4 with audio
/// kept in sync (audio is cut at sample precision on the same timeline as the frames), still frames, and frame
/// sequences for GIF export. The source file is only read; every export writes a new file, first under a temporary
/// name that is renamed into place when complete, so a failed or cancelled export never leaves a half-written file
/// under the final name.
/// </summary>
internal static unsafe class VideoExporter
{
    public static VideoFileInfo Inspect(string path)
    {
        EnsureStarted();
        nint reader = VideoAnalysis.OpenDecoding(path, video: true, audio: false);
        try
        {
            nint type = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            ulong size = Mf.GetUInt64(type, Mf.MT_FRAME_SIZE) ?? 0;
            ulong rate = Mf.GetUInt64(type, Mf.MT_FRAME_RATE) ?? 0;
            Com.Release(ref type);
            int fps = rate == 0 || (rate & 0xFFFFFFFF) == 0 ? 30 : (int)Math.Round((double)(rate >> 32) / (rate & 0xFFFFFFFF));
            long duration = (Mf.GetDuration(reader) ?? 0) / 10_000;
            bool audio = HasAudioStream(path);
            return new VideoFileInfo((int)(size >> 32), (int)(size & 0xFFFFFFFF), Math.Clamp(fps, 1, 120), duration, audio);
        }
        finally
        {
            Com.Release(ref reader);
        }
    }

    /// <summary>
    /// Writes the kept ranges of <paramref name="source"/> to <paramref name="destination"/> (MP4, H.264 + AAC).
    /// <paramref name="progress"/> receives 0..1. Throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public static void ExportEdited(string source, string destination, VideoEditPlan plan, VideoQuality quality,
        IProgress<double>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureStarted();
        VideoFileInfo info = Inspect(source);
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new InvalidDataException("The video has no picture that Windows can decode.");
        }

        // H.264 needs even dimensions; drop the last row/column if the source is odd.
        int w = info.Width & ~1, h = info.Height & ~1;
        string temp = destination + ".partial";
        File.Delete(temp);
        try
        {
            var format = new VideoStreamFormat(w, h, info.FramesPerSecond, VideoSettings.BitRate(w, h, info.FramesPerSecond, quality));
            using (var writer = new Mp4Writer(temp, format, info.HasAudio, fragmented: false, EncoderInput.SystemMemory, 0))
            {
                long total100ns = Math.Max(1, plan.OutputDurationMs * 10_000);
                long frameDuration = 10_000_000L / info.FramesPerSecond;
                nint reader = VideoAnalysis.OpenDecoding(source, video: true, audio: info.HasAudio);
                try
                {
                    // Video. Each kept frame is placed on the output timeline; frames in cut sections are skipped.
                    while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            long? outTs = plan.MapToOutput100ns(ts);
                            if (outTs is null)
                            {
                                continue;
                            }

                            nint buffer = Mf.ConvertToContiguousBuffer(sample);
                            try
                            {
                                byte* data = VideoAnalysis.LockDecodedRows(buffer, info.Height, out int stride, out bool locked2D);
                                try
                                {
                                    writer.WriteFrame(data, stride, outTs.Value, frameDuration); // stride may be negative (bottom-up)
                                }
                                finally
                                {
                                    Mf.UnlockRows(buffer, locked2D);
                                }
                            }
                            finally
                            {
                                Com.Release(ref buffer);
                            }

                            progress?.Report(Math.Min(0.95, outTs.Value / (double)total100ns * (info.HasAudio ? 0.8 : 0.95)));
                        }
                        finally
                        {
                            Com.Release(ref sample);
                        }
                    }

                    // Audio: 16-bit stereo 48 kHz PCM, cut at sample precision with the same mapping.
                    if (info.HasAudio)
                    {
                        while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out long ts, out nint sample) && sample != 0)
                        {
                            try
                            {
                                cancel.ThrowIfCancellationRequested();
                                nint buffer = Mf.ConvertToContiguousBuffer(sample);
                                try
                                {
                                    byte* data = Mf.LockBuffer(buffer, out _, out int length);
                                    try
                                    {
                                        int frames = length / 4;
                                        long blockLength = frames * 10_000_000L / 48_000;
                                        foreach ((long off, long len, long outTs) in plan.KeptPartsOfBlock(ts, blockLength))
                                        {
                                            int first = (int)Math.Round(off * 48_000 / 10_000_000.0);
                                            int count = (int)Math.Round(len * 48_000 / 10_000_000.0);
                                            count = Math.Min(count, frames - first);
                                            if (count > 0)
                                            {
                                                writer.WriteAudio(new ReadOnlySpan<short>((short*)data + (first * 2), count * 2), outTs);
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        Mf.UnlockBuffer(buffer);
                                    }
                                }
                                finally
                                {
                                    Com.Release(ref buffer);
                                }

                                progress?.Report(Math.Min(0.95, 0.8 + (0.15 * ((plan.MapToOutput100ns(ts) ?? 0) / (double)total100ns))));
                            }
                            finally
                            {
                                Com.Release(ref sample);
                            }
                        }
                    }
                }
                finally
                {
                    Com.Release(ref reader);
                }

                if (writer.FramesWritten == 0)
                {
                    throw new InvalidDataException("No frames were in the kept part of the video.");
                }

                writer.Complete();
            }

            File.Move(temp, destination, overwrite: true);
            progress?.Report(1);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Decoded frames from [startMs, endMs) sampled at <paramref name="fps"/>, scaled to <paramref name="maxWidth"/>.</summary>
    public static IEnumerable<(PixelBuffer Frame, long TimeMs)> SampleFrames(string source, long startMs, long endMs, int fps, int maxWidth, CancellationToken cancel)
    {
        EnsureStarted();
        VideoFileInfo info = Inspect(source);
        double step = 1000.0 / Math.Clamp(fps, 1, 50);
        double next = startMs;
        nint reader = VideoAnalysis.OpenDecoding(source, video: true, audio: false);
        try
        {
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                PixelBuffer? frame = null;
                long ms = ts / 10_000;
                try
                {
                    cancel.ThrowIfCancellationRequested();
                    if (ms >= endMs)
                    {
                        break;
                    }

                    if (ms + (step / 2) < next)
                    {
                        continue;
                    }

                    frame = ReadFrame(sample, info.Width, info.Height);
                }
                finally
                {
                    Com.Release(ref sample);
                }

                while (next <= ms + (step / 2))
                {
                    next += step;
                }

                if (frame.Width > maxWidth)
                {
                    int nh = Math.Max(1, (int)Math.Round(frame.Height * (maxWidth / (double)frame.Width)));
                    frame = ImageOps.Resize(frame, maxWidth, nh);
                }

                yield return (frame, ms);
            }
        }
        finally
        {
            Com.Release(ref reader);
        }
    }

    private static PixelBuffer ReadFrame(nint sample, int w, int h)
    {
        nint buffer = Mf.ConvertToContiguousBuffer(sample);
        try
        {
            byte* data = VideoAnalysis.LockDecodedRows(buffer, h, out int stride, out bool locked2D);
            try
            {
                var pixels = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                {
                    new ReadOnlySpan<byte>(data + ((long)y * stride), w * 4).CopyTo(pixels.AsSpan(y * w * 4));
                }

                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }

                return new PixelBuffer(w, h, pixels);
            }
            finally
            {
                Mf.UnlockRows(buffer, locked2D);
            }
        }
        finally
        {
            Com.Release(ref buffer);
        }
    }

    private static bool HasAudioStream(string path)
    {
        try
        {
            nint reader = VideoAnalysis.OpenDecoding(path, video: false, audio: true);
            try
            {
                return Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out _, out nint s) && Release(s);
            }
            finally
            {
                Com.Release(ref reader);
            }
        }
        catch (NativeCallException)
        {
            return false;
        }

        static bool Release(nint s)
        {
            bool any = s != 0;
            Com.Release(ref s);
            return any;
        }
    }

    private static int _started;

    private static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = Mf.MFStartup(Mf.MF_VERSION, 0);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
