using System.Runtime.InteropServices;

namespace SnapForge.App.Video.Interop;

/// <summary>Direct3D 11 / DXGI: device creation, staging textures and copies for screen recording.</summary>
internal static unsafe partial class D3D11
{
    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_ID3D10Multithread = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");

    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const uint D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800;
    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const int D3D_DRIVER_TYPE_WARP = 5;
    public const uint D3D11_SDK_VERSION = 7;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint D3D11_USAGE_DEFAULT = 0;
    public const uint D3D11_USAGE_STAGING = 3;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const uint D3D11_BIND_RENDER_TARGET = 0x20;
    public const uint D3D11_MAP_READ = 1;

    // vtable slots (ID3D11Device / ID3D11DeviceContext / ID3D11Texture2D / IDXGIDevice / IDXGIAdapter)
    private const int DeviceCreateTexture2D = 5, DeviceGetImmediateContext = 40;
    private const int ContextMap = 14, ContextUnmap = 15, ContextCopySubresourceRegion = 46, ContextCopyResource = 47, ContextFlush = 111;
    private const int Texture2DGetDesc = 10;
    private const int DxgiDeviceGetAdapter = 7, DxgiAdapterGetDesc = 8;
    private const int MultithreadSetMultithreadProtected = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Box
    {
        public uint Left;
        public uint Top;
        public uint Front;
        public uint Right;
        public uint Bottom;
        public uint Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MappedSubresource
    {
        public byte* Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint levels, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint inspectable);

    /// <summary>Creates a device that can feed Windows.Graphics.Capture and Media Foundation (hardware, else WARP).</summary>
    public static (nint Device, nint Context, bool Hardware) CreateDevice()
    {
        uint flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
        int hr = D3D11CreateDevice(0, D3D_DRIVER_TYPE_HARDWARE, 0, flags, 0, 0, D3D11_SDK_VERSION, out nint device, out _, out nint context);
        bool hardware = hr >= 0;
        if (!hardware)
        {
            hr = D3D11CreateDevice(0, D3D_DRIVER_TYPE_WARP, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, 0, 0, D3D11_SDK_VERSION, out device, out _, out context);
            Com.Check(hr, "D3D11CreateDevice (WARP)");
        }

        // The device is shared by the capture thread and Media Foundation: serialize access inside D3D.
        nint mt = Com.TryQueryInterface(device, IID_ID3D10Multithread);
        if (mt != 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(mt, MultithreadSetMultithreadProtected))(mt, 1);
            Com.Release(ref mt);
        }

        return (device, context, hardware);
    }

    /// <summary>WinRT IDirect3DDevice (as IInspectable*) wrapping a D3D11 device, for Windows.Graphics.Capture.</summary>
    public static nint CreateWinRtDevice(nint d3dDevice)
    {
        nint dxgi = Com.QueryInterface(d3dDevice, IID_IDXGIDevice);
        try
        {
            Com.Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out nint inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
            return inspectable;
        }
        finally
        {
            Com.Release(ref dxgi);
        }
    }

    /// <summary>Name of the graphics adapter behind a device (for reports).</summary>
    public static string AdapterName(nint d3dDevice)
    {
        nint dxgi = Com.TryQueryInterface(d3dDevice, IID_IDXGIDevice);
        if (dxgi == 0)
        {
            return "unknown";
        }

        try
        {
            nint adapter;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(dxgi, DxgiDeviceGetAdapter))(dxgi, &adapter) < 0)
            {
                return "unknown";
            }

            byte* desc = stackalloc byte[512];
            int hr = ((delegate* unmanaged[Stdcall]<nint, byte*, int>)Com.Slot(adapter, DxgiAdapterGetDesc))(adapter, desc);
            Com.Release(ref adapter);
            return hr >= 0 ? new string((char*)desc).TrimEnd('\0') : "unknown";
        }
        finally
        {
            Com.Release(ref dxgi);
        }
    }

    public static nint CreateTexture(nint device, uint width, uint height, bool staging)
    {
        var desc = new Texture2DDesc
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            Usage = staging ? D3D11_USAGE_STAGING : D3D11_USAGE_DEFAULT,
            BindFlags = staging ? 0 : D3D11_BIND_RENDER_TARGET,
            CPUAccessFlags = staging ? D3D11_CPU_ACCESS_READ : 0,
        };
        nint texture;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, Texture2DDesc*, nint, nint*, int>)Com.Slot(device, DeviceCreateTexture2D))(device, &desc, 0, &texture), "ID3D11Device.CreateTexture2D");
        return texture;
    }

    public static Texture2DDesc GetDesc(nint texture)
    {
        Texture2DDesc d;
        ((delegate* unmanaged[Stdcall]<nint, Texture2DDesc*, void>)Com.Slot(texture, Texture2DGetDesc))(texture, &d);
        return d;
    }

    /// <summary>Copies a rectangle of <paramref name="source"/> to (0,0) of <paramref name="destination"/>.</summary>
    public static void CopyRegion(nint context, nint destination, nint source, uint left, uint top, uint width, uint height)
    {
        var box = new Box { Left = left, Top = top, Right = left + width, Bottom = top + height, Front = 0, Back = 1 };
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, Box*, void>)Com.Slot(context, ContextCopySubresourceRegion))(context, destination, 0, 0, 0, 0, source, 0, &box);
    }

    public static void CopyResource(nint context, nint destination, nint source) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)Com.Slot(context, ContextCopyResource))(context, destination, source);

    public static MappedSubresource MapRead(nint context, nint texture)
    {
        MappedSubresource m;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, MappedSubresource*, int>)Com.Slot(context, ContextMap))(context, texture, 0, D3D11_MAP_READ, 0, &m), "ID3D11DeviceContext.Map");
        return m;
    }

    public static void Unmap(nint context, nint texture) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Com.Slot(context, ContextUnmap))(context, texture, 0);

    public static void Flush(nint context) =>
        ((delegate* unmanaged[Stdcall]<nint, void>)Com.Slot(context, ContextFlush))(context);
}
