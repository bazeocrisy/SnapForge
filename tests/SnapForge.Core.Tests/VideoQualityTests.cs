using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;

namespace SnapForge.Core.Tests;

/// <summary>
/// Release candidate 3 video quality (docs/VIDEO-QUALITY.md): the text-friendly bit rate model, the encoder tuning
/// and its fallback ladder, the camera overlay resampling kernel, and the fidelity metrics used by --video-quality.
/// </summary>
public class VideoQualityTests
{
    // ---- bit rate model

    [Theory]
    [InlineData(1920, 1080, 30, VideoQuality.Standard, 11_200_000)]
    [InlineData(1920, 1080, 30, VideoQuality.High, 17_400_000)]
    [InlineData(1920, 1080, 60, VideoQuality.Standard, 18_800_000)]
    [InlineData(1920, 1080, 60, VideoQuality.High, 29_300_000)]
    [InlineData(1280, 720, 30, VideoQuality.Standard, 5_000_000)]
    [InlineData(2560, 1440, 60, VideoQuality.Standard, 33_500_000)]
    [InlineData(3840, 2160, 30, VideoQuality.Standard, 44_800_000)]
    [InlineData(3840, 2160, 60, VideoQuality.High, 80_000_000)]
    [InlineData(320, 240, 30, VideoQuality.Standard, 1_000_000)]
    public void RC3_VQ_bit_rate_table(int w, int h, int fps, VideoQuality q, int expected) =>
        Assert.Equal(expected, VideoSettings.BitRate(w, h, fps, q));

