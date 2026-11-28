using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.App.Rendering;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Export;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Video;

/// <summary>
/// <c>--video-quality &lt;dir&gt;</c> (docs/VIDEO-QUALITY.md): records the same deterministic 1920×1080 test scene twice
/// through the real recorder — once with release candidate 2's settings (0.08 bits per pixel, Main profile,
/// encoder-default rate control, low latency, bilinear camera scaling) and once with the current default — decodes
/// both and compares every region against the source frame it came from (identified by a frame-number code drawn
/// into the scene, so dropped or repeated frames can never pair the wrong frames). The camera is a SYNTHETIC
/// static picture composited through the real <see cref="CompositeFrameSource"/>. Writes report.json and native-size
/// PNG crops (quality-*.png); exit code 0 only if the new default beats RC2 on small text and drops ≤ 1 % of frames.
/// </summary>
internal static partial class VideoSelfTest
{
    private const int QualitySeconds = 5;
    private const int StaticTarget = 45;  // mid-GOP frame of the static part (frames 0–59), 1.5 s in
    private const int ScrollTarget = 105; // mid-GOP frame while text scrolls and the object moves, 3.5 s in

    /// <summary>The PASS rule, printed with the result (docs/VIDEO-QUALITY.md states the same).</summary>
    internal const string QualityCriterion =
        "PASS if (1) new small-text PSNR, averaged over the static and the scrolling sample frame, is at least 1.0 dB above RC2's, "
        + "(2) new small-text SSIM (same average) is not below RC2's, and (3) the new recording dropped at most 1 % of its frames "
        + "(dropped ÷ (written + dropped)).";

    private sealed record QualityVariant(string Name, string Label, VideoEncoderTuning Tuning, CameraScaling Scaling);

