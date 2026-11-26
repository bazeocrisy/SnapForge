using System.Runtime.InteropServices;

namespace SnapForge.App.Video.Interop;

/// <summary>
/// Media Foundation (Windows' built-in media framework): sink writer for MP4 (H.264 + AAC) and source reader for
/// validation. GUIDs and vtable slots verified against the SDK interface definitions (docs/VIDEO-ARCHITECTURE.md).
/// </summary>
internal static unsafe partial class Mf
{
    public const uint MF_VERSION = 0x00020070;

    // ---- GUIDs
    public static readonly Guid MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static readonly Guid MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");
    public static readonly Guid MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid MediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormat_NV12 = new("3231564E-0000-0010-8000-00AA00389B71");
    public static readonly Guid AudioFormat_AAC = new("00001610-0000-0010-8000-00AA00389B71");
    public static readonly Guid AudioFormat_PCM = new("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid SINK_WRITER_D3D_MANAGER = new("ec822da2-e1e9-4b29-a0d8-563c719f5269");
    public static readonly Guid SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    public static readonly Guid TRANSCODE_CONTAINERTYPE = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
    public static readonly Guid TranscodeContainerType_FMPEG4 = new("9ba876f1-419f-4b77-a1e0-35959d9d4004");
    public static readonly Guid TranscodeContainerType_MPEG4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");
    public static readonly Guid LOW_LATENCY = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    public static readonly Guid MFT_CATEGORY_VIDEO_ENCODER = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    public static readonly Guid MFT_CATEGORY_AUDIO_ENCODER = new("91c64bd0-f91e-4d8c-9276-db248279d975");
    public static readonly Guid MFT_FRIENDLY_NAME = new("314ffbae-5b41-4c95-9c19-4e7d586face3");
    public static readonly Guid SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid MT_MAX_KEYFRAME_SPACING = new("c16eb52b-73a1-476f-8d62-839d6a020652");
    public static readonly Guid MFT_ENUM_HARDWARE_URL_Attribute = new("2fb866ac-b078-4942-ab6c-003d05cda674");
    private static readonly Guid IID_IMFSinkWriterEx = new("588d72ab-5bc1-496a-8714-b70617141b25");
    public static readonly Guid PD_DURATION = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    public const int MFVideoInterlace_Progressive = 2;
    public const int eAVEncH264VProfile_Main = 77;
    public const uint MFT_ENUM_FLAG_SYNCMFT = 0x1;
    public const uint MFT_ENUM_FLAG_ASYNCMFT = 0x2;
    public const uint MFT_ENUM_FLAG_HARDWARE = 0x4;
    public const uint MFT_ENUM_FLAG_SORTANDFILTER = 0x40;
    public const int MF_SOURCE_READER_FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC);
    public const int MF_SOURCE_READER_FIRST_AUDIO_STREAM = unchecked((int)0xFFFFFFFD);
    public const int MF_SOURCE_READER_ALL_STREAMS = unchecked((int)0xFFFFFFFE);
    public const int MF_SOURCE_READER_MEDIASOURCE = unchecked((int)0xFFFFFFFF);
    public const int MF_SOURCE_READERF_ENDOFSTREAM = 0x2;

    // ---- vtable slots
    private const int AttrGetUINT32 = 7, AttrGetUINT64 = 8, AttrGetGUID = 10, AttrGetAllocatedString = 13;
    private const int AttrSetUINT32 = 21, AttrSetUINT64 = 22, AttrSetGUID = 24, AttrSetUnknown = 27;
    private const int SampleSetSampleTime = 36, SampleSetSampleDuration = 38, SampleAddBuffer = 42, SampleConvertToContiguous = 41, SampleGetSampleTime = 35;
    private const int BufferLock = 3, BufferUnlock = 4, BufferGetCurrentLength = 5, BufferSetCurrentLength = 6;
    private const int SinkAddStream = 3, SinkSetInputMediaType = 4, SinkBeginWriting = 5, SinkWriteSample = 6, SinkSendStreamTick = 7, SinkFinalize = 11, SinkGetStatistics = 13, SinkExGetTransformForStream = 14;
    private const int TransformGetAttributes = 8;
    private const int ReaderSetStreamSelection = 4, ReaderGetCurrentMediaType = 6, ReaderSetCurrentMediaType = 7, ReaderReadSample = 9, ReaderGetPresentationAttribute = 12;
    private const int ActivateActivateObject = 33;
    private const int DxgiManagerResetDevice = 7;

    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateAttributes(out nint attributes, uint initialSize);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMediaType(out nint mediaType);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateSample(out nint sample);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMemoryBuffer(uint maxLength, out nint buffer);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateDXGIDeviceManager(out uint resetToken, out nint manager);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateDXGISurfaceBuffer(in Guid riid, nint surface, uint subresourceIndex, [MarshalAs(UnmanagedType.Bool)] bool bottomUp, out nint buffer);

