using System.Runtime.InteropServices;

namespace SnapForge.App.Video.Interop;

/// <summary>
/// Windows Core Audio (WASAPI): endpoint enumeration, loopback capture of system sound and microphone capture.
/// Shared mode with AUTOCONVERTPCM so Windows delivers 48 kHz stereo 32-bit float whatever the device format.
/// </summary>
internal static unsafe class CoreAudio
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
    private static readonly Guid IID_IAudioClient = new("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2");
    private static readonly Guid IID_IAudioCaptureClient = new("c8adbd64-e71e-48a0-a4de-185c395cd317");
    private static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PKEY_Device_FriendlyName_Fmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0");

    public const int eRender = 0, eCapture = 1, eConsole = 0;
    public const uint DEVICE_STATE_ACTIVE = 1;
    public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    public const uint AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY = 0x1;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;

    // vtable slots
    private const int EnumEnumAudioEndpoints = 3, EnumGetDefaultAudioEndpoint = 4, EnumGetDevice = 5;
    private const int CollectionGetCount = 3, CollectionItem = 4;
    private const int DeviceActivate = 3, DeviceOpenPropertyStore = 4, DeviceGetId = 5;
    private const int StoreGetValue = 5;
    private const int ClientInitialize = 3, ClientStart = 10, ClientStop = 11, ClientGetService = 14;
    private const int CaptureGetBuffer = 3, CaptureReleaseBuffer = 4, CaptureGetNextPacketSize = 5;

    public const int SampleRate = 48000;
    public const int Channels = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatExtensible
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
        public ushort ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref Mf.PropVariant pv);

    public sealed record Endpoint(string Id, string Name);

    public static nint CreateEnumerator()
    {
        Com.Check(Com.CoCreateInstance(CLSID_MMDeviceEnumerator, 0, Com.CLSCTX_ALL, IID_IMMDeviceEnumerator, out nint e), "CoCreateInstance(MMDeviceEnumerator)");
        return e;
    }

    /// <summary>Active endpoints of a data-flow direction (eRender or eCapture) with friendly names.</summary>
    public static IReadOnlyList<Endpoint> ListEndpoints(int dataFlow)
    {
        nint enumerator = CreateEnumerator();
        nint collection = 0;
        try
        {
            Com.Check(Com.Call(enumerator, EnumEnumAudioEndpoints, dataFlow, (int)DEVICE_STATE_ACTIVE, (nint)(&collection)), "EnumAudioEndpoints");
            uint count;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(collection, CollectionGetCount))(collection, &count), "GetCount");
            var list = new List<Endpoint>();
            for (uint i = 0; i < count; i++)
            {
                nint device;
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(collection, CollectionItem))(collection, i, &device) >= 0)
                {
                    list.Add(new Endpoint(DeviceId(device), FriendlyName(device)));
                    Com.Release(ref device);
                }
            }

            return list;
        }
        finally
        {
            Com.Release(ref collection);
            Com.Release(ref enumerator);
        }
    }

    /// <summary>The default endpoint (IMMDevice*), or 0 if there is none (e.g. no audio hardware).</summary>
    public static nint DefaultEndpoint(nint enumerator, int dataFlow)
    {
        nint device;
        return Com.Call(enumerator, EnumGetDefaultAudioEndpoint, dataFlow, eConsole, (nint)(&device)) >= 0 ? device : 0;
    }

    /// <summary>A specific endpoint by id (IMMDevice*), or 0 if it is not present.</summary>
    public static nint EndpointById(nint enumerator, string id)
    {
        nint device;
        fixed (char* p = id)
        {
            return Com.Call(enumerator, EnumGetDevice, (nint)p, (nint)(&device)) >= 0 ? device : 0;
        }
    }

    public static string DeviceId(nint device)
    {
        nint p;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(device, DeviceGetId))(device, &p) < 0)
        {
            return string.Empty;
        }

        string id = Marshal.PtrToStringUni(p) ?? string.Empty;
        Com.CoTaskMemFree(p);
        return id;
    }

    public static string FriendlyName(nint device)
    {
        nint store;
        if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(device, DeviceOpenPropertyStore))(device, 0 /* STGM_READ */, &store) < 0)
        {
            return "Audio device";
        }

        try
        {
            var key = new PropertyKey { FormatId = PKEY_Device_FriendlyName_Fmtid, PropertyId = 14 };
            Mf.PropVariant pv = default;
            if (((delegate* unmanaged[Stdcall]<nint, PropertyKey*, Mf.PropVariant*, int>)Com.Slot(store, StoreGetValue))(store, &key, &pv) < 0)
            {
                return "Audio device";
            }

            string name = pv.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(pv.Pointer) ?? "Audio device" : "Audio device";
            _ = PropVariantClear(ref pv);
            return name;
        }
        finally
        {
            Com.Release(ref store);
        }
    }

    /// <summary>
    /// Opens a shared-mode capture stream delivering 48 kHz stereo float. <paramref name="loopback"/> captures what a
    /// render device plays (system sound). Returns (IAudioClient*, IAudioCaptureClient*).
    /// </summary>
    public static (nint Client, nint Capture) OpenCapture(nint device, bool loopback)
    {
        Guid iid = IID_IAudioClient;
        nint client;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Com.Slot(device, DeviceActivate))(device, &iid, Com.CLSCTX_ALL, 0, &client), "IMMDevice.Activate(IAudioClient)");
        var format = new WaveFormatExtensible
        {
            FormatTag = 0xFFFE,
            Channels = Channels,
            SamplesPerSec = SampleRate,
            BitsPerSample = 32,
            BlockAlign = Channels * 4,
            AvgBytesPerSec = SampleRate * Channels * 4,
            Size = 22,
            ValidBitsPerSample = 32,
            ChannelMask = 0x3,
            SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
        };
        uint flags = AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY | (loopback ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0);
        int hr = ((delegate* unmanaged[Stdcall]<nint, int, uint, long, long, WaveFormatExtensible*, nint, int>)Com.Slot(client, ClientInitialize))(client, 0, flags, 2_000_000 /* 200 ms */, 0, &format, 0);
        if (hr < 0)
        {
            Com.Release(ref client);
            Com.Check(hr, "IAudioClient.Initialize");
        }

        Guid captureIid = IID_IAudioCaptureClient;
        nint capture;
        hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Com.Slot(client, ClientGetService))(client, &captureIid, &capture);
        if (hr < 0)
        {
            Com.Release(ref client);
            Com.Check(hr, "IAudioClient.GetService(IAudioCaptureClient)");
        }

        return (client, capture);
    }

    public static void Start(nint client) => Com.Check(Com.Call(client, ClientStart), "IAudioClient.Start");

    public static void Stop(nint client) => _ = Com.Call(client, ClientStop);

    /// <summary>Frames in the next packet (0 = none); HRESULT failure (e.g. device removed) is returned in <paramref name="hr"/>.</summary>
    public static uint NextPacketSize(nint capture, out int hr)
    {
        uint frames;
        hr = ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(capture, CaptureGetNextPacketSize))(capture, &frames);
        return hr >= 0 ? frames : 0;
    }

    /// <summary>Gets the next packet: float stereo frames, flags and its QPC capture time in 100-ns units.</summary>
    public static int GetBuffer(nint capture, out float* data, out uint frames, out uint flags, out long qpc100ns)
    {
        byte* d;
        uint f, fl;
        ulong devicePos, qpc;
        int hr = ((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, ulong*, ulong*, int>)Com.Slot(capture, CaptureGetBuffer))(capture, &d, &f, &fl, &devicePos, &qpc);
        data = (float*)d;
        frames = f;
        flags = fl;
        qpc100ns = (long)qpc;
        return hr;
    }

    public static void ReleaseBuffer(nint capture, uint frames) =>
        _ = ((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(capture, CaptureReleaseBuffer))(capture, frames);
}