    private static int Quality(string dir)
    {
        try
        {
            return QualityComparison(dir);
        }
#pragma warning disable CA1031 // reported in the CI log instead of crashing without a verdict
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.WriteLine("QUALITY VERDICT FAIL: the comparison could not run: " + ex);
            return 1;
        }
    }

    private static int QualityComparison(string dir)
    {
        Directory.CreateDirectory(dir);
        var scene = new QualityScene();
        var camera = new StaticPictureCameraSource(QualityScene.RenderCameraPicture(1280, 720));
        QualityVariant[] variants =
        [
            new("rc2", "release candidate 2 settings (old default)", VideoEncoderTuning.Rc2, CameraScaling.Bilinear),
            new("rc3", "current default", VideoEncoderTuning.Default, CameraScaling.AreaAverage),
        ];

        var recordings = new Dictionary<string, (RecordingResult Result, string Path, OverlayLayout Layout)>();
        var report = new Dictionary<string, object?> { ["criterion"] = QualityCriterion, ["scene"] = QualityScene.Describe(), ["camera"] = camera.Name };
        foreach (QualityVariant v in variants)
        {
            string path = Path.Combine(dir, $"quality-{v.Name}.mp4");
            camera.Start();
            var source = new QualityFrameSource(scene);
            var composite = new CompositeFrameSource(source, camera, CameraOverlayShape.Circle, CameraOverlaySize.Medium, CameraCorner.BottomRight, mirror: true) { Scaling = v.Scaling };
            RecordingResult r;
            using (ScreenRecorder rec = ScreenRecorder.Create(composite, [], Options(30, audio: false) with { Tuning = v.Tuning }, path))
            {
                rec.Start();
                Thread.Sleep(TimeSpan.FromSeconds(QualitySeconds));
                r = rec.Stop();
            }

            recordings[v.Name] = (r, path, composite.Layout);
        }

        // Frames both files contain, nearest to the targets (same frame number in old, new and the source).
        Dictionary<string, List<int>> indices = recordings.ToDictionary(kv => kv.Key, kv => DecodedFrameNumbers(kv.Value.Path));
        int Common(int target, int from, int to) => indices["rc2"].Intersect(indices["rc3"]).Where(i => i >= from && i <= to).DefaultIfEmpty(-1).MinBy(i => Math.Abs(i - target));
        int staticIndex = Common(StaticTarget, 30, QualityScene.StaticFrames - 1);
        int scrollIndex = Common(ScrollTarget, 90, 119);
        report["sampleFrames"] = new { staticFrame = staticIndex, scrollingFrame = scrollIndex, staticTarget = StaticTarget, scrollingTarget = ScrollTarget };

        OverlayLayout layout = recordings["rc3"].Layout;
        PixelRect cameraRegion = QualityScene.CameraMeasureRegion(layout);
        var regions = new (string Name, PixelRect Rect)[]
        {
            ("small-text", QualityScene.SmallText),
            ("static-detail", QualityScene.StaticDetail),
            ("scrolling", QualityScene.Scrolling),
            ("camera-overlay", cameraRegion),
        };

        var samples = new (string Name, int Index)[] { ("static", staticIndex), ("scrolling", scrollIndex) };
        var references = new Dictionary<int, PixelBuffer>();
        foreach (var (_, index) in samples.Where(s => s.Index >= 0))
        {
            references[index] = scene.ReferenceFrame(index, camera.Picture, layout);
        }

        var perRecording = new Dictionary<string, object?>();
        var quality = new Dictionary<(string Recording, string Sample, string Region), RegionQuality>();
        var decodedFrames = new Dictionary<(string Recording, int Index), PixelBuffer>();
        foreach (QualityVariant v in variants)
        {
            (RecordingResult r, string path, OverlayLayout _) = recordings[v.Name];
            Dictionary<int, PixelBuffer> frames = DecodeFrameNumbers(path, [.. references.Keys]);
            foreach ((int i, PixelBuffer f) in frames)
            {
                decodedFrames[(v.Name, i)] = f;
            }

            CompressedStats stats = ReadCompressedStats(path);
            double dropped = r.FramesWritten + r.FramesDropped == 0 ? 1 : r.FramesDropped / (double)(r.FramesWritten + r.FramesDropped);
            var regionResults = new Dictionary<string, object?>();
            foreach ((string sampleName, int index) in samples)
            {
                if (index < 0 || !frames.TryGetValue(index, out PixelBuffer? decoded) || decoded.Width != QualityScene.Width || decoded.Height != QualityScene.Height)
                {
                    regionResults[sampleName] = new { error = "sample frame not decoded" };
                    continue;
                }

                var byRegion = new Dictionary<string, object?>();
                foreach ((string regionName, PixelRect rect) in regions)
                {
                    RegionQuality q = ImageQuality.Compare(references[index], decoded, rect);
                    quality[(v.Name, sampleName, regionName)] = q;
                    byRegion[regionName] = new { psnrDb = q.PsnrDb, ssim = q.Ssim, meanAbsError = q.MeanAbsError };
                }

                regionResults[sampleName] = new { frame = index, regions = byRegion };
            }

            perRecording[v.Name] = new
            {
                settings = v.Label,
                tuning = v.Tuning.Describe(),
                cameraScaling = v.Scaling.ToString(),
                width = stats.Width,
                height = stats.Height,
                decodedFrames = stats.Frames,
                durationMs = stats.DurationMs,
                fps = stats.DurationMs <= 0 ? 0 : Math.Round(stats.Frames * 1000.0 / stats.DurationMs, 2),
                keyFrames = stats.KeyFrames,
                profileInFile = stats.Profile,
                averageVideoBitRate = stats.DurationMs <= 0 ? 0 : Math.Round(stats.VideoBytes * 8.0 / (stats.DurationMs / 1000.0)),
                requestedMeanBitRate = v.Tuning.BitRate(QualityScene.Width, QualityScene.Height, 30, VideoQuality.Standard),
                fileBytes = r.Bytes,
                encoder = r.Encoder,
                hardwareEncoder = r.HardwareEncoder,
                softwareFallback = r.SoftwareFallback,
                encoderSettings = r.EncoderSettings,
                framesWritten = r.FramesWritten,
                framesRepeated = r.FramesRepeated,
                framesDropped = r.FramesDropped,
                droppedShare = Math.Round(dropped, 4),
                cpuPercent = r.CpuPercent,
                quality = regionResults,
            };

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"QUALITY {v.Name}: {stats.Width}x{stats.Height}, {stats.Frames} frames in {stats.DurationMs} ms ({(stats.DurationMs <= 0 ? 0 : stats.Frames * 1000.0 / stats.DurationMs):0.00} fps), video {stats.VideoBytes * 8.0 / Math.Max(1, stats.DurationMs) / 1000:0.00} Mbit/s, file {r.Bytes / 1048576.0:0.00} MB, {stats.KeyFrames} key frames, dropped {r.FramesDropped} ({dropped:P2}), repeated {r.FramesRepeated}"));
            Console.WriteLine($"QUALITY {v.Name} encoder: {r.Encoder} ({(r.HardwareEncoder ? "hardware" : "software")}{(r.SoftwareFallback ? ", software fallback" : string.Empty)}), camera scaling {v.Scaling}; {r.EncoderSettings}");
            foreach ((string sampleName, int index) in samples)
            {
                Console.WriteLine($"QUALITY {v.Name} {sampleName} frame {index}: " + string.Join(" | ", regions.Select(rg => quality.TryGetValue((v.Name, sampleName, rg.Name), out RegionQuality q)
                    ? string.Create(CultureInfo.InvariantCulture, $"{rg.Name} PSNR {q.PsnrDb:0.00} dB SSIM {q.Ssim:0.0000} MAE {q.MeanAbsError:0.00}")
                    : $"{rg.Name} n/a")));
            }
        }

        report["recordings"] = perRecording;

        // The camera scaler alone (no encoding): RC2 bilinear and RC3 area average against the exact area average.
        if (references.Count > 0)
        {
            int any = references.Keys.First();
            var scaler = new Dictionary<string, object?>();
            foreach (QualityVariant v in variants)
            {
                RegionQuality q = ImageQuality.Compare(references[any], scene.CompositedFrame(any, camera.Picture, layout, v.Scaling), cameraRegion);
                scaler[v.Name] = new { scaling = v.Scaling.ToString(), psnrDb = q.PsnrDb, ssim = q.Ssim, meanAbsError = q.MeanAbsError };
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"QUALITY camera scaler only ({v.Scaling}, before encoding): PSNR {q.PsnrDb:0.00} dB SSIM {q.Ssim:0.0000} MAE {q.MeanAbsError:0.00}"));
            }

            report["cameraScalerOnly"] = scaler;
        }

        // Native-size crops (no rescaling) of the same frame and region: source, RC2, RC3.
        var pngs = new List<string>();
        void Crops(string what, int index, PixelRect rect)
        {
            if (index < 0 || !references.TryGetValue(index, out PixelBuffer? reference))
            {
                return;
            }

            foreach ((string tag, PixelBuffer? image) in new[] { ("source", reference), ("rc2", decodedFrames.GetValueOrDefault(("rc2", index))), ("rc3", decodedFrames.GetValueOrDefault(("rc3", index))) })
            {
                if (image is null || image.Width != QualityScene.Width || image.Height != QualityScene.Height)
                {
                    continue;
                }

                string name = $"quality-{what}-{tag}";
                PixelBuffer crop = image.Crop(rect);
                crop.ForceOpaque();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), ImageExporter.Encode(crop, ImageFileFormat.Png));
                pngs.Add(name);
            }
        }

        Crops("text", staticIndex, QualityScene.TextCrop);
        Crops("scroll", scrollIndex, QualityScene.ScrollCrop);
        Crops("detail", staticIndex, QualityScene.DetailCrop);
        Crops("camera", staticIndex, new PixelRect(layout.X, layout.Y, layout.Width, layout.Height));
        report["pngs"] = pngs;

        // Verdict.
        var failures = new List<string>();
        double Mean(string rec, string region, Func<RegionQuality, double> pick) =>
            samples.Select(s => quality.TryGetValue((rec, s.Name, region), out RegionQuality q) ? pick(q) : double.NaN).Average();
        double oldPsnr = Mean("rc2", "small-text", q => q.PsnrDb), newPsnr = Mean("rc3", "small-text", q => q.PsnrDb);
        double oldSsim = Mean("rc2", "small-text", q => q.Ssim), newSsim = Mean("rc3", "small-text", q => q.Ssim);
        if (double.IsNaN(oldPsnr) || double.IsNaN(newPsnr))
        {
            failures.Add("the sample frames could not be decoded from both recordings");
        }
        else
        {
            if (newPsnr < oldPsnr + 1.0)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"small-text PSNR new {newPsnr:0.00} dB is not ≥ RC2 {oldPsnr:0.00} dB + 1.0 dB"));
            }

            if (newSsim < oldSsim)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"small-text SSIM new {newSsim:0.0000} is below RC2 {oldSsim:0.0000}"));
            }
        }

        RecordingResult nr = recordings["rc3"].Result;
        double newDropped = nr.FramesWritten + nr.FramesDropped == 0 ? 1 : nr.FramesDropped / (double)(nr.FramesWritten + nr.FramesDropped);
        if (newDropped > 0.01)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"the new recording dropped {nr.FramesDropped} frames ({newDropped:P2} > 1 %)"));
        }

        report["verdict"] = new
        {
            passed = failures.Count == 0,
            smallTextPsnrDb = new { rc2 = Finite(oldPsnr, 2), rc3 = Finite(newPsnr, 2), gain = Finite(newPsnr - oldPsnr, 2) },
            smallTextSsim = new { rc2 = Finite(oldSsim, 4), rc3 = Finite(newSsim, 4) },
            newDroppedShare = Math.Round(newDropped, 4),
            failures,
        };
        File.WriteAllText(Path.Combine(dir, "quality-report.json"), JsonSerializer.Serialize(report, Json));
        string verdict = string.Create(CultureInfo.InvariantCulture,
            $"QUALITY VERDICT {(failures.Count == 0 ? "PASS" : "FAIL")}: small-text PSNR {oldPsnr:0.00} → {newPsnr:0.00} dB ({newPsnr - oldPsnr:+0.00;-0.00} dB), SSIM {oldSsim:0.0000} → {newSsim:0.0000}, new dropped {newDropped:P2}");
        Console.WriteLine(verdict + (failures.Count == 0 ? string.Empty : " — " + string.Join("; ", failures)));
        Console.WriteLine("QUALITY criterion: " + QualityCriterion);
        camera.Dispose();
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>JSON cannot hold NaN: an unmeasured value is null.</summary>
    private static double? Finite(double value, int digits) => double.IsFinite(value) ? Math.Round(value, digits) : null;

    // ---------------------------------------------------------------- decoding

    /// <summary>Frame numbers (read from the scene's frame code) of every decoded frame, in order.</summary>
    private static unsafe List<int> DecodedFrameNumbers(string path)
    {
        var list = new List<int>();
        ForEachDecodedFrame(path, (data, pitch, w, h) =>
        {
            list.Add(QualityScene.ReadCode(data, pitch, w, h));
            return true;
        });
        return list;
    }

    /// <summary>Full decoded frames for the wanted frame numbers (the first decoded frame carrying each number).</summary>
    private static unsafe Dictionary<int, PixelBuffer> DecodeFrameNumbers(string path, HashSet<int> wanted)
    {
        var result = new Dictionary<int, PixelBuffer>();
        ForEachDecodedFrame(path, (data, pitch, w, h) =>
        {
            int code = QualityScene.ReadCode(data, pitch, w, h);
            if (wanted.Contains(code) && !result.ContainsKey(code))
            {
                var pixels = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                {
                    new ReadOnlySpan<byte>(data + ((long)y * pitch), w * 4).CopyTo(pixels.AsSpan(y * w * 4));
                }

                var frame = new PixelBuffer(w, h, pixels);
                frame.ForceOpaque();
                result[code] = frame;
            }

            return result.Count < wanted.Count;
        });
        return result;
    }

    private unsafe delegate bool DecodedFrameVisitor(byte* data, int pitch, int width, int height);

    private static unsafe void ForEachDecodedFrame(string path, DecodedFrameVisitor visit)
    {
        nint reader = VideoAnalysis.OpenDecoding(path, video: true, audio: false);
        try
        {
            nint type = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            ulong size = Mf.GetUInt64(type, Mf.MT_FRAME_SIZE) ?? 0;
            Com.Release(ref type);
            int w = (int)(size >> 32), h = (int)(size & 0xFFFFFFFF);
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long _, out nint sample) && sample != 0)
            {
                try
                {
                    nint buffer = Mf.ConvertToContiguousBuffer(sample);
                    try
                    {
                        byte* data = VideoAnalysis.LockDecodedRows(buffer, w, h, out int pitch, out bool locked2D);
                        try
                        {
                            if (!visit(data, pitch, w, h))
                            {
                                return;
                            }
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
                finally
                {
                    Com.Release(ref sample);
                }
            }
        }
        finally
        {
            Com.Release(ref reader);
        }
    }

    private sealed record CompressedStats(int Width, int Height, int Frames, long DurationMs, long VideoBytes, int KeyFrames, string? Profile);

    private static readonly Guid SampleExtension_CleanPoint = new("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");

    /// <summary>Compressed video samples (no decoding): count, bytes, key frames, duration and the profile in the file.</summary>
    private static CompressedStats ReadCompressedStats(string path)
    {
        nint reader = Mf.CreateSourceReader(path, 0);
        try
        {
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
            nint type = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            ulong size = Mf.GetUInt64(type, Mf.MT_FRAME_SIZE) ?? 0;
            uint? profile = Mf.GetUInt32(type, Mf.MT_MPEG2_PROFILE);
            Com.Release(ref type);
            int frames = 0, keys = 0;
            long bytes = 0, first = -1, last = 0;
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                try
                {
                    frames++;
                    first = first < 0 ? ts : Math.Min(first, ts);
                    last = Math.Max(last, ts);
                    if (Mf.GetUInt32(sample, SampleExtension_CleanPoint) is 1)
                    {
                        keys++;
                    }

                    nint buffer = Mf.ConvertToContiguousBuffer(sample);
                    bytes += Mf.GetBufferLength(buffer);
                    Com.Release(ref buffer);
                }
                finally
                {
                    Com.Release(ref sample);
                }
            }

            long durationMs = frames == 0 ? 0 : ((last - first) / 10_000) + (1000 / 30);
            string? profileName = profile switch
            {
                66 => "Baseline (66)",
                77 => "Main (77)",
                100 => "High (100)",
                null => null,
                { } p => p.ToString(CultureInfo.InvariantCulture),
            };
            return new CompressedStats((int)(size >> 32), (int)(size & 0xFFFFFFFF), frames, durationMs, bytes, keys, profileName);
        }
        finally
        {
            Com.Release(ref reader);
        }
    }
}

