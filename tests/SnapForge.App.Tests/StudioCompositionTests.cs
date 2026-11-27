using SnapForge.App.Video;
using SnapForge.Core.Settings;

namespace SnapForge.App.Tests;

/// <summary>
/// SnapForge Studio: overlay layout, the CPU compositor (crop, scale, mirror, antialiased mask), the composite and
/// camera-only frame sources, the camera frame ring, format choice and pixel conversion. All camera input here is
/// SYNTHETIC (CI has no camera); the real-device path is covered by docs/VIDEO-ACCEPTANCE.md (physical PC).
/// </summary>
public unsafe class StudioCompositionTests
{
    private const uint Screen = 0xFF00C000; // BGRA green

    [Theory]
    [InlineData(CameraCorner.TopLeft)]
    [InlineData(CameraCorner.TopRight)]
    [InlineData(CameraCorner.BottomLeft)]
    [InlineData(CameraCorner.BottomRight)]
    public void Layout_is_in_the_chosen_corner_with_a_margin_and_inside_the_frame(CameraCorner corner)
    {
        OverlayLayout l = OverlayLayout.For(1920, 1080, CameraOverlayShape.Circle, CameraOverlaySize.Medium, corner, mirror: false);
        int margin = OverlayLayout.Margin(1920, 1080);
        Assert.Equal(l.Width, l.Height); // circle: square box
        Assert.Equal((int)Math.Round(1080 * 0.30), l.Height);
        bool left = corner is CameraCorner.TopLeft or CameraCorner.BottomLeft;
        bool top = corner is CameraCorner.TopLeft or CameraCorner.TopRight;
        Assert.Equal(left ? margin : 1920 - margin - l.Width, l.X);
        Assert.Equal(top ? margin : 1080 - margin - l.Height, l.Y);
    }

    [Fact]
    public void Size_presets_grow_and_rounded_rectangle_is_16_by_9()
    {
        int s = OverlayLayout.For(1280, 720, CameraOverlayShape.Circle, CameraOverlaySize.Small, CameraCorner.BottomRight, false).Width;
        int m = OverlayLayout.For(1280, 720, CameraOverlayShape.Circle, CameraOverlaySize.Medium, CameraCorner.BottomRight, false).Width;
        int l = OverlayLayout.For(1280, 720, CameraOverlayShape.Circle, CameraOverlaySize.Large, CameraCorner.BottomRight, false).Width;
        Assert.True(s < m && m < l);
        OverlayLayout r = OverlayLayout.For(1280, 720, CameraOverlayShape.RoundedRectangle, CameraOverlaySize.Medium, CameraCorner.BottomRight, false);
        Assert.Equal(Math.Round(16 / 9.0, 2), Math.Round(r.Width / (double)r.Height, 2));
    }

    [Fact]
    public void Layout_fits_tiny_regions()
    {
        OverlayLayout l = OverlayLayout.For(64, 40, CameraOverlayShape.RoundedRectangle, CameraOverlaySize.Large, CameraCorner.TopLeft, false);
        Assert.True(l.X >= 0 && l.Y >= 0 && l.X + l.Width <= 64 && l.Y + l.Height <= 40);
    }

    [Fact]
    public void Circle_mask_is_opaque_inside_clear_outside_and_antialiased_at_the_edge()
    {
        var compositor = new CameraCompositor();
        OverlayLayout layout = new(0, 0, 100, 100, CameraOverlayShape.Circle, false);
        uint[] frame = Solid(100, 100, Screen);
        uint[] cam = Solid(64, 64, SyntheticCameraSource.Background);
        Draw(compositor, frame, 100, 100, cam, 64, 64, layout);

        Assert.Equal(255, compositor.MaskAt(50, 50));
        Assert.Equal(0, compositor.MaskAt(0, 0));
        Assert.Equal(0, compositor.MaskAt(99, 99));
        // The edge row through the centre has partial coverage somewhere (antialiasing), not only 0/255.
        bool partial = Enumerable.Range(0, 100).Any(x => compositor.MaskAt(x, 50) is > 0 and < 255)
            || Enumerable.Range(0, 100).Any(y => compositor.MaskAt(50, y) is > 0 and < 255)
            || Enumerable.Range(0, 100).Any(i => compositor.MaskAt(i, i) is > 0 and < 255);
        Assert.True(partial);
        Assert.Equal(SyntheticCameraSource.Background, frame[(50 * 100) + 50]);
        Assert.Equal(Screen, frame[0]); // corner of the box: outside the circle, screen untouched
    }

