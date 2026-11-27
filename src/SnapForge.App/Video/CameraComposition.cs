using SnapForge.Core.Settings;

namespace SnapForge.App.Video;

/// <summary>Where and how the camera overlay is drawn on a frame (pixels of the recorded frame).</summary>
internal readonly record struct OverlayLayout(int X, int Y, int Width, int Height, CameraOverlayShape Shape, bool Mirror)
{
    /// <summary>Share of the frame's shorter side used by each size preset.</summary>
    public static double Share(CameraOverlaySize size) => size switch
    {
        CameraOverlaySize.Small => 0.22,
        CameraOverlaySize.Large => 0.40,
        _ => 0.30,
    };

    /// <summary>Distance from the frame edges.</summary>
    public static int Margin(int frameWidth, int frameHeight) => Math.Max(8, (int)Math.Round(Math.Min(frameWidth, frameHeight) * 0.03));

    /// <summary>
    /// The overlay box: a square for the circle, 16:9 for the rounded rectangle (most webcams are 16:9, so little is
    /// cropped), placed in <paramref name="corner"/> with a margin, always inside the frame.
    /// </summary>
    public static OverlayLayout For(int frameWidth, int frameHeight, CameraOverlayShape shape, CameraOverlaySize size, CameraCorner corner, bool mirror)
    {
        int margin = Margin(frameWidth, frameHeight);
        int shorter = Math.Min(frameWidth, frameHeight);
        int h = Math.Max(16, (int)Math.Round(shorter * Share(size)));
        int w = shape == CameraOverlayShape.Circle ? h : (int)Math.Round(h * 16 / 9.0);
        // Keep it inside the frame (tiny regions): shrink proportionally.
        double fit = Math.Min(1, Math.Min((frameWidth - (2.0 * margin)) / w, (frameHeight - (2.0 * margin)) / h));
        if (fit < 1)
        {
            w = Math.Max(8, (int)(w * fit));
            h = Math.Max(8, (int)(h * fit));
        }

        w = Math.Min(w, frameWidth);
        h = Math.Min(h, frameHeight);
        int x = corner is CameraCorner.TopLeft or CameraCorner.BottomLeft ? margin : frameWidth - margin - w;
        int y = corner is CameraCorner.TopLeft or CameraCorner.TopRight ? margin : frameHeight - margin - h;
        return new OverlayLayout(Math.Clamp(x, 0, frameWidth - w), Math.Clamp(y, 0, frameHeight - h), w, h, shape, mirror);
    }

    /// <summary>Corner radius of the rounded rectangle.</summary>
    public double CornerRadius => Shape == CameraOverlayShape.Circle ? Width / 2.0 : Math.Round(Height * 0.16);
}

/// <summary>
/// CPU compositor for the camera overlay: crops the camera picture to the overlay's aspect ratio (centre crop, no
/// distortion), scales it bilinearly, optionally mirrors it, and blends it through an antialiased circle or
/// rounded-rectangle mask. The sampling maps and the mask are cached per (layout, camera size) — per frame it is
/// one bilinear sample and one blend per overlay pixel (≈ 0.1–0.2 Mpx at 1080p: well inside a 30 FPS budget).
/// </summary>
internal sealed unsafe class CameraCompositor
{
    private OverlayLayout _layout;
    private int _camWidth, _camHeight;
    private int[] _x0 = [], _x1 = [], _fx = [];
    private int[] _y0 = [], _y1 = [], _fy = [];
    private byte[] _mask = [];

    /// <summary>Coverage 0–255 of the mask at a box pixel (tests).</summary>
    internal byte MaskAt(int x, int y) => _mask[(y * _layout.Width) + x];

    /// <summary>Draws the camera frame into <paramref name="frame"/> (BGRA, top-down) at <paramref name="layout"/>.</summary>
    public void Draw(byte* frame, int frameStride, int frameWidth, int frameHeight, ReadOnlySpan<byte> camera, int cameraWidth, int cameraHeight, OverlayLayout layout)
    {
        if (cameraWidth <= 0 || cameraHeight <= 0 || camera.Length < cameraWidth * cameraHeight * 4 || layout.Width <= 0 || layout.Height <= 0)
        {
            return;
        }

        if (layout != _layout || cameraWidth != _camWidth || cameraHeight != _camHeight || _mask.Length == 0)
        {
            Prepare(layout, cameraWidth, cameraHeight);
        }

        int bw = layout.Width, bh = layout.Height;
        fixed (byte* cam = camera)
        fixed (byte* mask = _mask)
        {
            for (int by = 0; by < bh; by++)
            {
                int fy = by + layout.Y;
                if (fy < 0 || fy >= frameHeight)
                {
                    continue;
                }

                byte* row0 = cam + ((long)_y0[by] * cameraWidth * 4);
                byte* row1 = cam + ((long)_y1[by] * cameraWidth * 4);
                int wy = _fy[by];
                byte* dst = frame + ((long)fy * frameStride);
                byte* m = mask + ((long)by * bw);
                for (int bx = 0; bx < bw; bx++)
                {
                    int a = m[bx];
                    int fx = bx + layout.X;
                    if (a == 0 || fx < 0 || fx >= frameWidth)
                    {
                        continue;
                    }

                    int x0 = _x0[bx] * 4, x1 = _x1[bx] * 4, wx = _fx[bx];
                    byte* d = dst + (fx * 4);
                    for (int c = 0; c < 3; c++)
                    {
                        int top = (row0[x0 + c] * (256 - wx)) + (row0[x1 + c] * wx);
                        int bottom = (row1[x0 + c] * (256 - wx)) + (row1[x1 + c] * wx);
                        int v = ((top * (256 - wy)) + (bottom * wy) + 32768) >> 16;
                        d[c] = (byte)(((v * a) + (d[c] * (255 - a)) + 127) / 255);
                    }

                    d[3] = 255;
                }
            }
        }
    }