/// <summary>
/// The deterministic 1920×1080 quality test scene (docs/VIDEO-QUALITY.md): small menu-size text (9–11 pt Segoe UI,
/// grayscale antialiasing at 100 %), static fine detail (1-pixel checkerboards, thin lines, a photo-like gradient),
/// a text panel that scrolls upward 6 px per frame (180 px/s) after the first 2 s, a moving object, and a 16-bit
/// frame-number code. Frame <c>i</c> is a pure function of <c>i</c>.
/// </summary>
internal sealed unsafe class QualityScene
{
    public const int Width = 1920, Height = 1080;
    public const int StaticFrames = 60;     // frames 0–59: nothing moves
    public const int ScrollPixelsPerFrame = 6;
    public const int ObjectPixelsPerFrame = 10;
    public const int CodeBits = 16, CodeBlock = 24;

    public static readonly PixelRect SmallText = new(40, 40, 860, 440);
    public static readonly PixelRect StaticDetail = new(960, 40, 920, 440);
    public static readonly PixelRect Scrolling = new(40, 520, 860, 480);
    public static readonly PixelRect MovingBand = new(960, 520, 560, 200);
    public static readonly PixelRect Code = new(40, 1024, CodeBits * CodeBlock, CodeBlock);

    /// <summary>Native-size crops shown in the CI log.</summary>
    public static readonly PixelRect TextCrop = new(40, 40, 480, 270);
    public static readonly PixelRect ScrollCrop = new(40, 520, 480, 270);
    public static readonly PixelRect DetailCrop = new(960, 40, 480, 270);

