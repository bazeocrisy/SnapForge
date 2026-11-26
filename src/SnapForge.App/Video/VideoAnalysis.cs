using SnapForge.App.Video.Interop;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Video;

/// <summary>Audio/video sync measured from a finished file (VID-R01).</summary>
internal sealed record SyncReport(
    IReadOnlyList<double> FlashesMs,
    IReadOnlyList<double> BeepsMs,
    IReadOnlyList<double> OffsetsMs,
    double MeanOffsetMs,
    double MaxAbsOffsetMs,
    double DriftMsPerMinute,
    int VideoFrames,
    double VideoDurationMs,
    double AudioDurationMs);

/// <summary>
/// Reads recordings back with the Windows decoders, without loading the file into memory: decodes frames and audio
/// for sync measurement, extracts a thumbnail frame and checks that a file plays to its end.
/// </summary>
internal static unsafe class VideoAnalysis
{
    /// <summary>
    /// Finds every white "flash" frame and every beep onset in a recording made from the synthetic sources and pairs
    /// them: offset = audio onset − video flash (positive = sound late). Drift is the slope of the offsets over time.
    /// </summary>
    public static SyncReport MeasureSync(string path, double flashEveryMs)
    {
        var flashes = new List<double>();
        var beeps = new List<double>();
        int frames = 0;
        double lastVideo = 0, lastAudio = 0;
        nint reader = OpenDecoding(path, video: true, audio: true);
        try
        {
            bool inFlash = false;
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                frames++;
                double brightness = MeanLuma(sample);
                Com.Release(ref sample);
                double ms = ts / 10_000.0;
                lastVideo = ms;
                if (brightness > 200 && !inFlash)
                {
                    flashes.Add(ms);
                }

                inFlash = brightness > 200;
            }

            // Beep onset: first sample whose amplitude exceeds 0.1 after ≥ 100 ms of silence.
            double quietSince = double.NegativeInfinity;
            bool loud = false;
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                nint buffer = Mf.ConvertToContiguousBuffer(sample);
                try
                {
                    byte* data = Mf.LockBuffer(buffer, out _, out int length);
                    try
                    {
                        short* pcm = (short*)data;
                        int count = length / 4; // stereo 16-bit frames
                        for (int i = 0; i < count; i++)
                        {
                            double t = (ts / 10_000.0) + (i * 1000.0 / 48000);
                            bool now = Math.Abs(pcm[i * 2] / 32768.0) > 0.1;
                            if (now && !loud && t - quietSince >= 100)
                            {
                                beeps.Add(t);
                            }

                            if (now)
                            {
                                quietSince = t;
                            }

                            loud = now || (loud && t - quietSince < 5);
                            lastAudio = t;
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
                    Com.Release(ref sample);
                }
            }
        }
        finally
        {
            Com.Release(ref reader);
        }

        // Pair each flash with the nearest beep within half the flash interval.
        var offsets = new List<double>();
        var times = new List<double>();
        foreach (double f in flashes)
        {
            double best = double.NaN;
            foreach (double b in beeps)
            {
                if (Math.Abs(b - f) < flashEveryMs / 2 && (double.IsNaN(best) || Math.Abs(b - f) < Math.Abs(best)))
                {
                    best = b - f;
                }
            }

            if (!double.IsNaN(best))
            {
                offsets.Add(best);
                times.Add(f);
            }
        }

        double mean = offsets.Count == 0 ? double.NaN : offsets.Average();
        double max = offsets.Count == 0 ? double.NaN : offsets.Max(Math.Abs);
        double drift = Slope(times, offsets) * 60_000;
        return new SyncReport(flashes, beeps, offsets, Math.Round(mean, 2), Math.Round(max, 2), Math.Round(drift, 3), frames, lastVideo, lastAudio);
    }

    /// <summary>A decoded frame near <paramref name="atMs"/> as a BGRA buffer (thumbnail of recovered recordings).</summary>
    public static PixelBuffer? DecodeFrame(string path, double atMs = 0)
    {
        nint reader = OpenDecoding(path, video: true, audio: false);
        try
        {
            nint type = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            ulong size = Mf.GetUInt64(type, Mf.MT_FRAME_SIZE) ?? 0;
            Com.Release(ref type);
            int w = (int)(size >> 32), h = (int)(size & 0xFFFFFFFF);
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                try
                {
                    if (ts / 10_000.0 + 1 < atMs)
                    {
                        continue;
                    }

                    nint buffer = Mf.ConvertToContiguousBuffer(sample);
                    try
                    {
                        byte* data = Mf.LockBuffer(buffer, out _, out int length);
                        try
                        {
                            var pixels = new byte[w * h * 4];
                            int stride = length / h;
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
                            Mf.UnlockBuffer(buffer);
                        }
                    }
                    finally
                    {
                        Com.Release(ref buffer);
                    }
                }
                finally
                {
                    Com.Release(ref sample);
                }
            }

            return null;
        }
        finally
        {
            Com.Release(ref reader);
        }
    }

    /// <summary>Opens a file with decoding to RGB32 video / 16-bit PCM audio.</summary>
    private static nint OpenDecoding(string path, bool video, bool audio)
    {
        nint attrs = Mf.CreateAttributes(1);
        nint reader;
        try
        {
            Mf.SetUInt32(attrs, Mf.SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            reader = Mf.CreateSourceReader(path, attrs);
        }
        finally
        {
            Com.Release(ref attrs);
        }

        try
        {
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            if (video)
            {
                Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
                nint t = Mf.CreateMediaType();
                Mf.SetGuid(t, Mf.MT_MAJOR_TYPE, Mf.MediaType_Video);
                Mf.SetGuid(t, Mf.MT_SUBTYPE, Mf.VideoFormat_RGB32);
                Mf.SetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, t);
                Com.Release(ref t);
            }

            if (audio)
            {
                Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_FIRST_AUDIO_STREAM, true);
                nint t = Mf.CreateMediaType();
                Mf.SetGuid(t, Mf.MT_MAJOR_TYPE, Mf.MediaType_Audio);
                Mf.SetGuid(t, Mf.MT_SUBTYPE, Mf.AudioFormat_PCM);
                Mf.SetUInt32(t, Mf.MT_AUDIO_BITS_PER_SAMPLE, 16);
                Mf.SetUInt32(t, Mf.MT_AUDIO_NUM_CHANNELS, 2);
                Mf.SetUInt32(t, Mf.MT_AUDIO_SAMPLES_PER_SECOND, 48000);
                Mf.SetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_AUDIO_STREAM, t);
                Com.Release(ref t);
            }

            return reader;
        }
        catch
        {
            Com.Release(ref reader);
            throw;
        }
    }

    private static double MeanLuma(nint sample)
    {
        nint buffer = Mf.ConvertToContiguousBuffer(sample);
        try
        {
            byte* data = Mf.LockBuffer(buffer, out _, out int length);
            try
            {
                long sum = 0;
                int n = 0;
                for (int i = 0; i + 3 < length; i += 4 * 16) // every 16th pixel is plenty
                {
                    sum += data[i] + data[i + 1] + data[i + 2];
                    n += 3;
                }

                return n == 0 ? 0 : sum / (double)n;
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
    }

    private static double Slope(List<double> x, List<double> y)
    {
        if (x.Count < 2)
        {
            return 0;
        }

        double mx = x.Average(), my = y.Average();
        double num = 0, den = 0;
        for (int i = 0; i < x.Count; i++)
        {
            num += (x[i] - mx) * (y[i] - my);
            den += (x[i] - mx) * (x[i] - mx);
        }

        return den == 0 ? 0 : num / den;
    }
}
