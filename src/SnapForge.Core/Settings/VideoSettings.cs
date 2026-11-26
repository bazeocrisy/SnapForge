namespace SnapForge.Core.Settings;

/// <summary>Recording quality presets (owner decision: Standard and High).</summary>
public enum VideoQuality
{
    /// <summary>Good quality, moderate file size: ~0.08 bits per pixel per frame.</summary>
    Standard,

    /// <summary>Higher quality, larger files: ~0.16 bits per pixel per frame.</summary>
    High,
}

/// <summary>What a recording captures.</summary>
public enum RecordingSource
{
    Region,
    Window,
    Display,
}

/// <summary>
/// Screen recording preferences (Milestone V1). Defaults follow the owner decisions of 2026-11-26: system audio on,
/// microphone off, cursor on, 30 FPS (60 optional), countdown on (3 s), MP4 H.264/AAC, Standard quality.
/// </summary>
public sealed record VideoSettings
{
    public static IReadOnlyList<int> SupportedFrameRates { get; } = [30, 60];

    public static IReadOnlyList<int> CountdownChoices { get; } = [0, 3, 5, 10];

    public bool RecordSystemAudio { get; init; } = true;

    public bool RecordMicrophone { get; init; }

    /// <summary>Endpoint id of the chosen microphone; null = the Windows default microphone (followed when it changes).</summary>
    public string? MicrophoneDeviceId { get; init; }

    public bool CaptureCursor { get; init; } = true;

    public int FramesPerSecond { get; init; } = 30;

    public VideoQuality Quality { get; init; } = VideoQuality.Standard;

    public bool Countdown { get; init; } = true;

    public int CountdownSeconds { get; init; } = 3;

    public RecordingSource Source { get; init; } = RecordingSource.Region;

    /// <summary>Opens the finished recording in the Library player.</summary>
    public bool OpenAfterRecording { get; init; } = true;

    /// <summary>Recording stops (and is saved) when free space on the library drive falls below this.</summary>
    public int MinimumFreeSpaceMegabytes { get; init; } = 500;

    /// <summary>A warning is shown when the estimated remaining recording time falls below this.</summary>
    public int LowSpaceWarningMinutes { get; init; } = 10;

    public HotkeyGesture? StartStopHotkey { get; init; } = new(HotkeyModifiers.Shift, "F9");

    public HotkeyGesture? PauseResumeHotkey { get; init; } = new(HotkeyModifiers.Shift, "F10");

    public HotkeyGesture? CancelHotkey { get; init; }

    /// <summary>The settings with out-of-range values replaced by defaults (hand-edited or newer settings files).</summary>
    public VideoSettings Normalized()
    {
        var d = new VideoSettings();
        return this with
        {
            FramesPerSecond = SupportedFrameRates.Contains(FramesPerSecond) ? FramesPerSecond : d.FramesPerSecond,
            CountdownSeconds = CountdownChoices.Contains(CountdownSeconds) ? CountdownSeconds : d.CountdownSeconds,
            Quality = Enum.IsDefined(Quality) ? Quality : d.Quality,
            Source = Enum.IsDefined(Source) ? Source : d.Source,
            MinimumFreeSpaceMegabytes = Math.Clamp(MinimumFreeSpaceMegabytes, 100, 100_000),
            LowSpaceWarningMinutes = Math.Clamp(LowSpaceWarningMinutes, 1, 600),
        };
    }

    /// <summary>
    /// Video bit rate for a size, frame rate and quality: bits-per-pixel model, rounded to 100 kbit/s and kept
    /// between 1 and 80 Mbit/s. Standard 1080p30 ≈ 5 Mbit/s, High 1080p30 ≈ 10 Mbit/s.
    /// </summary>
    public static int BitRate(int width, int height, int fps, VideoQuality quality)
    {
        double bpp = quality == VideoQuality.High ? 0.16 : 0.08;
        double bits = width * (double)height * fps * bpp;
        int rounded = (int)(Math.Round(bits / 100_000) * 100_000);
        return Math.Clamp(rounded, 1_000_000, 80_000_000);
    }

    /// <summary>Estimated bytes per second of a recording (video + 160 kbit/s AAC + ~2 % container overhead).</summary>
    public static double BytesPerSecond(int videoBitRate, bool audio) => (videoBitRate + (audio ? 160_000 : 0)) / 8.0 * 1.02;
}