    private const uint Background = 0xFFFFFFFF;
    private const uint BandColor = 0xFFE8ECEF;
    private const int ObjectSize = 120;

    private readonly byte[] _base;
    private readonly PixelBuffer _strip;
    private readonly PixelBuffer _sprite;

    public QualityScene()
    {
        _base = RenderBase().Pixels;
        _strip = RenderScrollStrip(Scrolling.Width, Scrolling.Height + (ScrollPixelsPerFrame * 400));
        _sprite = RenderSprite();
    }

    public static object Describe() => new
    {
        size = $"{Width}x{Height}",
        smallText = SmallText.ToString(),
        staticDetail = StaticDetail.ToString(),
        scrolling = Scrolling.ToString(),
        movingBand = MovingBand.ToString(),
        staticFrames = StaticFrames,
        scrollPixelsPerFrame = ScrollPixelsPerFrame,
        objectPixelsPerFrame = ObjectPixelsPerFrame,
        text = "Segoe UI 9/10/11 pt (12/13.3/14.7 px) at 96 DPI, grayscale antialiasing, display text formatting",
        camera = "SYNTHETIC static 1280x720 picture (zone plate, face-like gradients, fine lines, label), circle Medium bottom-right, mirrored",
    };

    /// <summary>The region of the camera overlay that is measured: the square inscribed in the circle (no mask edge).</summary>
    public static PixelRect CameraMeasureRegion(OverlayLayout l)
    {
        int side = (int)(Math.Min(l.Width, l.Height) / Math.Sqrt(2)) - 4;
        return new PixelRect(l.X + ((l.Width - side) / 2), l.Y + ((l.Height - side) / 2), side, side);
    }

