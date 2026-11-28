using SnapForge.Core.Settings;

namespace SnapForge.Core.Video;

/// <summary>H.264 profile (values as in eAVEncH264VProfile / MF_MT_MPEG2_PROFILE).</summary>
public enum H264Profile
{
    Baseline = 66,
    Main = 77,
    High = 100,
}

/// <summary>Encoder rate control (values as in eAVEncCommonRateControlMode, except <see cref="EncoderDefault"/>).</summary>
public enum VideoRateControl
{
    /// <summary>Nothing is requested: the encoder's own default (constant bit rate for the Windows software encoder).</summary>
    EncoderDefault = -1,

    /// <summary>Constant bit rate.</summary>
    Cbr = 0,

    /// <summary>Variable bit rate around the mean, never above the peak: bits go to the frames that need them.</summary>
    PeakConstrainedVbr = 1,
}

/// <summary>Which bit rate formula a tuning uses.</summary>
public enum BitRateModel
{
    /// <summary>Release candidate 2: 0.08 / 0.16 bits per pixel, linear in FPS (<see cref="VideoSettings.Rc2BitRate"/>).</summary>
    Rc2,

    /// <summary>Release candidate 3 and later: <see cref="VideoSettings.BitRate"/>.</summary>
    Current,
}

/// <summary>
/// Technical H.264 encoder settings for a recording (not shown to the user; the user picks Standard or High). The
/// writer applies what the chosen encoder accepts and steps down <see cref="FallbackLadder"/> when an encoder refuses
/// a combination, logging what was applied. <see cref="Default"/> is what recordings use; <see cref="Rc2"/>
/// reproduces release candidate 2 for the quality comparison (docs/VIDEO-QUALITY.md).
/// </summary>
public sealed record VideoEncoderTuning
{
    /// <summary>Short name for logs and reports.</summary>
    public required string Name { get; init; }

    public BitRateModel BitRateModel { get; init; } = BitRateModel.Current;

    public H264Profile Profile { get; init; } = H264Profile.High;

    public VideoRateControl RateControl { get; init; } = VideoRateControl.PeakConstrainedVbr;

    /// <summary>Peak bit rate as a multiple of the mean (peak-constrained VBR only).</summary>
    public double PeakToMean { get; init; } = 2.0;

    /// <summary>Key frame interval in seconds (also bounds what an interrupted fragmented recording loses).</summary>
    public double GopSeconds { get; init; } = 2.0;

    /// <summary>CABAC entropy coding (Main/High profile; ~10 % fewer bits than CAVLC). Null = encoder default.</summary>
    public bool? Cabac { get; init; } = true;

    /// <summary>
    /// B-frames requested. Always 0 in shipped tunings: with B-frames the decoded timestamps of fragmented MP4s were
    /// measured up to 300 ms off (CI A/V sync test, 2026-11-26). Null = not requested (encoder default).
    /// </summary>
    public int? BFrames { get; init; } = 0;

    /// <summary>
    /// Media Foundation low-latency mode. It also forbids B-frames, but on many encoders it trades quality for latency
    /// (smaller rate-control buffer, no look-ahead). With <see cref="BFrames"/> = 0 confirmed by the encoder it is not
    /// needed; the writer turns it on again when the encoder cannot confirm that (<see cref="RequiresConfirmedNoBFrames"/>).
    /// </summary>
    public bool LowLatency { get; init; }

    /// <summary>Whether any encoder property (rate control, GOP, CABAC, B-frames) is requested through ICodecAPI.</summary>
    public bool UsesCodecApi => RateControl != VideoRateControl.EncoderDefault || Cabac is not null || BFrames is not null || GopSeconds > 0;

    /// <summary>True when timestamps rely on the encoder confirming no B-frames (low-latency mode off).</summary>
    public bool RequiresConfirmedNoBFrames => !LowLatency;

    /// <summary>The settings every recording uses (release candidate 3).</summary>
    public static VideoEncoderTuning Default { get; } = new() { Name = "rc3-default" };

    /// <summary>
    /// Release candidate 2 exactly: 0.08 / 0.16 bits per pixel, Main profile, low-latency mode, the encoder's default
    /// rate control (CBR for the Windows software encoder), key frame every 2 s set only on the media type, nothing
    /// through ICodecAPI.
    /// </summary>
    public static VideoEncoderTuning Rc2 { get; } = new()
    {
        Name = "rc2",
        BitRateModel = BitRateModel.Rc2,
        Profile = H264Profile.Main,
        RateControl = VideoRateControl.EncoderDefault,
        Cabac = null,
        BFrames = null,
        LowLatency = true,
        GopSeconds = 0,
    };

    /// <summary>Mean bit rate for a recording with this tuning.</summary>
    public int BitRate(int width, int height, int fps, VideoQuality quality) => BitRateModel == BitRateModel.Rc2
        ? VideoSettings.Rc2BitRate(width, height, fps, quality)
        : VideoSettings.BitRate(width, height, fps, quality);

    /// <summary>Peak bit rate (peak-constrained VBR), at least the mean.</summary>
    public int PeakBitRate(int meanBitRate) => (int)Math.Min(int.MaxValue, Math.Max(meanBitRate, meanBitRate * Math.Max(1, PeakToMean)));

    /// <summary>Frames between key frames at a frame rate (at least 1; the RC2 tuning keeps its 2 s media-type spacing).</summary>
    public int GopFrames(int fps) => Math.Max(1, (int)Math.Round(fps * (GopSeconds > 0 ? GopSeconds : 2.0)));

    /// <summary>
    /// What to try, in order, when an encoder refuses a combination (the sink writer fails to accept it): this tuning;
    /// the same with low-latency mode on (if it was off); the same profile with no ICodecAPI properties and low latency;
    /// finally release candidate 2's encoder settings (Main profile, low latency) at this tuning's bit rate — the
    /// combination every encoder in CI and on the owner's PC accepted. Never contains duplicates.
    /// </summary>
    public IReadOnlyList<VideoEncoderTuning> FallbackLadder()
    {
        var list = new List<VideoEncoderTuning> { this };
        void Add(VideoEncoderTuning t)
        {
            if (!list.Any(x => x with { Name = t.Name } == t))
            {
                list.Add(t);
            }
        }

        if (!LowLatency)
        {
            Add(this with { Name = Name + "+low-latency", LowLatency = true });
        }

        Add(this with { Name = Name + "-no-codecapi", LowLatency = true, RateControl = VideoRateControl.EncoderDefault, Cabac = null, BFrames = null, GopSeconds = 0 });
        Add(Rc2 with { Name = Name + "-rc2-encoder", BitRateModel = BitRateModel });
        return list;
    }

    /// <summary>One line for logs and reports.</summary>
    public string Describe() =>
        $"{Name}: {Profile} profile, {(RateControl == VideoRateControl.EncoderDefault ? "encoder-default rate control" : RateControl.ToString())}"
        + (RateControl == VideoRateControl.PeakConstrainedVbr ? $" (peak {PeakToMean:0.#}× mean)" : string.Empty)
        + $", GOP {(GopSeconds > 0 ? $"{GopSeconds:0.#} s" : "2 s (media type only)")}, CABAC {(Cabac is { } c ? (c ? "on" : "off") : "default")}"
        + $", B-frames {(BFrames is { } b ? b.ToString(System.Globalization.CultureInfo.InvariantCulture) : "default")}, low latency {(LowLatency ? "on" : "off")}, bit rate model {BitRateModel}";
}
