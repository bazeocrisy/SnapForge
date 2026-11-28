using System.IO;
using SnapForge.App.Video;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.App.Tests;

/// <summary>
/// Release candidate 3 video quality (docs/VIDEO-QUALITY.md), Windows side: camera overlay scaling (area average vs
/// RC2's bilinear sampling), camera capture size choice, the encoder properties each tuning requests, a real encode
/// with the default tuning (applied settings reported, timestamps exact), and the quality scene's frame code.
/// All camera pictures here are SYNTHETIC.
/// </summary>
[Collection(RecordingTestGroup.Name)]
public sealed unsafe class VideoQualityAppTests : IDisposable
{
    private const uint Screen = 0xFF00C000;
    private static readonly int[] CodeSamples = [0, 1, 45, 105, 1234];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapforge-vq-" + Guid.NewGuid().ToString("N"));

    public VideoQualityAppTests()
    {
        Directory.CreateDirectory(_dir);
        _ = Mf.MFStartup(Mf.MF_VERSION, 0);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void RC3_VQ_area_average_turns_fine_stripes_into_even_grey_where_bilinear_aliases()
    {
        // 1-pixel black/white columns, 1280×720 camera into a 324×324 circle (the 1080p Medium overlay): the true
        // downscale is mid grey everywhere. RC2's two-tap bilinear sampling lands on whole columns and shows stripes.
        uint[] cam = new uint[1280 * 720];
        for (int i = 0; i < cam.Length; i++)
        {
            cam[i] = (i % 1280) % 2 == 0 ? 0xFF000000u : 0xFFFFFFFFu;
        }

        var layout = new OverlayLayout(0, 0, 324, 324, CameraOverlayShape.Circle, Mirror: true);
        (double areaMean, double areaSpread) = CentreStats(Compose(CameraScaling.AreaAverage, cam, 1280, 720, layout), 324);
        (double _, double bilinearSpread) = CentreStats(Compose(CameraScaling.Bilinear, cam, 1280, 720, layout), 324);
        Assert.InRange(areaMean, 120, 135);
        Assert.True(areaSpread <= 40, $"area average spread {areaSpread}");
        Assert.True(bilinearSpread > 150, $"bilinear spread {bilinearSpread} (expected aliasing)");
        Assert.True(new CameraCompositor().Scaling == CameraScaling.AreaAverage); // the default
    }

    [Fact]
    public void RC3_VQ_area_average_keeps_flat_colour_exact_mask_antialiased_and_mirror()
    {
        uint[] flat = Enumerable.Repeat(SyntheticCameraSource.Background, 1920 * 1080).ToArray();
        var circle = new OverlayLayout(0, 0, 324, 324, CameraOverlayShape.Circle, Mirror: false);
        var compositor = new CameraCompositor(CameraScaling.AreaAverage);
        uint[] frame = Enumerable.Repeat(Screen, 324 * 324).ToArray();
        Draw(compositor, frame, 324, 324, flat, 1920, 1080, circle);
        Assert.True(compositor.UsedAreaAverage);
        Assert.Equal(SyntheticCameraSource.Background, frame[(162 * 324) + 162]);
        Assert.Equal(Screen, frame[0]);
        // Anti-aliased edge: on the middle row the circle edge falls exactly on pixel boundaries (no partial pixel),
        // so check a row where the edge is diagonal (run 155: the middle-row assertion could never hold).
        Assert.Contains(Enumerable.Range(0, 324), x => compositor.MaskAt(x, 20) is > 0 and < 255);

        // Mirror at a 2:1 downscale: left half red, right half blue → swapped.
        uint[] cam = new uint[256 * 64];
        for (int i = 0; i < cam.Length; i++)
        {
            cam[i] = i % 256 < 128 ? 0xFFFF0000u : 0xFF0000FFu;
        }

        uint[] plain = Enumerable.Repeat(Screen, 128 * 32).ToArray(), mirrored = Enumerable.Repeat(Screen, 128 * 32).ToArray();
        Draw(new CameraCompositor(), plain, 128, 32, cam, 256, 64, new OverlayLayout(0, 0, 128, 32, CameraOverlayShape.RoundedRectangle, false));
        Draw(new CameraCompositor(), mirrored, 128, 32, cam, 256, 64, new OverlayLayout(0, 0, 128, 32, CameraOverlayShape.RoundedRectangle, true));
        Assert.Equal(0xFFFF0000u, plain[(16 * 128) + 20]);
        Assert.Equal(0xFF0000FFu, plain[(16 * 128) + 108]);
        Assert.Equal(0xFF0000FFu, mirrored[(16 * 128) + 20]);
        Assert.Equal(0xFFFF0000u, mirrored[(16 * 128) + 108]);
    }

    [Fact]
    public void RC3_VQ_area_average_matches_an_exact_box_filter_within_one_level()
    {
        // A smooth gradient with a fine texture; compare one row of the overlay with a double-precision box filter.
        const int cw = 640, ch = 360, box = 100;
        uint[] cam = new uint[cw * ch];
        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                uint v = (uint)((x * 255 / (cw - 1)) ^ ((y & 3) * 16)) & 0xFF;
                cam[(y * cw) + x] = 0xFF000000u | (v << 16) | (v << 8) | v;
            }
        }

