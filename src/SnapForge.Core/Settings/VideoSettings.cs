namespace SnapForge.Core.Settings;

/// <summary>Recording quality presets (owner decision: Standard and High).</summary>
public enum VideoQuality
{
    /// <summary>
    /// Good quality, moderate file size: 0.18 bits per pixel per frame at 30 FPS (1080p30 ≈ 11.2 Mbit/s), legible
    /// desktop text. Release candidate 2 used 0.08 (≈ 5 Mbit/s), which smeared small text (docs/VIDEO-QUALITY.md).
    /// </summary>
    Standard,

    /// <summary>Higher quality, larger files: 0.28 bits per pixel per frame at 30 FPS (1080p30 ≈ 17.4 Mbit/s).</summary>
    High,
}

/// <summary>What a recording captures.</summary>
public enum RecordingSource
{
    Region,
    Window,
    Display,
}

/// <summary>What SnapForge Studio records (SF-006).</summary>
public enum StudioMode
{
    /// <summary>The screen only (the camera is never opened).</summary>
    Screen,

    /// <summary>The screen with the camera composited in a corner.</summary>
    ScreenAndCamera,

    /// <summary>The camera picture only, at the camera's own size.</summary>
    Camera,
}

/// <summary>Shape of the camera overlay on a screen recording.</summary>
public enum CameraOverlayShape
{
    Circle,
    RoundedRectangle,
}

/// <summary>Size preset of the camera overlay (a share of the recording's shorter side).</summary>
public enum CameraOverlaySize
{
    Small,
    Medium,
    Large,
}

/// <summary>Corner of the recording the camera overlay sits in.</summary>
public enum CameraCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
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

    /// <summary>VID-06: starts a recording; while recording, pauses and resumes it (default Shift+F9).</summary>
    public HotkeyGesture? RecordHotkey { get; init; } = new(HotkeyModifiers.Shift, "F9");

    /// <summary>VID-06: stops and saves the recording (default Shift+F10).</summary>
    public HotkeyGesture? StopHotkey { get; init; } = new(HotkeyModifiers.Shift, "F10");

    /// <summary>
    /// VID-08 "without history": when off, a finished recording is saved only where the user chooses and not kept
    /// in the Library.
    /// </summary>
    public bool SaveToLibrary { get; init; } = true;

    public HotkeyGesture? CancelHotkey { get; init; }

    /// <summary>SnapForge Studio: screen only (default), screen + camera, or camera only. The camera is off by default.</summary>
    public StudioMode StudioMode { get; init; } = StudioMode.Screen;

    /// <summary>Symbolic link of the chosen camera; null = the first camera Windows lists.</summary>
    public string? CameraDeviceId { get; init; }

    public CameraOverlayShape CameraShape { get; init; } = CameraOverlayShape.Circle;

    public CameraOverlaySize CameraSize { get; init; } = CameraOverlaySize.Medium;

    public CameraCorner CameraCorner { get; init; } = CameraCorner.BottomRight;

    /// <summary>Mirror the camera picture (like looking in a mirror). Applies to the overlay and to camera-only recordings.</summary>
    public bool CameraMirror { get; init; } = true;

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
            StudioMode = Enum.IsDefined(StudioMode) ? StudioMode : d.StudioMode,
            CameraShape = Enum.IsDefined(CameraShape) ? CameraShape : d.CameraShape,
            CameraSize = Enum.IsDefined(CameraSize) ? CameraSize : d.CameraSize,
            CameraCorner = Enum.IsDefined(CameraCorner) ? CameraCorner : d.CameraCorner,
            MinimumFreeSpaceMegabytes = Math.Clamp(MinimumFreeSpaceMegabytes, 100, 100_000),
            LowSpaceWarningMinutes = Math.Clamp(LowSpaceWarningMinutes, 1, 600),
        };
    }

    /// <summary>Bits per pixel per frame at the 30 FPS reference rate (release candidate 3 and later).</summary>
    public static double BitsPerPixel(VideoQuality quality) => quality == VideoQuality.High ? 0.28 : 0.18;

    /// <summary>
    /// Video bit rate (the encoder's mean target) for a size, frame rate and quality: bits per pixel at 30 FPS, scaled
    /// by (fps ÷ 30)^0.75 — at a higher frame rate consecutive frames differ less, so 60 FPS needs ~1.7×, not 2×, the
    /// bits of 30 FPS. Rounded to 100 kbit/s and kept between 1 and 80 Mbit/s.
    /// Standard: 1080p30 ≈ 11.2 Mbit/s, 1080p60 ≈ 18.8 Mbit/s; High: 1080p30 ≈ 17.4 Mbit/s.
    /// Desktop text needs roughly twice the bits of camera video at the same size (sharp edges on flat colour), which
    /// is why release candidate 2's 0.08 bits per pixel looked grainy (<see cref="Rc2BitRate"/>; docs/VIDEO-QUALITY.md).
    /// </summary>
    public static int BitRate(int width, int height, int fps, VideoQuality quality)
    {
        double rate = Math.Max(1, fps);
        double bits = width * (double)height * 30 * BitsPerPixel(quality) * Math.Pow(rate / 30.0, 0.75);
        return RoundAndClamp(bits);
    }

    /// <summary>
    /// The release candidate 2 bit rate model, kept only to reproduce old recordings in the quality comparison
    /// (<c>--video-quality</c>): 0.08 (Standard) / 0.16 (High) bits per pixel per frame, linear in frame rate.
    /// Standard 1080p30 ≈ 5 Mbit/s, High 1080p30 ≈ 10 Mbit/s.
    /// </summary>
    public static int Rc2BitRate(int width, int height, int fps, VideoQuality quality)
    {
        double bpp = quality == VideoQuality.High ? 0.16 : 0.08;
        return RoundAndClamp(width * (double)height * fps * bpp);
    }

    private static int RoundAndClamp(double bits)
    {
        int rounded = (int)(Math.Round(bits / 100_000) * 100_000);
        return Math.Clamp(rounded, 1_000_000, 80_000_000);
    }

    /// <summary>Estimated bytes per second of a recording (video + 160 kbit/s AAC + ~2 % container overhead).</summary>
    public static double BytesPerSecond(int videoBitRate, bool audio) => (videoBitRate + (audio ? 160_000 : 0)) / 8.0 * 1.02;
}