    /// <summary>Writes frame <paramref name="index"/> (BGRA, top-down) into <paramref name="dst"/>.</summary>
    public void Render(int index, byte* dst, int stride)
    {
        fixed (byte* b = _base)
        {
            for (int y = 0; y < Height; y++)
            {
                Buffer.MemoryCopy(b + ((long)y * Width * 4), dst + ((long)y * stride), Width * 4, Width * 4);
            }
        }

        int moving = Math.Max(0, index - StaticFrames);
        int maxScroll = _strip.Height - Scrolling.Height;
        int offset = (moving * ScrollPixelsPerFrame) % maxScroll;
        fixed (byte* s = _strip.Pixels)
        {
            for (int y = 0; y < Scrolling.Height; y++)
            {
                Buffer.MemoryCopy(s + ((long)(y + offset) * _strip.Width * 4), dst + ((long)(Scrolling.Y + y) * stride) + (Scrolling.X * 4), Scrolling.Width * 4, Scrolling.Width * 4);
            }
        }

        // Moving object: back and forth across the band.
        int travel = MovingBand.Width - ObjectSize - 20;
        int phase = (moving * ObjectPixelsPerFrame) % (2 * travel);
        int ox = MovingBand.X + 10 + (phase <= travel ? phase : (2 * travel) - phase);
        int oy = MovingBand.Y + ((MovingBand.Height - ObjectSize) / 2);
        fixed (byte* sp = _sprite.Pixels)
        {
            for (int y = 0; y < ObjectSize; y++)
            {
                Buffer.MemoryCopy(sp + ((long)y * ObjectSize * 4), dst + ((long)(oy + y) * stride) + (ox * 4), ObjectSize * 4, ObjectSize * 4);
            }
        }

        // Frame code: 16 blocks, black = 1, white = 0, least significant bit first.
        for (int bit = 0; bit < CodeBits; bit++)
        {
            uint c = ((index >> bit) & 1) != 0 ? 0xFF000000 : 0xFFFFFFFF;
            for (int y = 0; y < CodeBlock; y++)
            {
                new Span<uint>(dst + ((long)(Code.Y + y) * stride) + ((Code.X + (bit * CodeBlock)) * 4), CodeBlock).Fill(c);
            }
        }
    }

    /// <summary>Reads the frame code from a (decoded) frame; −1 if the frame is too small.</summary>
    public static int ReadCode(byte* data, int pitch, int w, int h)
    {
        if (w < Code.Right || h < Code.Bottom)
        {
            return -1;
        }

        int value = 0;
        for (int bit = 0; bit < CodeBits; bit++)
        {
            int sum = 0, n = 0;
            for (int dy = -4; dy <= 4; dy += 4)
            {
                for (int dx = -4; dx <= 4; dx += 4)
                {
                    byte* p = data + ((long)(Code.Y + (CodeBlock / 2) + dy) * pitch) + ((Code.X + (bit * CodeBlock) + (CodeBlock / 2) + dx) * 4);
                    sum += (p[0] + p[1] + p[2]) / 3;
                    n++;
                }
            }

            if (sum / n < 128)
            {
                value |= 1 << bit;
            }
        }

        return value;
    }