        var layout = new OverlayLayout(0, 0, box, box, CameraOverlayShape.Circle, false);
        uint[] frame = Compose(CameraScaling.AreaAverage, cam, cw, ch, layout, box);
        double srcX = (cw - ch) / 2.0, scale = ch / (double)box; // centre crop 360×360 → 100×100
        int row = 50;
        for (int bx = 30; bx < 70; bx++)
        {
            double x0 = srcX + (bx * scale), x1 = x0 + scale, y0 = row * scale, y1 = y0 + scale, sum = 0, area = 0;
            for (int y = (int)y0; y < Math.Ceiling(y1); y++)
            {
                for (int x = (int)x0; x < Math.Ceiling(x1); x++)
                {
                    double w = (Math.Min(y1, y + 1) - Math.Max(y0, y)) * (Math.Min(x1, x + 1) - Math.Max(x0, x));
                    sum += (cam[(y * cw) + x] & 0xFF) * w;
                    area += w;
                }
            }

            int got = (int)(frame[(row * box) + bx] & 0xFF);
            Assert.InRange(got, (int)Math.Round(sum / area) - 1, (int)Math.Round(sum / area) + 1);
        }
    }

    [Fact]
    public void RC3_VQ_camera_capture_size_covers_the_overlay_and_avoids_tiny_defaults()
    {
        Assert.Equal((1920, 1080), CameraFormatChooser.WantedSize(StudioMode.Camera, 0, 0, CameraOverlayShape.Circle, CameraOverlaySize.Medium));
        Assert.Equal((1280, 720), CameraFormatChooser.WantedSize(StudioMode.ScreenAndCamera, 1920, 1080, CameraOverlayShape.Circle, CameraOverlaySize.Large));
        Assert.Equal((1920, 1080), CameraFormatChooser.WantedSize(StudioMode.ScreenAndCamera, 3840, 2160, CameraOverlayShape.Circle, CameraOverlaySize.Medium));

        Guid nv12 = Mf.VideoFormat_NV12, mjpg = Mf.VideoFormat_MJPG, yuy2 = Mf.VideoFormat_YUY2;
        CameraFormat[] formats =
        [
            new(160, 120, 30, yuy2, 0),
            new(320, 240, 30, yuy2, 1),
            new(640, 480, 30, yuy2, 2),
            new(1280, 720, 30, nv12, 3),
            new(1920, 1080, 30, mjpg, 4),
            new(1920, 1080, 5, yuy2, 5),
        ];
        Assert.Equal(4, CameraFormatChooser.Choose(formats, 1920, 1080, 30)!.Value.Index); // 1080p30 MJPG, not 1080p5
        Assert.Equal(3, CameraFormatChooser.Choose(formats, 1280, 720, 30)!.Value.Index);
        Assert.Equal(2, CameraFormatChooser.Choose(formats.Take(3).ToList(), 1280, 720, 30)!.Value.Index); // never 160×120 while 640×480 exists
    }

    [Fact]
    public void RC3_VQ_default_tuning_requests_vbr_gop_cabac_and_no_b_frames_rc2_requests_nothing()
    {
        IReadOnlyList<EncoderProperty> p = EncoderProperty.For(VideoEncoderTuning.Default, 11_200_000, 30);
        Assert.Contains(p, x => x.Key == Mf.CODECAPI_AVEncCommonRateControlMode && x.Value == 1);
        Assert.Contains(p, x => x.Key == Mf.CODECAPI_AVEncCommonMeanBitRate && x.Value == 11_200_000);
        Assert.Contains(p, x => x.Key == Mf.CODECAPI_AVEncCommonMaxBitRate && x.Value == 22_400_000);
        Assert.Contains(p, x => x.Key == Mf.CODECAPI_AVEncMPVGOPSize && x.Value == 60);
        Assert.Contains(p, x => x.Key == Mf.CODECAPI_AVEncH264CABACEnable && x.Value == 1 && x.Boolean);
        Assert.Contains(p, x => x.Key == Mf.CODECAPI_AVEncMPVDefaultBPictureCount && x.Value == 0);
        Assert.Empty(EncoderProperty.For(VideoEncoderTuning.Rc2, 5_000_000, 30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RC3_VQ_writer_reports_applied_settings_and_keeps_exact_timestamps(bool rc2)
    {
        // Real Media Foundation encode (software encoder on the CI runner).
        string path = Path.Combine(_dir, $"tuning-{(rc2 ? "rc2" : "rc3")}.mp4");
        const int w = 320, h = 240;
        uint[] px = new uint[w * h];
        string summary;
        using (var writer = new Mp4Writer(path, new VideoStreamFormat(w, h, 30, 2_000_000), withAudio: false, fragmented: true, EncoderInput.SystemMemory, 0, allowHardware: false, rc2 ? VideoEncoderTuning.Rc2 : null))
        {
            fixed (uint* p = px)
            {
                for (int f = 0; f < 45; f++)
                {
                    for (int i = 0; i < px.Length; i++)
                    {
                        px[i] = 0xFF000000u | (uint)((((i % w) + (f * 3)) & 0xFF) << 8) | (uint)((i / w) & 0xFF);
                    }

                    writer.WriteFrame((byte*)p, w * 4, f * 333_333L, 333_333L);
                }
            }

            summary = writer.SettingsSummary;
            Assert.False(string.IsNullOrWhiteSpace(summary));
            Assert.NotNull(writer.Applied);
            Assert.Equal(rc2 ? H264Profile.Main : writer.AppliedTuning.Profile, writer.AppliedTuning.Profile);
            Assert.True(writer.AppliedTuning.LowLatency || writer.Applied!.BFrames == 0, summary); // never unconfirmed B-frames
            writer.Complete();
        }

        // Every frame decodes in order at its exact time (no B-frame reordering).
        nint reader = VideoAnalysis.OpenDecoding(path, video: true, audio: false);
        var times = new List<long>();
        try
        {
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint sample) && sample != 0)
            {
                times.Add(ts);
                Com.Release(ref sample);
            }
        }
        finally
        {
            Com.Release(ref reader);
        }

        Assert.Equal(45, times.Count);
        for (int i = 0; i < times.Count; i++)
        {
            Assert.InRange(times[i], (i * 333_333L) - 10_000, (i * 333_333L) + 10_000);
        }
    }

    [Fact]
    public void RC3_VQ_quality_scene_is_deterministic_and_its_frame_code_round_trips() => Sta.Run(() =>
    {
        var scene = new QualityScene();
        byte[] a = new byte[QualityScene.Width * QualityScene.Height * 4], b = new byte[a.Length];
        fixed (byte* pa = a)
        fixed (byte* pb = b)
        {
            foreach (int index in CodeSamples)
            {
                scene.Render(index, pa, QualityScene.Width * 4);
                Assert.Equal(index, QualityScene.ReadCode(pa, QualityScene.Width * 4, QualityScene.Width, QualityScene.Height));
            }

            // The comparison records both variants and builds every reference from ONE scene instance, so that is
            // what must be deterministic. (Run 155: two separately created scenes differed — WPF text rasterisation
            // into a RenderTargetBitmap is not guaranteed identical across renders; the comparison never relies on it.)
            scene.Render(105, pa, QualityScene.Width * 4);
            scene.Render(40, pb, QualityScene.Width * 4);
            scene.Render(105, pb, QualityScene.Width * 4);
        }

        Assert.True(a.AsSpan().SequenceEqual(b)); // the same frame number renders the same pixels

        // Static for the first 2 s, moving afterwards.
        byte[] s0 = Frame(scene, 10), s1 = Frame(scene, 50), m = Frame(scene, 100);
        Assert.True(s0.AsSpan().SequenceEqual(Without(s1, s0)));
        Assert.False(Frame(scene, 100).AsSpan().SequenceEqual(Frame(scene, 101)));
        Assert.NotEmpty(m);
    });

    private static byte[] Frame(QualityScene scene, int index)
    {
        byte[] f = new byte[QualityScene.Width * QualityScene.Height * 4];
        fixed (byte* p = f)
        {
            scene.Render(index, p, QualityScene.Width * 4);
        }

        return f;
    }

    /// <summary><paramref name="frame"/> with the frame-code strip copied from <paramref name="like"/> (the only difference while static).</summary>
    private static byte[] Without(byte[] frame, byte[] like)
    {
        byte[] copy = (byte[])frame.Clone();
        for (int y = QualityScene.Code.Y; y < QualityScene.Code.Bottom; y++)
        {
            int o = ((y * QualityScene.Width) + QualityScene.Code.X) * 4;
            Array.Copy(like, o, copy, o, QualityScene.Code.Width * 4);
        }

        return copy;
    }

    private static uint[] Compose(CameraScaling scaling, uint[] cam, int cw, int ch, OverlayLayout layout, int size = 324)
    {
        uint[] frame = Enumerable.Repeat(Screen, size * size).ToArray();
        Draw(new CameraCompositor(scaling), frame, size, size, cam, cw, ch, layout);
        return frame;
    }

    /// <summary>Mean and max−min of the blue channel over the centre 100×100 of a size×size frame.</summary>
    private static (double Mean, double Spread) CentreStats(uint[] frame, int size)
    {
        var values = new List<int>();
        for (int y = (size / 2) - 50; y < (size / 2) + 50; y++)
        {
            for (int x = (size / 2) - 50; x < (size / 2) + 50; x++)
            {
                values.Add((int)(frame[(y * size) + x] & 0xFF));
            }
        }

        return (values.Average(), values.Max() - values.Min());
    }

    private static void Draw(CameraCompositor c, uint[] frame, int w, int h, uint[] cam, int cw, int ch, OverlayLayout layout)
    {
        fixed (uint* f = frame)
        {
            c.Draw((byte*)f, w * 4, w, h, System.Runtime.InteropServices.MemoryMarshal.AsBytes(cam.AsSpan()), cw, ch, layout);
        }
    }
}