    [LibraryImport("mfplat.dll")]
    private static partial int MFTEnumEx(Guid category, uint flags, nint inputType, nint outputType, out nint activates, out uint count);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateSinkWriterFromURL(string url, nint byteStream, nint attributes, out nint sinkWriter);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateSourceReaderFromURL(string url, nint attributes, out nint sourceReader);

    [StructLayout(LayoutKind.Sequential)]
    public struct RegisterTypeInfo
    {
        public Guid MajorType;
        public Guid SubType;
    }

    // ---- attributes

    public static nint CreateAttributes(uint size)
    {
        Com.Check(MFCreateAttributes(out nint a, size), "MFCreateAttributes");
        return a;
    }

    public static nint CreateMediaType()
    {
        Com.Check(MFCreateMediaType(out nint t), "MFCreateMediaType");
        return t;
    }

    public static void SetUInt32(nint attrs, Guid key, uint value) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)Com.Slot(attrs, AttrSetUINT32))(attrs, &key, value), "SetUINT32");

    public static void SetUInt64(nint attrs, Guid key, ulong value) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, ulong, int>)Com.Slot(attrs, AttrSetUINT64))(attrs, &key, value), "SetUINT64");

    public static void SetRatio(nint attrs, Guid key, uint high, uint low) => SetUInt64(attrs, key, ((ulong)high << 32) | low);

    public static void SetGuid(nint attrs, Guid key, Guid value) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Com.Slot(attrs, AttrSetGUID))(attrs, &key, &value), "SetGUID");

    public static void SetUnknown(nint attrs, Guid key, nint value) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint, int>)Com.Slot(attrs, AttrSetUnknown))(attrs, &key, value), "SetUnknown");

    public static uint? GetUInt32(nint attrs, Guid key)
    {
        uint v;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, uint*, int>)Com.Slot(attrs, AttrGetUINT32))(attrs, &key, &v) >= 0 ? v : null;
    }

    public static ulong? GetUInt64(nint attrs, Guid key)
    {
        ulong v;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, ulong*, int>)Com.Slot(attrs, AttrGetUINT64))(attrs, &key, &v) >= 0 ? v : null;
    }

    public static Guid? GetGuid(nint attrs, Guid key)
    {
        Guid v;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Com.Slot(attrs, AttrGetGUID))(attrs, &key, &v) >= 0 ? v : null;
    }

    public static string? GetString(nint attrs, Guid key)
    {
        nint p;
        uint len;
        if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, uint*, int>)Com.Slot(attrs, AttrGetAllocatedString))(attrs, &key, &p, &len) < 0)
        {
            return null;
        }

        string s = Marshal.PtrToStringUni(p, (int)len);
        Com.CoTaskMemFree(p);
        return s;
    }

    // ---- samples and buffers

    public static nint CreateSample()
    {
        Com.Check(MFCreateSample(out nint s), "MFCreateSample");
        return s;
    }

    public static nint CreateMemoryBuffer(int length)
    {
        Com.Check(MFCreateMemoryBuffer((uint)length, out nint b), "MFCreateMemoryBuffer");
        return b;
    }

    public static nint CreateDxgiSurfaceBuffer(nint texture)
    {
        Com.Check(MFCreateDXGISurfaceBuffer(D3D11.IID_ID3D11Texture2D, texture, 0, false, out nint b), "MFCreateDXGISurfaceBuffer");
        return b;
    }

    /// <summary>Locks a buffer and returns a pointer to its memory (call <see cref="UnlockBuffer"/>).</summary>
    public static byte* LockBuffer(nint buffer, out int maxLength, out int currentLength)
    {
        byte* p;
        uint max, cur;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Com.Slot(buffer, BufferLock))(buffer, &p, &max, &cur), "IMFMediaBuffer.Lock");
        maxLength = (int)max;
        currentLength = (int)cur;
        return p;
    }

    public static void UnlockBuffer(nint buffer) => Com.Check(Com.Call(buffer, BufferUnlock), "IMFMediaBuffer.Unlock");

    public static void SetBufferLength(nint buffer, int length) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(buffer, BufferSetCurrentLength))(buffer, (uint)length), "SetCurrentLength");

    public static int GetBufferLength(nint buffer)
    {
        uint len;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(buffer, BufferGetCurrentLength))(buffer, &len), "GetCurrentLength");
        return (int)len;
    }

    public static void AddBuffer(nint sample, nint buffer) => Com.Check(Com.Call(sample, SampleAddBuffer, buffer), "IMFSample.AddBuffer");

    public static void SetSampleTime(nint sample, long time100ns) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Com.Slot(sample, SampleSetSampleTime))(sample, time100ns), "SetSampleTime");

    public static void SetSampleDuration(nint sample, long duration100ns) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Com.Slot(sample, SampleSetSampleDuration))(sample, duration100ns), "SetSampleDuration");

    public static long GetSampleTime(nint sample)
    {
        long t;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Com.Slot(sample, SampleGetSampleTime))(sample, &t), "GetSampleTime");
        return t;
    }

    public static nint ConvertToContiguousBuffer(nint sample)
    {
        nint b;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(sample, SampleConvertToContiguous))(sample, &b), "ConvertToContiguousBuffer");
        return b;
    }

    // ---- sink writer

    public static nint CreateSinkWriter(string path, nint attributes)
    {
        Com.Check(MFCreateSinkWriterFromURL(path, 0, attributes, out nint w), "MFCreateSinkWriterFromURL");
        return w;
    }

    public static int AddStream(nint writer, nint mediaType)
    {
        uint index;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)Com.Slot(writer, SinkAddStream))(writer, mediaType, &index), "IMFSinkWriter.AddStream");
        return (int)index;
    }

    public static void SetInputMediaType(nint writer, int stream, nint mediaType) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Com.Slot(writer, SinkSetInputMediaType))(writer, (uint)stream, mediaType, 0), "IMFSinkWriter.SetInputMediaType");

    public static void BeginWriting(nint writer) => Com.Check(Com.Call(writer, SinkBeginWriting), "IMFSinkWriter.BeginWriting");

    public static void WriteSample(nint writer, int stream, nint sample) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Com.Slot(writer, SinkWriteSample))(writer, (uint)stream, sample), "IMFSinkWriter.WriteSample");

    /// <summary>Tells the writer there is a gap (no samples) in a stream until <paramref name="time100ns"/>.</summary>
    public static void SendStreamTick(nint writer, int stream, long time100ns) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, long, int>)Com.Slot(writer, SinkSendStreamTick))(writer, (uint)stream, time100ns), "IMFSinkWriter.SendStreamTick");

    public static void FinalizeWriter(nint writer) => Com.Check(Com.Call(writer, SinkFinalize), "IMFSinkWriter.Finalize");

    [StructLayout(LayoutKind.Sequential)]
    public struct SinkWriterStatistics
    {
        public uint Cb;
        public long LastTimestampReceived;
        public long LastTimestampEncoded;
        public long LastTimestampProcessed;
        public long LastStreamTickReceived;
        public long LastSinkSampleRequest;
        public ulong NumSamplesReceived;
        public ulong NumSamplesEncoded;
        public ulong NumSamplesProcessed;
        public ulong NumStreamTicksReceived;
        public uint ByteCountQueued;
        public ulong ByteCountProcessed;
        public uint NumOutstandingSinkSampleRequests;
        public uint AverageSampleRateReceived;
        public uint AverageSampleRateEncoded;
        public uint AverageSampleRateProcessed;
    }

    /// <summary>Per-stream counters (samples received vs. encoded = encoder backlog / latency).</summary>
    public static SinkWriterStatistics? GetStatistics(nint writer, int stream)
    {
        var stats = new SinkWriterStatistics { Cb = (uint)sizeof(SinkWriterStatistics) };
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, SinkWriterStatistics*, int>)Com.Slot(writer, SinkGetStatistics))(writer, (uint)stream, &stats);
        return hr >= 0 ? stats : null;
    }

    /// <summary>
    /// The encoder the sink writer actually chose for a stream: its friendly name and whether it is a hardware MFT
    /// (hardware MFTs carry MFT_ENUM_HARDWARE_URL_Attribute). Null if Windows does not expose it.
    /// </summary>
    public static (string Name, bool Hardware)? GetEncoder(nint writer, int stream)
    {
        nint ex = Com.TryQueryInterface(writer, IID_IMFSinkWriterEx);
        if (ex == 0)
        {
            return null;
        }

        try
        {
            for (uint i = 0; i < 8; i++)
            {
                Guid category;
                nint transform;
                if (((delegate* unmanaged[Stdcall]<nint, uint, uint, Guid*, nint*, int>)Com.Slot(ex, SinkExGetTransformForStream))(ex, (uint)stream, i, &category, &transform) < 0)
                {
                    break;
                }

                try
                {
                    if (category != MFT_CATEGORY_VIDEO_ENCODER && category != MFT_CATEGORY_AUDIO_ENCODER)
                    {
                        continue;
                    }

                    nint attrs;
                    if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(transform, TransformGetAttributes))(transform, &attrs) < 0 || attrs == 0)
                    {
                        return ("Encoder", false);
                    }

                    try
                    {
                        string name = GetString(attrs, MFT_FRIENDLY_NAME) ?? "Encoder";
                        bool hardware = GetString(attrs, MFT_ENUM_HARDWARE_URL_Attribute) is not null;
                        return (name, hardware);
                    }
                    finally
                    {
                        Com.Release(ref attrs);
                    }
                }
                finally
                {
                    Com.Release(ref transform);
                }
            }

            return null;
        }
        finally
        {
            Com.Release(ref ex);
        }
    }

    // ---- source reader (validation, thumbnails, tests)

    public static nint CreateSourceReader(string path, nint attributes)
    {
        Com.Check(MFCreateSourceReaderFromURL(path, attributes, out nint r), "MFCreateSourceReaderFromURL");
        return r;
    }

    public static void SetStreamSelection(nint reader, int stream, bool selected) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, int, int>)Com.Slot(reader, ReaderSetStreamSelection))(reader, (uint)stream, selected ? 1 : 0), "SetStreamSelection");

    public static void SetCurrentMediaType(nint reader, int stream, nint mediaType) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Com.Slot(reader, ReaderSetCurrentMediaType))(reader, (uint)stream, 0, mediaType), "SetCurrentMediaType");

    public static nint GetCurrentMediaType(nint reader, int stream)
    {
        nint t;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(reader, ReaderGetCurrentMediaType))(reader, (uint)stream, &t), "GetCurrentMediaType");
        return t;
    }

    /// <summary>Reads the next sample of a stream; returns false at the end. <paramref name="sample"/> may be 0 (gap).</summary>
    public static bool ReadSample(nint reader, int stream, out long timestamp, out nint sample)
    {
        uint actual, flags;
        long ts;
        nint s;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Com.Slot(reader, ReaderReadSample))(reader, (uint)stream, 0, &actual, &flags, &ts, &s), "IMFSourceReader.ReadSample");
        timestamp = ts;
        sample = s;
        return (flags & MF_SOURCE_READERF_ENDOFSTREAM) == 0 || s != 0;
    }

    /// <summary>Duration of the opened media in 100-ns units (from the presentation descriptor), or null.</summary>
    public static long? GetDuration(nint reader)
    {
        PropVariant pv = default;
        Guid key = PD_DURATION;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, Guid*, PropVariant*, int>)Com.Slot(reader, ReaderGetPresentationAttribute))(reader, unchecked((uint)MF_SOURCE_READER_MEDIASOURCE), &key, &pv);
        return hr >= 0 && pv.Type == 21 /* VT_UI8 */ ? (long)pv.Value : null;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public ulong Value;

        [FieldOffset(8)]
        public nint Pointer;
    }

    // ---- DXGI device manager (hardware encoding path)

    public static nint CreateDxgiDeviceManager(nint d3dDevice)
    {
        Com.Check(MFCreateDXGIDeviceManager(out uint token, out nint manager), "MFCreateDXGIDeviceManager");
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Com.Slot(manager, DxgiManagerResetDevice))(manager, d3dDevice, token), "IMFDXGIDeviceManager.ResetDevice");
        return manager;
    }

    // ---- encoder discovery

    /// <summary>Friendly names of installed encoders for an output subtype (hardware or software).</summary>
    public static IReadOnlyList<string> EnumerateEncoders(Guid category, Guid majorType, Guid subType, bool hardware)
    {
        var info = new RegisterTypeInfo { MajorType = majorType, SubType = subType };
        uint flags = (hardware ? MFT_ENUM_FLAG_HARDWARE : MFT_ENUM_FLAG_SYNCMFT) | MFT_ENUM_FLAG_SORTANDFILTER;
        Com.Check(MFTEnumEx(category, flags, 0, (nint)(&info), out nint array, out uint count), "MFTEnumEx");
        var names = new List<string>();
        for (int i = 0; i < count; i++)
        {
            nint activate = ((nint*)array)[i];
            names.Add(GetString(activate, MFT_FRIENDLY_NAME) ?? "(unnamed)");
            Marshal.Release(activate);
        }

        Com.CoTaskMemFree(array);
        return names;
    }
}
