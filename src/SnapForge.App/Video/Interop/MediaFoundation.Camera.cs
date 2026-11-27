using System.Runtime.InteropServices;

namespace SnapForge.App.Video.Interop;

/// <summary>
/// Media Foundation video capture (webcams): device enumeration, the device media source and a source reader on it.
/// <para>Vtable slots (IUnknown = 0–2), from the Windows SDK interface order (mfobjects.idl / mfidl.idl /
/// mfreadwrite.idl; checked against the Wine IDL copies in the scratch workspace, see docs/VIDEO-ARCHITECTURE.md):</para>
/// <list type="bullet">
/// <item>IMFAttributes 3–32 (GetItem … CopyAllItems; GetUINT32 = 7, GetUINT64 = 8, GetGUID = 10, GetAllocatedString = 13,
/// SetUINT32 = 21, SetGUID = 24 — shared with <see cref="Mf"/>).</item>
/// <item>IMFActivate : IMFAttributes — ActivateObject = 33, ShutdownObject = 34, DetachObject = 35.</item>
/// <item>IMFMediaEventGenerator — GetEvent = 3, BeginGetEvent = 4, EndGetEvent = 5, QueueEvent = 6;
/// IMFMediaSource : IMFMediaEventGenerator — GetCharacteristics = 7, CreatePresentationDescriptor = 8, Start = 9,
/// Stop = 10, Pause = 11, Shutdown = 12 (the <c>Remote…</c> <c>call_as</c> methods are not vtable entries).</item>
/// <item>IMFSourceReader — GetStreamSelection = 3, SetStreamSelection = 4, GetNativeMediaType = 5,
/// GetCurrentMediaType = 6, SetCurrentMediaType = 7, SetCurrentPosition = 8, ReadSample = 9, Flush = 10,
/// GetServiceForStream = 11, GetPresentationAttribute = 12.</item>
/// <item>IMFSample : IMFAttributes — GetSampleFlags = 33, SetSampleFlags = 34, GetSampleTime = 35, SetSampleTime = 36,
/// GetSampleDuration = 37, SetSampleDuration = 38, GetBufferCount = 39, GetBufferByIndex = 40,
/// ConvertToContiguousBuffer = 41, AddBuffer = 42.</item>
/// <item>IMF2DBuffer — Lock2D = 3, Unlock2D = 4, GetScanline0AndPitch = 5, IsContiguousFormat = 6,
/// GetContiguousLength = 7, ContiguousCopyTo = 8, ContiguousCopyFrom = 9.</item>
/// </list>
/// </summary>
internal static unsafe partial class Mf
{
    // ---- GUIDs (mfidl.idl, mfreadwrite.idl, mfapi.h)
    public static readonly Guid DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    public static readonly Guid DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    public static readonly Guid DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    public static readonly Guid DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
    public static readonly Guid SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    public static readonly Guid VideoFormat_YUY2 = new("32595559-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormat_MJPG = new("47504A4D-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormat_ARGB32 = new("00000015-0000-0010-8000-00AA00389B71");
    private static readonly Guid IID_IMFMediaSource = new("279a808d-aec7-40c8-9c6b-a6b492c78a66");
    private static readonly Guid IID_IMF2DBuffer = new("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");

    // ---- source reader flags (MF_SOURCE_READER_FLAG, mfreadwrite.idl)
    public const uint MF_SOURCE_READERF_ERROR = 0x1;
    public const uint MF_SOURCE_READERF_STREAMTICK = 0x100;

    // ---- vtable slots (see the class summary)
    private const int ActivateShutdownObject = 34;
    private const int MediaSourceShutdown = 12;
    private const int ReaderGetNativeMediaType = 5;
    private const int Buffer2DLock2D = 3, Buffer2DUnlock2D = 4;

    [LibraryImport("mf.dll")]
    private static partial int MFEnumDeviceSources(nint attributes, out nint activates, out uint count);

    [LibraryImport("mfreadwrite.dll")]
    private static partial int MFCreateSourceReaderFromMediaSource(nint mediaSource, nint attributes, out nint sourceReader);

    /// <summary>A video capture device as Windows lists it. Listing does not open the camera (its light stays off).</summary>
    public readonly record struct VideoDevice(string Name, string SymbolicLink);

    /// <summary>The video capture devices (MFEnumDeviceSources with MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = VIDCAP).</summary>
    public static IReadOnlyList<VideoDevice> EnumerateVideoDevices()
    {
        var list = new List<VideoDevice>();
        ForEachVideoDevice((activate, device) =>
        {
            list.Add(device);
            return false;
        });
        return list;
    }

    /// <summary>
    /// Activates the media source of a video capture device (the first device when <paramref name="symbolicLink"/>
    /// is null). Returns (media source, its activation object) — both must be released with
    /// <see cref="ShutdownVideoDevice"/>; null when no such device exists.
    /// </summary>
    public static (nint Source, nint Activate, VideoDevice Device)? ActivateVideoDevice(string? symbolicLink)
    {
        (nint, nint, VideoDevice)? found = null;
        ForEachVideoDevice((activate, device) =>
        {
            if (symbolicLink is not null && !string.Equals(device.SymbolicLink, symbolicLink, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Guid iid = IID_IMFMediaSource;
            nint source;
            // IMFActivate::ActivateObject(REFIID, void**) — slot 33.
            Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Com.Slot(activate, ActivateActivateObject))(activate, &iid, &source), "IMFActivate.ActivateObject(camera)");
            Marshal.AddRef(activate); // kept for ShutdownObject
            found = (source, activate, device);
            return true;
        });
        return found;
    }

    /// <summary>Shuts the camera down (releases the hardware: the camera light goes off) and releases both objects.</summary>
    public static void ShutdownVideoDevice(ref nint source, ref nint activate)
    {
        if (source != 0)
        {
            _ = Com.Call(source, MediaSourceShutdown); // IMFMediaSource::Shutdown — slot 12
            Com.Release(ref source);
        }

        if (activate != 0)
        {
            _ = Com.Call(activate, ActivateShutdownObject); // IMFActivate::ShutdownObject — slot 34
            Com.Release(ref activate);
        }
    }

    /// <summary>
    /// A source reader on a live media source, with the reader's video processing enabled so it converts the camera's
    /// native format (NV12, YUY2, MJPG…) to RGB32 when asked.
    /// </summary>
    public static nint CreateSourceReaderFromMediaSource(nint mediaSource, bool advancedProcessing)
    {
        nint attrs = CreateAttributes(2);
        try
        {
            SetUInt32(attrs, advancedProcessing ? SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING : SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            Com.Check(MFCreateSourceReaderFromMediaSource(mediaSource, attrs, out nint reader), "MFCreateSourceReaderFromMediaSource");
            return reader;
        }
        finally
        {
            Com.Release(ref attrs);
        }
    }

    /// <summary>The <paramref name="index"/>-th native media type of a reader stream, or 0 when there are no more.</summary>
    public static nint GetNativeMediaType(nint reader, int stream, int index)
    {
        nint type;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int>)Com.Slot(reader, ReaderGetNativeMediaType))(reader, (uint)stream, (uint)index, &type);
        return hr >= 0 ? type : 0;
    }