    /// <summary>The scene frame with the camera composited by the recorder's compositor (no encoding).</summary>
    public PixelBuffer CompositedFrame(int index, PixelBuffer cameraPicture, OverlayLayout layout, CameraScaling scaling)
    {
        var frame = new PixelBuffer(Width, Height);
        fixed (byte* f = frame.Pixels)
        {
            Render(index, f, Width * 4);
            new CameraCompositor(scaling).Draw(f, Width * 4, Width, Height, cameraPicture.Pixels, cameraPicture.Width, cameraPicture.Height, layout);
        }

        return frame;
    }

    /// <summary>
    /// The reference frame: the scene with the camera picture scaled into the overlay by an exact (double precision)
    /// area average — the ideal box-filtered downscale — and blended through the same antialiased circle mask.
    /// </summary>
    public PixelBuffer ReferenceFrame(int index, PixelBuffer cam, OverlayLayout l)
    {
        var frame = new PixelBuffer(Width, Height);
        fixed (byte* f = frame.Pixels)
        {
            Render(index, f, Width * 4);
        }

        int bw = l.Width, bh = l.Height;
        double boxAspect = bw / (double)bh, camAspect = cam.Width / (double)cam.Height;
        double srcW = cam.Width, srcH = cam.Height, srcX = 0, srcY = 0;
        if (camAspect > boxAspect)
        {
            srcW = cam.Height * boxAspect;
            srcX = (cam.Width - srcW) / 2;
        }
        else
        {
            srcH = cam.Width / boxAspect;
            srcY = (cam.Height - srcH) / 2;
        }

        double sx = srcW / bw, sy = srcH / bh;
        byte[] c = cam.Pixels, o = frame.Pixels;
        for (int by = 0; by < bh; by++)
        {
            double y0 = srcY + (by * sy), y1 = y0 + sy;
            for (int bx = 0; bx < bw; bx++)
            {
                double px = bx + 0.5 - (bw / 2.0), py = by + 0.5 - (bh / 2.0);
                double coverage = Math.Clamp(0.5 - (Math.Sqrt((px * px) + (py * py)) - (Math.Min(bw, bh) / 2.0)), 0, 1);
                if (coverage <= 0)
                {
                    continue;
                }

                int k = l.Mirror ? bw - 1 - bx : bx;
                double x0 = srcX + (k * sx), x1 = x0 + sx;
                double b = 0, g = 0, r = 0, area = 0;
                for (int yy = (int)Math.Floor(y0); yy < (int)Math.Ceiling(y1); yy++)
                {
                    double wy = Math.Min(y1, yy + 1) - Math.Max(y0, yy);
                    int ry = Math.Clamp(yy, 0, cam.Height - 1);
                    for (int xx = (int)Math.Floor(x0); xx < (int)Math.Ceiling(x1); xx++)
                    {
                        double w = wy * (Math.Min(x1, xx + 1) - Math.Max(x0, xx));
                        int p = ((ry * cam.Width) + Math.Clamp(xx, 0, cam.Width - 1)) * 4;
                        b += c[p] * w;
                        g += c[p + 1] * w;
                        r += c[p + 2] * w;
                        area += w;
                    }
                }

                int q = (((l.Y + by) * Width) + l.X + bx) * 4;
                double a = Math.Round(coverage * 255) / 255;
                o[q] = (byte)Math.Round((b / area * a) + (o[q] * (1 - a)));
                o[q + 1] = (byte)Math.Round((g / area * a) + (o[q + 1] * (1 - a)));
                o[q + 2] = (byte)Math.Round((r / area * a) + (o[q + 2] * (1 - a)));
                o[q + 3] = 255;
            }
        }

        return frame;
    }

    // ---------------------------------------------------------------- rendering (WPF, once)

    private static readonly Typeface Ui = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static double Pt(double points) => points * 96 / 72;

    private static FormattedText Text(string s, double points, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Ui, Pt(points), brush, 1.0);