    [Fact]
    public void Rounded_rectangle_keeps_edge_centres_and_cuts_corners()
    {
        var compositor = new CameraCompositor();
        OverlayLayout layout = new(0, 0, 160, 90, CameraOverlayShape.RoundedRectangle, false);
        uint[] frame = Solid(160, 90, Screen);
        Draw(compositor, frame, 160, 90, Solid(32, 18, SyntheticCameraSource.Background), 32, 18, layout);
        Assert.Equal(255, compositor.MaskAt(80, 1));
        Assert.Equal(255, compositor.MaskAt(1, 45));
        Assert.Equal(0, compositor.MaskAt(0, 0));
        Assert.Equal(Screen, frame[0]);
    }

    [Fact]
    public void Mirror_flips_the_camera_horizontally()
    {
        // Camera: left half red, right half blue.
        uint[] cam = new uint[64 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                cam[(y * 64) + x] = x < 32 ? 0xFFFF0000u : 0xFF0000FFu;
            }
        }

        uint[] plain = Solid(64, 32, Screen), mirrored = Solid(64, 32, Screen);
        Draw(new CameraCompositor(), plain, 64, 32, cam, 64, 32, new OverlayLayout(0, 0, 64, 32, CameraOverlayShape.RoundedRectangle, false));
        Draw(new CameraCompositor(), mirrored, 64, 32, cam, 64, 32, new OverlayLayout(0, 0, 64, 32, CameraOverlayShape.RoundedRectangle, true));
        Assert.Equal(0xFFFF0000u, plain[(16 * 64) + 8]);
        Assert.Equal(0xFF0000FFu, plain[(16 * 64) + 56]);
        Assert.Equal(0xFF0000FFu, mirrored[(16 * 64) + 8]);
        Assert.Equal(0xFFFF0000u, mirrored[(16 * 64) + 56]);
    }

    [Fact]
    public void Wide_camera_is_centre_cropped_into_a_square_box_without_distortion()
    {
        // 16:9 camera: outer quarter columns red, middle blue. A square box shows the middle (crop), so the
        // whole circle is blue — letterboxing or stretching would show red.
        uint[] cam = new uint[160 * 90];
        for (int y = 0; y < 90; y++)
        {
            for (int x = 0; x < 160; x++)
            {
                cam[(y * 160) + x] = x < 30 || x >= 130 ? 0xFFFF0000u : 0xFF0000FFu;
            }
        }

        uint[] frame = Solid(90, 90, Screen);
        Draw(new CameraCompositor(), frame, 90, 90, cam, 160, 90, new OverlayLayout(0, 0, 90, 90, CameraOverlayShape.Circle, false));
        Assert.Equal(0xFF0000FFu, frame[(45 * 90) + 3]);
        Assert.Equal(0xFF0000FFu, frame[(45 * 90) + 86]);
    }

    [Fact]
    public void Composite_source_draws_synthetic_camera_in_the_corner_only_and_survives_unplug()
    {
        using var camera = new SyntheticCameraSource(320, 180, 30);
        camera.Start();
        var screen = new SolidFrameSource(640, 360, Screen);
        using var composite = new CompositeFrameSource(screen, camera, CameraOverlayShape.Circle, CameraOverlaySize.Medium, CameraCorner.BottomRight, mirror: false);
        composite.Start();
        uint[] frame = new uint[640 * 360];
        fixed (uint* p = frame)
        {
            Assert.Equal(FramePoll.NewFrame, composite.Poll((byte*)p, 640 * 4, out long t1));
            Assert.True(t1 > 0);
        }

        OverlayLayout l = composite.Layout;
        Assert.True(l.X > 320 && l.Y > 180);
        Assert.True(IsSynthetic(frame[((l.Y + (l.Height / 2)) * 640) + l.X + (l.Width / 2)]));
        Assert.Equal(Screen, frame[(20 * 640) + 20]);       // top-left: screen
        Assert.Equal(Screen, frame[(20 * 640) + 620]);      // top-right: screen
        Assert.Equal(Screen, frame[(340 * 640) + 20]);      // bottom-left: screen

        // Screen unchanged but the camera moved: a new frame is produced (overlay animates over a static screen).
        Thread.Sleep(80);
        fixed (uint* p = frame)
        {
            Assert.Equal(FramePoll.NewFrame, composite.Poll((byte*)p, 640 * 4, out _));
        }

        // Unplugged: the overlay disappears, the screen continues, nothing throws.
        camera.SimulateUnplug();
        Assert.NotNull(camera.LostReason);
        fixed (uint* p = frame)
        {
            Assert.Equal(FramePoll.NewFrame, composite.Poll((byte*)p, 640 * 4, out _)); // one refresh without the overlay
            Assert.Equal(FramePoll.NoChange, composite.Poll((byte*)p, 640 * 4, out _));
        }

        Assert.Equal(Screen, frame[((l.Y + (l.Height / 2)) * 640) + l.X + (l.Width / 2)]);
        Assert.Null(composite.EndReason); // the recording goes on
    }

    [Fact]
    public void Hidden_overlay_shows_the_plain_screen()
    {
        using var camera = new SyntheticCameraSource(160, 90, 30);
        camera.Start();
        using var composite = new CompositeFrameSource(new SolidFrameSource(320, 180, Screen), camera, CameraOverlayShape.Circle, CameraOverlaySize.Large, CameraCorner.TopLeft, false);
        composite.OverlayVisible = false;
        uint[] frame = new uint[320 * 180];
        fixed (uint* p = frame)
        {
            Assert.Equal(FramePoll.NewFrame, composite.Poll((byte*)p, 320 * 4, out _));
        }

        Assert.All(frame, px => Assert.Equal(Screen, px));
    }

    [Fact]
    public void Camera_only_source_has_the_camera_size_and_ends_when_unplugged()
    {
        using var camera = new SyntheticCameraSource(1280, 720, 30);
        camera.Start();
        using var source = new CameraFrameSource(camera, mirror: false);
        Assert.Equal(1280, source.Width);
        Assert.Equal(720, source.Height);
        uint[] frame = new uint[1280 * 720];
        fixed (uint* p = frame)
        {
            Assert.Equal(FramePoll.NewFrame, source.Poll((byte*)p, 1280 * 4, out long t));
            Assert.True(t > 0);
        }

        Assert.True(IsSynthetic(frame[(360 * 1280) + 640]));
        camera.SimulateUnplug();
        fixed (uint* p = frame)
        {
            Assert.Equal(FramePoll.Ended, source.Poll((byte*)p, 1280 * 4, out _));
        }

        Assert.NotNull(source.EndReason);
    }

    [Fact]
    public void Odd_camera_size_is_rounded_down_to_even()
    {
        using var camera = new SyntheticCameraSource(641, 361, 30);
        camera.Start();
        using var source = new CameraFrameSource(camera, mirror: true);
        Assert.Equal(640, source.Width);
        Assert.Equal(360, source.Height);
    }

    [Fact]
    public void Synthetic_camera_releases_on_stop_and_restarts()
    {
        using var camera = new SyntheticCameraSource(64, 64, 30);
        camera.Start();
        Assert.True(camera.IsRunning);
        camera.Stop();
        Assert.False(camera.IsRunning);
        camera.Start();
        Assert.True(camera.IsRunning);
        Assert.Equal(2, camera.StartCount);
        camera.FailNextStart = CameraFailure.AccessDenied;
        camera.Stop();
        CameraException e = Assert.Throws<CameraException>(camera.Start);
        Assert.Equal(CameraFailure.AccessDenied, e.Kind);
        Assert.Equal(PrivacySettings.CameraUri, e.SettingsUri);
        Assert.Contains("Privacy & security", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Frame_ring_gives_the_newest_frame_not_newer_than_the_time()
    {
        var ring = new CameraFrameRing();
        foreach (long t in new long[] { 100, 200, 300 })
        {
            byte[] b = ring.Rent(2, 2);
            b[0] = (byte)(t / 100);
            ring.Commit(t);
        }

        long Pick(long notAfter)
        {
            long got = -1;
            ring.WithFrame(notAfter, (_, _, _, time) => got = time);
            return got;
        }

        Assert.Equal(200, Pick(250));
        Assert.Equal(300, Pick(long.MaxValue));
        Assert.Equal(100, Pick(50)); // all newer: the oldest one kept
        Assert.Equal(3, ring.Sequence);
        ring.Clear();
        Assert.False(ring.WithFrame(long.MaxValue, (_, _, _, _) => { }));
    }

    [Fact]
    public void Format_chooser_prefers_720p30_then_closer_sizes_and_uncompressed()
    {
        Guid nv12 = Video.Interop.Mf.VideoFormat_NV12, mjpg = Video.Interop.Mf.VideoFormat_MJPG;
        CameraFormat[] formats =
        [
            new(640, 480, 30, nv12, 0),
            new(1280, 720, 10, nv12, 1),
            new(1280, 720, 30, mjpg, 2),
            new(1280, 720, 30, nv12, 3),
            new(1920, 1080, 30, mjpg, 4),
            new(3840, 2160, 30, mjpg, 5),
        ];
        Assert.Equal(3, CameraFormatChooser.Choose(formats)!.Value.Index);
        Assert.Equal(2, CameraFormatChooser.Choose(formats.Where(f => f.Index != 3).ToList())!.Value.Index);
        Assert.Equal(4, CameraFormatChooser.Choose([formats[0], formats[1], formats[4], formats[5]])!.Value.Index); // 1080p30 beats 720p10 and 480p
        Assert.Null(CameraFormatChooser.Choose([]));
    }

    [Fact]
    public void Nv12_and_yuy2_convert_to_bgra()
    {
        // 2×2 NV12: Y=235 (white), U=V=128.
        byte[] nv12 = [235, 235, 235, 235, 128, 128];
        byte[] bgra = new byte[16];
        fixed (byte* p = nv12)
        {
            CameraPixels.Nv12ToBgra(p, 2, p + 4, 2, 2, 2, bgra);
        }

        Assert.All(bgra, b => Assert.True(b >= 250));

        // YUY2 black (Y=16) and saturated red (Y=81, U=90, V=240).
        byte[] yuy2 = [16, 128, 16, 128, 81, 90, 81, 240];
        byte[] out2 = new byte[16];
        fixed (byte* p = yuy2)
        {
            CameraPixels.Yuy2ToBgra(p, 4, 2, 2, out2);
        }

        Assert.True(out2[0] < 5 && out2[1] < 5 && out2[2] < 5);
        Assert.True(out2[10] > 240 && out2[9] < 20 && out2[8] < 20); // row 1: red
    }

    [Fact]
    public void Studio_settings_default_to_screen_only_with_camera_and_microphone_off()
    {
        var v = new VideoSettings();
        Assert.Equal(StudioMode.Screen, v.StudioMode);
        Assert.False(v.RecordMicrophone);
        Assert.Null(v.CameraDeviceId);
        VideoSettings odd = (v with { StudioMode = (StudioMode)42, CameraCorner = (CameraCorner)9, CameraShape = (CameraOverlayShape)7, CameraSize = (CameraOverlaySize)5 }).Normalized();
        Assert.Equal(StudioMode.Screen, odd.StudioMode);
        Assert.Equal(CameraCorner.BottomRight, odd.CameraCorner);
        Assert.Equal(CameraOverlayShape.Circle, odd.CameraShape);
        Assert.Equal(CameraOverlaySize.Medium, odd.CameraSize);
    }

    [Fact]
    public void Camera_errors_are_explained()
    {
        Assert.Equal(CameraFailure.AccessDenied, CameraException.FromHResult(CameraException.E_ACCESSDENIED, "Cam").Kind);
        Assert.Equal(CameraFailure.Busy, CameraException.FromHResult(CameraException.MF_E_HW_MFT_FAILED_START_STREAMING, "Cam").Kind);
        Assert.Equal(CameraFailure.Disconnected, CameraException.FromHResult(CameraException.MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED, "Cam").Kind);
        Assert.Contains("another app", CameraException.FromHResult(CameraException.ERROR_BUSY, "Cam").Message, StringComparison.Ordinal);
    }

    private static uint[] Solid(int w, int h, uint color) => Enumerable.Repeat(color, w * h).ToArray();

    /// <summary>Both synthetic camera colours (magenta, yellow, and blends at the bar edge) have full red; the test screen has none.</summary>
    private static bool IsSynthetic(uint bgra) => ((bgra >> 16) & 0xFF) >= 200;

    private static void Draw(CameraCompositor c, uint[] frame, int w, int h, uint[] cam, int cw, int ch, OverlayLayout layout)
    {
        fixed (uint* f = frame)
        {
            c.Draw((byte*)f, w * 4, w, h, System.Runtime.InteropServices.MemoryMarshal.AsBytes(cam.AsSpan()), cw, ch, layout);
        }
    }

    /// <summary>A screen that never changes after its first frame.</summary>
    private sealed class SolidFrameSource(int width, int height, uint color) : IFrameSource
    {
        private bool _sent;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public string? EndReason => null;

        public string Description => "solid test screen";

        public void Start()
        {
        }

        public FramePoll Poll(byte* destination, int stride, out long sourceTime100ns)
        {
            sourceTime100ns = Qpc.Now100ns();
            if (_sent)
            {
                return FramePoll.NoChange;
            }

            _sent = true;
            for (int y = 0; y < Height; y++)
            {
                new Span<uint>(destination + ((long)y * stride), Width).Fill(color);
            }

            return FramePoll.NewFrame;
        }

        public void Dispose()
        {
        }
    }
}