    /// <summary>Sets a reader stream's output type, returning the HRESULT (a camera may refuse a conversion).</summary>
    public static int TrySetCurrentMediaType(nint reader, int stream, nint mediaType) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Com.Slot(reader, ReaderSetCurrentMediaType))(reader, (uint)stream, 0, mediaType);

    /// <summary>ReadSample without throwing: returns the HRESULT, the stream flags, the timestamp and the sample (may be 0).</summary>
    public static int TryReadSample(nint reader, int stream, out uint flags, out long timestamp, out nint sample)
    {
        uint actual, f;
        long ts;
        nint s;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Com.Slot(reader, ReaderReadSample))(reader, (uint)stream, 0, &actual, &f, &ts, &s);
        flags = f;
        timestamp = ts;
        sample = hr >= 0 ? s : 0;
        return hr;
    }

    /// <summary>
    /// Locks a buffer for reading rows: through IMF2DBuffer when offered (actual pitch, negative = bottom-up), else as
    /// a contiguous buffer with <paramref name="fallbackPitch"/>. Returns the first row (scanline 0) and its pitch.
    /// </summary>
    public static byte* LockRows(nint buffer, int fallbackPitch, int height, out int pitch, out bool locked2D)
    {
        nint b2 = Com.TryQueryInterface(buffer, IID_IMF2DBuffer);
        if (b2 != 0)
        {
            byte* scan0;
            int p;
            int hr = ((delegate* unmanaged[Stdcall]<nint, byte**, int*, int>)Com.Slot(b2, Buffer2DLock2D))(b2, &scan0, &p);
            Com.Release(ref b2); // the buffer object keeps the interface alive; Unlock2D re-queries
            if (hr >= 0)
            {
                pitch = p;
                locked2D = true;
                return scan0;
            }
        }

        byte* data = LockBuffer(buffer, out _, out _);
        locked2D = false;
        pitch = fallbackPitch;
        return fallbackPitch < 0 ? data + ((long)(height - 1) * -fallbackPitch) : data; // bottom-up: scanline 0 is last in memory
    }

    public static void UnlockRows(nint buffer, bool locked2D)
    {
        if (!locked2D)
        {
            UnlockBuffer(buffer);
            return;
        }

        nint b2 = Com.TryQueryInterface(buffer, IID_IMF2DBuffer);
        if (b2 != 0)
        {
            _ = Com.Call(b2, Buffer2DUnlock2D);
            Com.Release(ref b2);
        }
    }

    /// <summary>Reads a signed 32-bit attribute (MF_MT_DEFAULT_STRIDE is stored as UINT32 holding a LONG).</summary>
    public static int? GetInt32(nint attrs, Guid key) => GetUInt32(attrs, key) is { } v ? unchecked((int)v) : null;

    /// <summary>Calls <paramref name="visit"/> for each video capture device until it returns true.</summary>
    private static void ForEachVideoDevice(Func<nint, VideoDevice, bool> visit)
    {
        nint attrs = CreateAttributes(1);
        nint array = 0;
        uint count = 0;
        try
        {
            SetGuid(attrs, DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
            Com.Check(MFEnumDeviceSources(attrs, out array, out count), "MFEnumDeviceSources");
            bool done = false;
            for (int i = 0; i < count; i++)
            {
                nint activate = ((nint*)array)[i];
                try
                {
                    if (!done)
                    {
                        var device = new VideoDevice(
                            GetString(activate, DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME) ?? "Camera",
                            GetString(activate, DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK) ?? string.Empty);
                        done = visit(activate, device);
                    }
                }
                finally
                {
                    Marshal.Release(activate);
                }
            }
        }
        finally
        {
            if (array != 0)
            {
                Com.CoTaskMemFree(array);
            }

            Com.Release(ref attrs);
        }
    }
}