    private static PixelBuffer RenderVisual(int w, int h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(visual, TextHintingMode.Fixed);
        using (DrawingContext dc = visual.RenderOpen())
        {
            draw(dc);
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PixelBuffer p = BitmapConversion.FromBitmapSource(bitmap);
        p.ForceOpaque();
        return p;
    }

    private static readonly string[] Sentences =
    [
        "File  Edit  View  Insert  Format  Tools  Window  Help",
        "Settings › Recording › Video quality: Standard (H.264, 30 FPS) — More options…",
        "The quick brown fox jumps over the lazy dog. 0123456789 (){}[] <> /\\ | @#$%&*",
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz ÄÖÜ äöüß éèêë",
        "Il1| O0o rn m cl d vv w — characters that blur together when compression smears them",
        "C:\\Users\\Owner\\Documents\\SnapForge\\Recordings\\2026-11-28 10.42.17.mp4  (83.4 MB)",
        "Ready   |   Ln 128, Col 42   |   Spaces: 4   |   UTF-8   |   CRLF   |   C#",
    ];

    private static PixelBuffer RenderBase()
    {
        PixelBuffer p = RenderVisual(Width, Height, dc =>
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Width, Height));
            var dark = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
            var grey = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
            var blue = new SolidColorBrush(Color.FromRgb(0x00, 0x63, 0xB1));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, Width, 30));
            dc.DrawText(Text(Sentences[0], 9, dark), new Point(12, 7));

            // Small text: 9, 10 and 11 pt lines, dark and grey on white, a selected line (white on blue).
            double y = SmallText.Y + 4;
            int line = 0;
            while (y < SmallText.Bottom - 20)
            {
                double size = (line % 3) switch { 0 => 9, 1 => 10, _ => 11 };
                string s = Sentences[1 + (line % (Sentences.Length - 1))];
                bool selected = line == 7;
                if (selected)
                {
                    dc.DrawRectangle(blue, null, new Rect(SmallText.X, y - 2, SmallText.Width, Pt(size) * 1.45));
                }

                dc.DrawText(Text(s, size, selected ? Brushes.White : line % 4 == 3 ? grey : dark), new Point(SmallText.X + 6, y));
                y += Math.Round(Pt(size) * 1.45);
                line++;
            }

            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEF)), null, new Rect(MovingBand.X, MovingBand.Y, MovingBand.Width, MovingBand.Height));
            var thin = new Pen(dark, 1);
            thin.Freeze();
            dc.DrawRectangle(null, thin, new Rect(Scrolling.X - 0.5, Scrolling.Y - 0.5, Scrolling.Width + 1, Scrolling.Height + 1));
            dc.DrawText(Text("Scrolling panel (text scrolls up 180 px/s after 2 s)", 9, grey), new Point(Scrolling.X, Scrolling.Y - 18));
            dc.DrawText(Text("Moving object", 9, grey), new Point(MovingBand.X, MovingBand.Y - 18));
            dc.DrawText(Text("Frame code", 9, grey), new Point(Code.X, Code.Y - 18));
            dc.DrawText(Text("SnapForge video quality scene — camera overlay is SYNTHETIC", 9, grey), new Point(960, 1040));

            // Thin lines: hairlines on the pixel grid and antialiased diagonals.
            int lx = StaticDetail.X + 310;
            for (int i = 0; i < 300; i += 3)
            {
                dc.DrawLine(thin, new Point(lx + i + 0.5, StaticDetail.Y), new Point(lx + i + 0.5, StaticDetail.Y + 140));
            }

            for (int i = 0; i < 140; i += 3)
            {
                dc.DrawLine(thin, new Point(lx, StaticDetail.Y + 150 + i + 0.5), new Point(lx + 300, StaticDetail.Y + 150 + i + 0.5));
            }

            for (int i = 0; i < 300; i += 8)
            {
                dc.DrawLine(thin, new Point(lx + i, StaticDetail.Y + 300), new Point(lx + i + 100, StaticDetail.Y + 440));
            }
        });

        // Checkerboards (1 px and 2 px) and a photo-like gradient, written directly.
        byte[] px = p.Pixels;
        for (int y = StaticDetail.Y; y < StaticDetail.Bottom; y++)
        {
            for (int x = StaticDetail.X; x < StaticDetail.X + 300; x++)
            {
                int cell = y < StaticDetail.Y + 220 ? 1 : 2;
                byte v = (((x / cell) + (y / cell)) & 1) == 0 ? (byte)0 : (byte)255;
                Set(px, x, y, v, v, v);
            }

            for (int x = StaticDetail.X + 620; x < StaticDetail.Right; x++)
            {
                double u = (x - StaticDetail.X - 620) / 300.0, w = (y - StaticDetail.Y) / (double)StaticDetail.Height;
                double tex = 10 * Math.Sin((x * 0.21) + (Math.Sin(y * 0.05) * 3)) * Math.Cos(y * 0.13);
                int noise = (int)(Hash(x, y) % 13) - 6;
                Set(px, x, y,
                    Clamp((60 + (150 * u * (1 - w))) + tex + noise),
                    Clamp((90 + (120 * w)) + (tex * 0.7) + noise),
                    Clamp((200 - (110 * u)) + (60 * w) + (tex * 0.5) + noise));
            }
        }

        return p;
    }

    private static PixelBuffer RenderScrollStrip(int w, int h) => RenderVisual(w, h, dc =>
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        var dark = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
        double y = 6;
        int line = 0;
        while (y < h - 20)
        {
            double size = (line % 3) switch { 0 => 10, 1 => 9, _ => 11 };
            dc.DrawText(Text($"{line + 1,4}  " + Sentences[1 + (line % (Sentences.Length - 1))], size, dark), new Point(6, y));
            y += Math.Round(Pt(size) * 1.5);
            line++;
        }
    });

    private static PixelBuffer RenderSprite() => RenderVisual(ObjectSize, ObjectSize, dc =>
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEF)), null, new Rect(0, 0, ObjectSize, ObjectSize));
        var fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0x8C, 0x00), Color.FromRgb(0xC0, 0x30, 0x10), 45);
        dc.DrawRoundedRectangle(fill, new Pen(Brushes.Black, 2), new Rect(4, 4, ObjectSize - 8, ObjectSize - 8), 14, 14);
        dc.DrawText(Text("Drag", 11, Brushes.White), new Point(38, 48));
    });

    /// <summary>The SYNTHETIC camera picture: zone plate, face-like gradients, fine "hair" lines and a label.</summary>
    public static PixelBuffer RenderCameraPicture(int w, int h)
    {
        var zone = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x - (w / 2.0), dy = y - (h / 2.0);
                double v = 128 + (90 * Math.Cos(0.0021 * ((dx * dx) + (dy * dy))));
                Set(zone.Pixels, w, x, y, Clamp(v * 0.8), Clamp(v * 0.9), Clamp(v));
            }
        }

        BitmapSource zoneBitmap = BitmapConversion.ToFrozenBitmap(zone);
        return RenderVisual(w, h, dc =>
        {
            dc.DrawImage(zoneBitmap, new Rect(0, 0, w, h));
            var skin = new RadialGradientBrush(Color.FromRgb(0xF1, 0xC2, 0x9A), Color.FromRgb(0x9C, 0x62, 0x3E)) { GradientOrigin = new Point(0.4, 0.35) };
            dc.DrawEllipse(skin, null, new Point(w / 2.0, (h / 2.0) + 10), 170, 220);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2A, 0x1E, 0x18)), null, new Point((w / 2.0) - 60, (h / 2.0) - 30), 22, 13);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2A, 0x1E, 0x18)), null, new Point((w / 2.0) + 60, (h / 2.0) - 30), 22, 13);
            var hair = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x22, 0x14)), 1);
            for (int i = 0; i < 90; i++)
            {
                double x0 = (w / 2.0) - 170 + (i * 3.8);
                dc.DrawLine(hair, new Point(x0, (h / 2.0) - 230), new Point(x0 + 14 - (i % 7 * 4), (h / 2.0) - 120));
            }

            var label = new FormattedText("SYNTHETIC CAMERA", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Ui, 30, Brushes.White, 1.0);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), null, new Rect((w / 2.0) - (label.Width / 2) - 8, (h / 2.0) + 150, label.Width + 16, label.Height + 4));
            dc.DrawText(label, new Point((w / 2.0) - (label.Width / 2), (h / 2.0) + 152));
        });
    }

    private static void Set(byte[] px, int x, int y, byte r, byte g, byte b) => Set(px, Width, x, y, r, g, b);

    private static void Set(byte[] px, int stride, int x, int y, byte r, byte g, byte b)
    {
        int o = ((y * stride) + x) * 4;
        px[o] = b;
        px[o + 1] = g;
        px[o + 2] = r;
        px[o + 3] = 255;
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static uint Hash(int x, int y)
    {
        uint h = (uint)((x * 73856093) ^ (y * 19349663));
        h ^= h >> 13;
        h *= 0x5bd1e995;
        return h ^ (h >> 15);
    }
}

