using System.Runtime.InteropServices;

namespace SnapForge.App.Video.Interop;

/// <summary>
/// Windows.Graphics.Capture through raw WinRT ABI calls (no projection assembly). Interface IDs and vtable slots were
/// read from the Windows SDK metadata (Microsoft.Windows.SDK.NET 10.0.22621) — see docs/VIDEO-ARCHITECTURE.md.
/// The capture session keeps Windows' own capture indicator (yellow border) — SnapForge never turns it off.
/// </summary>
internal static unsafe class GraphicsCapture
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IGraphicsCaptureSessionStatics = new("2224A540-5974-49AA-B232-0882536F4CB5");
    private static readonly Guid IID_IGraphicsCaptureSession2 = new("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");
    private static readonly Guid IID_IDirect3D11CaptureFramePoolStatics2 = new("589B103F-6BBC-5DF5-A991-02E28B3B66D5");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IID_IClosable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");

    // WinRT methods start after IInspectable (IUnknown 0-2, GetIids 3, GetRuntimeClassName 4, GetTrustLevel 5).
    private const int ItemGetSize = 7;
    private const int SessionStaticsIsSupported = 6;
    private const int Session2SetCursorCapture = 7;
    private const int SessionStartCapture = 6;
    private const int PoolStatics2CreateFreeThreaded = 6;
    private const int PoolRecreate = 6, PoolTryGetNextFrame = 7, PoolCreateCaptureSession = 10;
    private const int FrameGetSurface = 6, FrameGetSystemRelativeTime = 7, FrameGetContentSize = 8;
    private const int ClosableClose = 6;
    private const int InteropCreateForWindow = 3, InteropCreateForMonitor = 4;
    private const int DxgiAccessGetInterface = 3;
    private const int PixelFormatBgra8 = 87;

    [StructLayout(LayoutKind.Sequential)]
    public struct SizeInt32
    {
        public int Width;
        public int Height;
    }

    /// <summary>True if Windows.Graphics.Capture is available on this PC.</summary>
    public static bool IsSupported()
    {
        try
        {
            nint statics = WinRt.GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureSession", IID_IGraphicsCaptureSessionStatics);
            byte supported;
            int hr = ((delegate* unmanaged[Stdcall]<nint, byte*, int>)Com.Slot(statics, SessionStaticsIsSupported))(statics, &supported);
            Com.Release(ref statics);
            return hr >= 0 && supported != 0;
        }
        catch (NativeCallException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>A capture item (IGraphicsCaptureItem*) for a monitor (HMONITOR) or window (HWND).</summary>
    public static nint CreateItem(nint handle, bool window)
    {
        nint interop = WinRt.GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", IID_IGraphicsCaptureItemInterop);
        try
        {
            Guid iid = IID_IGraphicsCaptureItem;
            nint item;
            int slot = window ? InteropCreateForWindow : InteropCreateForMonitor;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Com.Slot(interop, slot))(interop, handle, &iid, &item), window ? "CreateForWindow" : "CreateForMonitor");
            return item;
        }
        finally
        {
            Com.Release(ref interop);
        }
    }

    public static SizeInt32 ItemSize(nint item)
    {
        SizeInt32 size;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)Com.Slot(item, ItemGetSize))(item, &size), "IGraphicsCaptureItem.Size");
        return size;
    }

    /// <summary>A free-threaded frame pool (frames are polled on the recording thread; no dispatcher needed).</summary>
    public static nint CreateFramePool(nint winRtDevice, SizeInt32 size, int buffers)
    {
        nint statics = WinRt.GetActivationFactory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", IID_IDirect3D11CaptureFramePoolStatics2);
        try
        {
            nint pool;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, SizeInt32, nint*, int>)Com.Slot(statics, PoolStatics2CreateFreeThreaded))(statics, winRtDevice, PixelFormatBgra8, buffers, size, &pool), "CreateFreeThreaded");
            return pool;
        }
        finally
        {
            Com.Release(ref statics);
        }
    }

    public static void RecreatePool(nint pool, nint winRtDevice, SizeInt32 size, int buffers) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, SizeInt32, int>)Com.Slot(pool, PoolRecreate))(pool, winRtDevice, PixelFormatBgra8, buffers, size), "FramePool.Recreate");

    public static nint CreateSession(nint pool, nint item)
    {
        nint session;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(pool, PoolCreateCaptureSession))(pool, item, &session), "CreateCaptureSession");
        return session;
    }

    /// <summary>Turns the mouse pointer in the capture on or off (Windows 10 2004+); returns false if unsupported.</summary>
    public static bool SetCursorCapture(nint session, bool enabled)
    {
        nint s2 = Com.TryQueryInterface(session, IID_IGraphicsCaptureSession2);
        if (s2 == 0)
        {
            return false;
        }

        int hr = ((delegate* unmanaged[Stdcall]<nint, byte, int>)Com.Slot(s2, Session2SetCursorCapture))(s2, enabled ? (byte)1 : (byte)0);
        Com.Release(ref s2);
        return hr >= 0;
    }

    public static void StartCapture(nint session) => Com.Check(Com.Call(session, SessionStartCapture), "StartCapture");

    /// <summary>Next frame, or 0 if none is ready yet.</summary>
    public static nint TryGetNextFrame(nint pool)
    {
        nint frame;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(pool, PoolTryGetNextFrame))(pool, &frame), "TryGetNextFrame");
        return frame;
    }

    /// <summary>The frame's ID3D11Texture2D (caller releases) and its QPC-based capture time in 100-ns units.</summary>
    public static (nint Texture, long SystemRelativeTime, SizeInt32 ContentSize) FrameTexture(nint frame)
    {
        nint surface;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(frame, FrameGetSurface))(frame, &surface), "Frame.Surface");
        long time;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Com.Slot(frame, FrameGetSystemRelativeTime))(frame, &time), "Frame.SystemRelativeTime");
        SizeInt32 size;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)Com.Slot(frame, FrameGetContentSize))(frame, &size), "Frame.ContentSize");
        nint access = Com.QueryInterface(surface, IID_IDirect3DDxgiInterfaceAccess);
        Com.Release(ref surface);
        try
        {
            Guid iid = D3D11.IID_ID3D11Texture2D;
            nint texture;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Com.Slot(access, DxgiAccessGetInterface))(access, &iid, &texture), "GetInterface(ID3D11Texture2D)");
            return (texture, time, size);
        }
        finally
        {
            Com.Release(ref access);
        }
    }

    /// <summary>Closes a WinRT object (frame, pool or session) through IClosable, then releases it.</summary>
    public static void Close(ref nint obj)
    {
        if (obj == 0)
        {
            return;
        }

        nint closable = Com.TryQueryInterface(obj, IID_IClosable);
        if (closable != 0)
        {
            _ = Com.Call(closable, ClosableClose);
            Com.Release(ref closable);
        }

        Com.Release(ref obj);
    }
}