    [Fact]
    public void RC3_VQ_standard_1080p30_is_in_the_text_friendly_range_and_more_than_double_rc2()
    {
        int now = VideoSettings.BitRate(1920, 1080, 30, VideoQuality.Standard);
        int rc2 = VideoSettings.Rc2BitRate(1920, 1080, 30, VideoQuality.Standard);
        Assert.Equal(5_000_000, rc2); // the owner's sample: ≈ 5 Mbit/s
        Assert.InRange(now, 10_000_000, 12_000_000);
        Assert.True(now >= 2 * rc2);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void RC3_VQ_new_model_is_above_rc2_for_every_unclamped_size_rate_and_quality(int w, int h)
    {
        foreach (int fps in VideoSettings.SupportedFrameRates)
        {
            foreach (VideoQuality q in Enum.GetValues<VideoQuality>())
            {
                Assert.True(VideoSettings.BitRate(w, h, fps, q) > VideoSettings.Rc2BitRate(w, h, fps, q), $"{w}x{h}@{fps} {q}");
            }
        }
    }

    [Fact]
    public void RC3_VQ_bit_rate_grows_with_pixels_frame_rate_and_quality_and_stays_clamped()
    {
        Assert.True(VideoSettings.BitRate(2560, 1440, 30, VideoQuality.Standard) > VideoSettings.BitRate(1920, 1080, 30, VideoQuality.Standard));
        Assert.True(VideoSettings.BitRate(1920, 1080, 60, VideoQuality.Standard) > VideoSettings.BitRate(1920, 1080, 30, VideoQuality.Standard));
        Assert.True(VideoSettings.BitRate(1920, 1080, 30, VideoQuality.High) > VideoSettings.BitRate(1920, 1080, 30, VideoQuality.Standard));
        Assert.Equal(1_000_000, VideoSettings.BitRate(16, 16, 30, VideoQuality.Standard));
        Assert.Equal(80_000_000, VideoSettings.BitRate(7680, 4320, 60, VideoQuality.High));
        Assert.Equal(1_000_000, VideoSettings.BitRate(16, 16, 0, VideoQuality.Standard)); // no division by zero / NaN
    }

    [Fact]
    public void RC3_VQ_60fps_costs_less_than_twice_30fps()
    {
        double ratio = VideoSettings.BitRate(1920, 1080, 60, VideoQuality.Standard) / (double)VideoSettings.BitRate(1920, 1080, 30, VideoQuality.Standard);
        Assert.InRange(ratio, 1.6, 1.75); // (60/30)^0.75 ≈ 1.68
    }

    [Fact]
    public void RC3_VQ_size_estimate_follows_the_new_bit_rate()
    {
        int rate = VideoSettings.BitRate(1920, 1080, 30, VideoQuality.Standard);
        double perSecond = VideoSettings.BytesPerSecond(rate, audio: true);
        Assert.Equal((11_200_000 + 160_000) / 8.0 * 1.02, perSecond, 6);
        double mbPerMinute = perSecond * 60 / (1024 * 1024);
        Assert.InRange(mbPerMinute, 82, 84); // RC2 Standard 1080p30 was ≈ 37 MB/min
        Assert.True(perSecond > VideoSettings.BytesPerSecond(VideoSettings.Rc2BitRate(1920, 1080, 30, VideoQuality.Standard), audio: true) * 2);
    }

    // ---- encoder tuning

    [Fact]
    public void RC3_VQ_default_tuning_is_high_profile_peak_constrained_vbr_two_second_gop_cabac_no_b_frames()
    {
        VideoEncoderTuning t = VideoEncoderTuning.Default;
        Assert.Equal(H264Profile.High, t.Profile);
        Assert.Equal(VideoRateControl.PeakConstrainedVbr, t.RateControl);
        Assert.Equal(60, t.GopFrames(30));
        Assert.Equal(120, t.GopFrames(60));
        Assert.True(t.Cabac);
        Assert.Equal(0, t.BFrames);
        Assert.False(t.LowLatency);
        Assert.True(t.RequiresConfirmedNoBFrames);
        Assert.Equal(BitRateModel.Current, t.BitRateModel);
        Assert.Equal(VideoSettings.BitRate(1920, 1080, 30, VideoQuality.Standard), t.BitRate(1920, 1080, 30, VideoQuality.Standard));
        Assert.Equal(22_400_000, t.PeakBitRate(11_200_000));
        Assert.Equal(100, (int)H264Profile.High); // eAVEncH264VProfile_High
        Assert.Equal(1, (int)VideoRateControl.PeakConstrainedVbr); // eAVEncCommonRateControlMode_PeakConstrainedVBR
    }

    [Fact]
    public void RC3_VQ_rc2_tuning_reproduces_release_candidate_2()
    {
        VideoEncoderTuning t = VideoEncoderTuning.Rc2;
        Assert.Equal(H264Profile.Main, t.Profile);
        Assert.Equal(VideoRateControl.EncoderDefault, t.RateControl);
        Assert.True(t.LowLatency);
        Assert.Null(t.Cabac);
        Assert.Null(t.BFrames);
        Assert.False(t.UsesCodecApi);
        Assert.Equal(60, t.GopFrames(30)); // RC2 set a key frame every 2 s on the media type
        Assert.Equal(5_000_000, t.BitRate(1920, 1080, 30, VideoQuality.Standard));
        Assert.Single(t.FallbackLadder());
    }

    [Fact]
    public void RC3_VQ_fallback_ladder_ends_at_rc2_encoder_settings_and_never_risks_b_frames()
    {
        IReadOnlyList<VideoEncoderTuning> ladder = VideoEncoderTuning.Default.FallbackLadder();
        Assert.Same(VideoEncoderTuning.Default, ladder[0]);
        Assert.True(ladder.Count >= 3);
        VideoEncoderTuning last = ladder[^1];
        Assert.Equal(H264Profile.Main, last.Profile);
        Assert.True(last.LowLatency);
        Assert.False(last.UsesCodecApi);
        Assert.Equal(BitRateModel.Current, last.BitRateModel); // the bit rate fix survives every fallback
        Assert.All(ladder, t => Assert.True(t.LowLatency || t.BFrames == 0, t.Name)); // timestamps stay exact
        Assert.All(ladder, t => Assert.Equal(BitRateModel.Current, t.BitRateModel));
        Assert.Equal(ladder.Count, ladder.Select(t => t with { Name = "x" }).Distinct().Count());
        Assert.Contains(ladder, t => t.LowLatency && t.UsesCodecApi && t.Profile == H264Profile.High);
        Assert.All(ladder, t => Assert.False(string.IsNullOrWhiteSpace(t.Describe())));
    }

    // ---- camera overlay resampling

    [Theory]
    [InlineData(324, 0, 1080, 1080, false)]
    [InlineData(324, 280, 720, 1280, true)]
    [InlineData(100, 0, 64, 64, false)]
    [InlineData(64, 0, 64, 64, true)]
    [InlineData(7, 0.25, 33.5, 40, false)]
    public void RC3_VQ_resample_weights_sum_to_one_and_stay_inside_the_source(int n, double start, double length, int limit, bool mirror)
    {
        var k = ResampleKernel.Create(n, start, length, limit, mirror);
        Assert.Equal(n, k.Length);
        for (int i = 0; i < n; i++)
        {
            int sum = 0;
            for (int t = 0; t < k.Count[i]; t++)
            {
                int idx = k.Index[k.Start[i] + t];
                Assert.InRange(idx, 0, limit - 1);
                Assert.True(k.Weight[k.Start[i] + t] >= 0);
                sum += k.Weight[k.Start[i] + t];
            }

            Assert.Equal(ResampleKernel.One, sum);
        }

        Assert.Equal(length / n > 1, k.IsAreaAverage);
    }

    [Fact]
    public void RC3_VQ_downscale_averages_fine_detail_instead_of_aliasing()
    {
        // A one-pixel stripe pattern (0, 255, 0, 255 …) scaled 3:1 must become an even grey (mean ≈ 85 or 170 per
        // output pixel depending on phase — never pure 0 or 255, which is what point/2-tap sampling produces).
        int[] source = [.. Enumerable.Range(0, 300).Select(i => i % 2 == 0 ? 0 : 255)];
        var k = ResampleKernel.Create(100, 0, 300, 300, mirror: false);
        Assert.True(k.IsAreaAverage);
        Assert.True(k.MaxTaps >= 3);
        for (int i = 0; i < 100; i++)
        {
            double v = 0;
            for (int t = 0; t < k.Count[i]; t++)
            {
                v += source[k.Index[k.Start[i] + t]] * k.Weight[k.Start[i] + t] / (double)ResampleKernel.One;
            }

            Assert.InRange(v, 80, 175);
        }

        // 2:1 exactly: every output pixel is the mean of its two source pixels.
        var half = ResampleKernel.Create(150, 0, 300, 300, mirror: false);
        Assert.All(Enumerable.Range(0, 150), i => Assert.Equal(2, half.Count[i]));
        Assert.All(half.Weight, w => Assert.Equal(ResampleKernel.One / 2, w));
    }

    [Fact]
    public void RC3_VQ_mirror_reverses_and_upscale_is_bilinear()
    {
        var plain = ResampleKernel.Create(50, 0, 200, 200, mirror: false);
        var mirrored = ResampleKernel.Create(50, 0, 200, 200, mirror: true);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(plain.Index.AsSpan(plain.Start[49 - i], plain.Count[49 - i]).ToArray(), mirrored.Index.AsSpan(mirrored.Start[i], mirrored.Count[i]).ToArray());
        }

        Assert.Equal(196, mirrored.Index[mirrored.Start[0]]); // the first output pixel shows the right edge
        var up = ResampleKernel.Create(200, 0, 100, 100, mirror: false);
        Assert.False(up.IsAreaAverage);
        Assert.True(up.MaxTaps <= 2);
        var same = ResampleKernel.Create(64, 0, 64, 64, mirror: false);
        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(i, same.Index[same.Start[i]])); // 1:1 is an exact copy
        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(1, same.Count[i]));
    }

    // ---- fidelity metrics

    [Fact]
    public void RC3_VQ_identical_regions_score_perfectly()
    {
        PixelBuffer a = Pattern(64, 48, 0);
        RegionQuality q = ImageQuality.Compare(a, a.Clone(), new PixelRect(0, 0, 64, 48));
        Assert.Equal(ImageQuality.MaxPsnr, q.PsnrDb);
        Assert.Equal(1.0, q.Ssim, 6);
        Assert.Equal(0, q.MeanAbsError);
    }

    [Fact]
    public void RC3_VQ_psnr_matches_the_formula_and_errors_rank_correctly()
    {
        PixelBuffer a = Pattern(32, 32, 0);
        PixelBuffer b = a.Clone();
        for (int i = 0; i < b.Pixels.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                b.Pixels[i + c] = (byte)Math.Min(255, b.Pixels[i + c] + 4); // luma +4 everywhere (no clipping below 251)
            }
        }

        RegionQuality q = ImageQuality.Compare(a, b, new PixelRect(0, 0, 32, 32));
        Assert.Equal(10 * Math.Log10(255.0 * 255 / 16), q.PsnrDb, 1);
        Assert.Equal(4, q.MeanAbsError, 1);

        PixelBuffer noisy = a.Clone();
        var rng = new Random(7);
        for (int i = 0; i < noisy.Pixels.Length; i += 4)
        {
            int d = rng.Next(-40, 41);
            for (int c = 0; c < 3; c++)
            {
                noisy.Pixels[i + c] = (byte)Math.Clamp(noisy.Pixels[i + c] + d, 0, 255);
            }
        }

        RegionQuality n = ImageQuality.Compare(a, noisy, new PixelRect(0, 0, 32, 32));
        Assert.True(n.PsnrDb < q.PsnrDb);
        Assert.True(n.Ssim < q.Ssim);
        Assert.True(n.MeanAbsError > q.MeanAbsError);
    }

    [Fact]
    public void RC3_VQ_metrics_only_look_at_the_region()
    {
        PixelBuffer a = Pattern(64, 64, 0), b = a.Clone();
        b.FillRect(new PixelRect(32, 0, 32, 64), new ArgbColor(255, 0, 0, 0));
        Assert.Equal(ImageQuality.MaxPsnr, ImageQuality.Compare(a, b, new PixelRect(0, 0, 32, 64)).PsnrDb);
        Assert.True(ImageQuality.Compare(a, b, new PixelRect(32, 0, 32, 64)).PsnrDb < 30);
        Assert.Throws<ArgumentException>(() => ImageQuality.Compare(a, new PixelBuffer(8, 8), new PixelRect(0, 0, 8, 8)));
    }

    private static PixelBuffer Pattern(int w, int h, int seed)
    {
        var p = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int o = ((y * w) + x) * 4;
                byte v = (byte)(((x * 7) + (y * 3) + seed) % 200);
                p.Pixels[o] = v;
                p.Pixels[o + 1] = (byte)(v / 2);
                p.Pixels[o + 2] = (byte)(200 - v);
                p.Pixels[o + 3] = 255;
            }
        }

        return p;
    }
}