/// <summary>The quality scene as a recorder frame source: frame index from the recording clock, like a real screen.</summary>
internal sealed unsafe class QualityFrameSource(QualityScene scene) : IFrameSource
{
    private long _start;
    private long _last = -1;

    public int Width => QualityScene.Width;

    public int Height => QualityScene.Height;

    public string? EndReason => null;

    public string Description => "deterministic 1920×1080 quality test scene (text, fine detail, scrolling, moving object)";

    public void Start() => _start = Qpc.Now100ns();

    public FramePoll Poll(byte* destination, int stride, out long sourceTime100ns)
    {
        long period = 10_000_000 / 30;
        long index = (Qpc.Now100ns() - _start) / period;
        sourceTime100ns = _start + (index * period);
        if (index == _last)
        {
            return FramePoll.NoChange;
        }

        _last = index;
        scene.Render((int)Math.Min(index, 65_535), destination, stride);
        return FramePoll.NewFrame;
    }

    public void Dispose()
    {
    }
}

/// <summary>SYNTHETIC camera that always shows one fixed picture (deterministic overlay for the quality comparison).</summary>
internal sealed class StaticPictureCameraSource(PixelBuffer picture) : ICameraSource
{
    private volatile bool _running;

    public event EventHandler<string>? Lost
    {
        add { }
        remove { }
    }

    public PixelBuffer Picture { get; } = picture;

    public string Name => "Static picture test camera (SYNTHETIC)";

    public bool IsSynthetic => true;

    public int Width => Picture.Width;

    public int Height => Picture.Height;

    public bool IsRunning => _running;

    public string? LostReason => null;

    public long FrameSequence => _running ? 1 : 0;

    public void Start() => _running = true;

    public void Stop() => _running = false;

    public bool WithFrame(long notAfter100ns, CameraFrameAction use)
    {
        ArgumentNullException.ThrowIfNull(use);
        use(Picture.Pixels, Picture.Width, Picture.Height, Math.Min(notAfter100ns, Qpc.Now100ns()));
        return true;
    }

    public void Dispose() => Stop();
}