    private void Prepare(OverlayLayout layout, int camW, int camH)
    {
        _layout = layout;
        _camWidth = camW;
        _camHeight = camH;
        int bw = layout.Width, bh = layout.Height;

        // Centre crop of the camera picture to the box's aspect ratio.
        double boxAspect = bw / (double)bh, camAspect = camW / (double)camH;
        double srcW = camW, srcH = camH, srcX = 0, srcY = 0;
        if (camAspect > boxAspect)
        {
            srcW = camH * boxAspect;
            srcX = (camW - srcW) / 2;
        }
        else
        {
            srcH = camW / boxAspect;
            srcY = (camH - srcH) / 2;
        }

        (_x0, _x1, _fx) = Map(bw, srcX, srcW, camW, layout.Mirror);
        (_y0, _y1, _fy) = Map(bh, srcY, srcH, camH, mirror: false);

        _mask = new byte[bw * bh];
        double cx = bw / 2.0, cy = bh / 2.0, r = layout.CornerRadius;
        for (int y = 0; y < bh; y++)
        {
            for (int x = 0; x < bw; x++)
            {
                double px = x + 0.5 - cx, py = y + 0.5 - cy;
                double distance; // signed distance to the shape's edge (negative inside)
                if (layout.Shape == CameraOverlayShape.Circle)
                {
                    distance = Math.Sqrt((px * px) + (py * py)) - (Math.Min(bw, bh) / 2.0);
                }
                else
                {
                    double qx = Math.Abs(px) - (cx - r), qy = Math.Abs(py) - (cy - r);
                    double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                    distance = Math.Sqrt((ox * ox) + (oy * oy)) + Math.Min(Math.Max(qx, qy), 0) - r;
                }

                double coverage = Math.Clamp(0.5 - distance, 0, 1); // one-pixel antialiased edge
                _mask[(y * bw) + x] = (byte)Math.Round(coverage * 255);
            }
        }
    }

    private static (int[] I0, int[] I1, int[] F) Map(int n, double start, double length, int limit, bool mirror)
    {
        var i0 = new int[n];
        var i1 = new int[n];
        var f = new int[n];
        double scale = length / n;
        for (int i = 0; i < n; i++)
        {
            int k = mirror ? n - 1 - i : i;
            double s = start + ((k + 0.5) * scale) - 0.5;
            s = Math.Clamp(s, 0, limit - 1);
            int a = (int)Math.Floor(s);
            i0[i] = a;
            i1[i] = Math.Min(limit - 1, a + 1);
            f[i] = (int)Math.Round((s - a) * 256);
        }

        return (i0, i1, f);
    }
}

/// <summary>
/// Screen + camera: the screen source with the camera composited onto every frame, using the newest camera frame not
/// newer than the screen frame. A camera change alone (screen unchanged) also produces a new frame, so the overlay
/// moves even over a static screen. If the camera is lost the recording continues without the overlay.
/// </summary>
internal sealed unsafe class CompositeFrameSource : IFrameSource
{
    private readonly IFrameSource _screen;
    private readonly ICameraSource _camera;
    private readonly CameraCompositor _compositor = new();
    private readonly byte[] _last;
    private readonly CameraFrameAction _draw;
    private bool _haveScreen;
    private long _lastSequence = -1;
    private bool _lastShown;
    private byte* _target;
    private int _targetStride;
    private long _drawnTime;

    public CompositeFrameSource(IFrameSource screen, ICameraSource camera, CameraOverlayShape shape, CameraOverlaySize size, CameraCorner corner, bool mirror)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(camera);
        _screen = screen;
        _camera = camera;
        _last = new byte[screen.Width * screen.Height * 4];
        Layout = OverlayLayout.For(screen.Width, screen.Height, shape, size, corner, mirror);
        _draw = (bgra, w, h, t) =>
        {
            _compositor.Draw(_target, _targetStride, Width, Height, bgra, w, h, Layout);
            _drawnTime = t;
        };
    }

    public int Width => _screen.Width;

    public int Height => _screen.Height;

    public string? EndReason => _screen.EndReason;

    public string Description => $"{_screen.Description}, camera overlay {_camera.Name} ({Layout.Shape}, {Layout.Width}×{Layout.Height} at {Layout.X},{Layout.Y}{(Layout.Mirror ? ", mirrored" : string.Empty)})";

    public OverlayLayout Layout { get; }

    public ICameraSource Camera => _camera;

    /// <summary>Toolbar toggle: show or hide the camera while recording.</summary>
    public bool OverlayVisible { get; set; } = true;

    /// <summary>Frames that carried the overlay (diagnostics).</summary>
    public long FramesWithOverlay { get; private set; }

    public void Start() => _screen.Start();

    public FramePoll Poll(byte* destination, int stride, out long sourceTime100ns)
    {
        sourceTime100ns = 0;
        FramePoll poll;
        long screenTime;
        fixed (byte* last = _last)
        {
            poll = _screen.Poll(last, Width * 4, out screenTime);
        }

        if (poll == FramePoll.Ended)
        {
            return FramePoll.Ended;
        }

        if (poll == FramePoll.NewFrame)
        {
            _haveScreen = true;
        }

        if (!_haveScreen)
        {
            return FramePoll.NoChange;
        }

        bool show = OverlayVisible && _camera.IsRunning && _camera.LostReason is null;
        long sequence = _camera.FrameSequence;
        bool cameraChanged = show && sequence != _lastSequence;
        if (poll != FramePoll.NewFrame && !cameraChanged && show == _lastShown)
        {
            return FramePoll.NoChange;
        }

        int rowBytes = Width * 4;
        fixed (byte* last = _last)
        {
            for (int y = 0; y < Height; y++)
            {
                Buffer.MemoryCopy(last + ((long)y * rowBytes), destination + ((long)y * stride), rowBytes, rowBytes);
            }
        }

        long time = poll == FramePoll.NewFrame ? screenTime : 0;
        if (show)
        {
            _target = destination;
            _targetStride = stride;
            _drawnTime = 0;
            if (_camera.WithFrame(poll == FramePoll.NewFrame ? screenTime : long.MaxValue, _draw))
            {
                FramesWithOverlay++;
                if (poll != FramePoll.NewFrame)
                {
                    time = _drawnTime; // only the camera changed: the frame is as old as the camera picture
                }
            }

            _target = null;
        }

        _lastSequence = sequence;
        _lastShown = show;
        sourceTime100ns = time != 0 ? time : Qpc.Now100ns();
        return FramePoll.NewFrame;
    }

    public void Dispose()
    {
        _camera.Dispose();
        _screen.Dispose();
    }
}

/// <summary>Camera only: the camera picture itself is the video, at the camera's size (rounded down to even).</summary>
internal sealed unsafe class CameraFrameSource : IFrameSource
{
    private readonly ICameraSource _camera;
    private readonly bool _mirror;
    private readonly CameraFrameAction _copy;
    private long _lastSequence = -1;
    private byte* _target;
    private int _targetStride;
    private long _copiedTime;

    /// <param name="camera">A started camera (its size is known).</param>
    public CameraFrameSource(ICameraSource camera, bool mirror)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _camera = camera;
        _mirror = mirror;
        Width = Math.Max(16, camera.Width & ~1);
        Height = Math.Max(16, camera.Height & ~1);
        _copy = (bgra, w, h, t) =>
        {
            Copy(bgra, w, h);
            _copiedTime = t;
        };
    }

    public int Width { get; }

    public int Height { get; }

    public string? EndReason => _camera.LostReason;

    public string Description => $"camera {_camera.Name} {Width}×{Height}{(_mirror ? ", mirrored" : string.Empty)}";

    public ICameraSource Camera => _camera;

    public void Start()
    {
    }

    public FramePoll Poll(byte* destination, int stride, out long sourceTime100ns)
    {
        sourceTime100ns = 0;
        if (_camera.LostReason is not null)
        {
            return FramePoll.Ended;
        }

        long sequence = _camera.FrameSequence;
        if (sequence == _lastSequence)
        {
            return FramePoll.NoChange;
        }

        _target = destination;
        _targetStride = stride;
        bool got = _camera.WithFrame(long.MaxValue, _copy);
        _target = null;
        if (!got)
        {
            return FramePoll.NoChange;
        }

        _lastSequence = sequence;
        sourceTime100ns = _copiedTime;
        return FramePoll.NewFrame;
    }

    private void Copy(ReadOnlySpan<byte> bgra, int w, int h)
    {
        fixed (byte* src = bgra)
        {
            for (int y = 0; y < Height; y++)
            {
                uint* d = (uint*)(_target + ((long)y * _targetStride));
                if (y >= h)
                {
                    new Span<uint>(d, Width).Clear(); // a restarted camera with a smaller picture: black below
                    continue;
                }

                uint* s = (uint*)(src + ((long)y * w * 4));
                for (int x = 0; x < Width; x++)
                {
                    int sx = _mirror ? w - 1 - x : x;
                    d[x] = sx >= 0 && sx < w ? s[sx] : 0xFF000000;
                }
            }
        }
    }

    public void Dispose() => _camera.Dispose();
}
