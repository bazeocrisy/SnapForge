using SnapForge.App.Video.Interop;

namespace SnapForge.App.Video;

/// <summary>Video stream settings for <see cref="Mp4Writer"/>. Width and height must be even (H.264 requirement).</summary>
internal sealed record VideoStreamFormat(int Width, int Height, int FramesPerSecond, int BitsPerSecond);

/// <summary>How the writer receives frames.</summary>
internal enum EncoderInput
{
    /// <summary>BGRA bytes in system memory; Media Foundation converts and encodes (software or hardware MFT).</summary>
    SystemMemory,

    /// <summary>Direct3D textures via a DXGI device manager; enables hardware encoders without CPU copies.</summary>
    GpuTextures,
}

/// <summary>
/// Writes an MP4 (H.264 video + optional AAC audio) with Media Foundation's sink writer, which uses the encoders that
/// ship with Windows (hardware encoders when available). <b>Fragmented MP4</b> is used by default: the file is written
/// as a series of self-contained fragments, so a crash loses at most the last unfinished fragment instead of the
/// whole recording.
/// </summary>
internal sealed unsafe class Mp4Writer : IDisposable
{
    public const int AudioSampleRate = 48000;
    public const int AudioChannels = 2;
    public const int AudioBitsPerSecond = 160_000;

    private nint _writer;
    private readonly int _videoStream;
    private readonly int _audioStream = -1;
    private readonly VideoStreamFormat _format;
    private bool _finalized;

    public Mp4Writer(string path, VideoStreamFormat format, bool withAudio, bool fragmented, EncoderInput input, nint dxgiDeviceManager, bool allowHardware = true)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.Width % 2 != 0 || format.Height % 2 != 0 || format.Width < 16 || format.Height < 16)
        {
            throw new ArgumentException("Video size must be even and at least 16×16.", nameof(format));
        }

        _format = format;
        Input = input;
        nint attrs = Mf.CreateAttributes(4);
        try
        {
            Mf.SetGuid(attrs, Mf.TRANSCODE_CONTAINERTYPE, fragmented ? Mf.TranscodeContainerType_FMPEG4 : Mf.TranscodeContainerType_MPEG4);
            Mf.SetUInt32(attrs, Mf.READWRITE_ENABLE_HARDWARE_TRANSFORMS, allowHardware ? 1u : 0u);
            if (input == EncoderInput.GpuTextures)
            {
                Mf.SetUnknown(attrs, Mf.SINK_WRITER_D3D_MANAGER, dxgiDeviceManager);
            }

            _writer = Mf.CreateSinkWriter(path, attrs);
        }
        finally
        {
            Com.Release(ref attrs);
        }

        try
        {
            _videoStream = AddVideoStream();
            if (withAudio)
            {
                _audioStream = AddAudioStream();
            }

            Mf.BeginWriting(_writer);
            Encoder = Mf.GetEncoder(_writer, _videoStream);
        }
        catch
        {
            Com.Release(ref _writer);
            throw;
        }
    }

    public EncoderInput Input { get; }

    /// <summary>The video encoder Media Foundation chose (name, hardware?), if Windows reports it.</summary>
    public (string Name, bool Hardware)? Encoder { get; }

    /// <summary>Video samples handed to the writer but not yet encoded (encoder backlog), and the time gap between them.</summary>
    public (long Pending, long LatencyMs) Backlog()
    {
        Mf.SinkWriterStatistics? s = _writer == 0 ? null : Mf.GetStatistics(_writer, _videoStream);
        return s is { } st ? ((long)(st.NumSamplesReceived - st.NumSamplesEncoded), Math.Max(0, (st.LastTimestampReceived - st.LastTimestampEncoded) / 10_000)) : (0, 0);
    }

    public bool HasAudio => _audioStream >= 0;

    public long FramesWritten { get; private set; }

    public long AudioFramesWritten { get; private set; }

    private int AddVideoStream()
    {
        nint output = Mf.CreateMediaType();
        nint input = Mf.CreateMediaType();
        try
        {
            Mf.SetGuid(output, Mf.MT_MAJOR_TYPE, Mf.MediaType_Video);
            Mf.SetGuid(output, Mf.MT_SUBTYPE, Mf.VideoFormat_H264);
            Mf.SetUInt32(output, Mf.MT_AVG_BITRATE, (uint)_format.BitsPerSecond);
            Mf.SetUInt32(output, Mf.MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
            Mf.SetUInt32(output, Mf.MT_MPEG2_PROFILE, Mf.eAVEncH264VProfile_Main);
            Mf.SetRatio(output, Mf.MT_FRAME_SIZE, (uint)_format.Width, (uint)_format.Height);
            Mf.SetRatio(output, Mf.MT_FRAME_RATE, (uint)_format.FramesPerSecond, 1);
            Mf.SetRatio(output, Mf.MT_PIXEL_ASPECT_RATIO, 1, 1);
            // A key frame every 2 s bounds what an interrupted recording can lose to the last fragment.
            Mf.SetUInt32(output, Mf.MT_MAX_KEYFRAME_SPACING, (uint)(_format.FramesPerSecond * 2));
            int stream = Mf.AddStream(_writer, output);

            Mf.SetGuid(input, Mf.MT_MAJOR_TYPE, Mf.MediaType_Video);
            Mf.SetGuid(input, Mf.MT_SUBTYPE, Mf.VideoFormat_RGB32);
            Mf.SetUInt32(input, Mf.MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
            Mf.SetRatio(input, Mf.MT_FRAME_SIZE, (uint)_format.Width, (uint)_format.Height);
            Mf.SetRatio(input, Mf.MT_FRAME_RATE, (uint)_format.FramesPerSecond, 1);
            Mf.SetRatio(input, Mf.MT_PIXEL_ASPECT_RATIO, 1, 1);
            Mf.SetUInt32(input, Mf.MT_DEFAULT_STRIDE, (uint)(_format.Width * 4)); // positive = top-down rows
            Mf.SetUInt32(input, Mf.MT_ALL_SAMPLES_INDEPENDENT, 1);
            Mf.SetInputMediaType(_writer, stream, input);
            return stream;
        }
        finally
        {
            Com.Release(ref output);
            Com.Release(ref input);
        }
    }

    private int AddAudioStream()
    {
        nint output = Mf.CreateMediaType();
        nint input = Mf.CreateMediaType();
        try
        {
            Mf.SetGuid(output, Mf.MT_MAJOR_TYPE, Mf.MediaType_Audio);
            Mf.SetGuid(output, Mf.MT_SUBTYPE, Mf.AudioFormat_AAC);
            Mf.SetUInt32(output, Mf.MT_AUDIO_SAMPLES_PER_SECOND, AudioSampleRate);
            Mf.SetUInt32(output, Mf.MT_AUDIO_NUM_CHANNELS, AudioChannels);
            Mf.SetUInt32(output, Mf.MT_AUDIO_BITS_PER_SAMPLE, 16);
            Mf.SetUInt32(output, Mf.MT_AUDIO_AVG_BYTES_PER_SECOND, AudioBitsPerSecond / 8);
            int stream = Mf.AddStream(_writer, output);

            Mf.SetGuid(input, Mf.MT_MAJOR_TYPE, Mf.MediaType_Audio);
            Mf.SetGuid(input, Mf.MT_SUBTYPE, Mf.AudioFormat_PCM);
            Mf.SetUInt32(input, Mf.MT_AUDIO_SAMPLES_PER_SECOND, AudioSampleRate);
            Mf.SetUInt32(input, Mf.MT_AUDIO_NUM_CHANNELS, AudioChannels);
            Mf.SetUInt32(input, Mf.MT_AUDIO_BITS_PER_SAMPLE, 16);
            Mf.SetUInt32(input, Mf.MT_AUDIO_BLOCK_ALIGNMENT, AudioChannels * 2);
            Mf.SetUInt32(input, Mf.MT_AUDIO_AVG_BYTES_PER_SECOND, AudioSampleRate * AudioChannels * 2);
            Mf.SetInputMediaType(_writer, stream, input);
            return stream;
        }
        finally
        {
            Com.Release(ref output);
            Com.Release(ref input);
        }
    }

    /// <summary>Writes one BGRA frame from system memory (rows of <paramref name="stride"/> bytes, top-down).</summary>
    public void WriteFrame(byte* bgra, int stride, long time100ns, long duration100ns)
    {
        int rowBytes = _format.Width * 4;
        int length = rowBytes * _format.Height;
        nint buffer = Mf.CreateMemoryBuffer(length);
        nint sample = 0;
        try
        {
            byte* dst = Mf.LockBuffer(buffer, out _, out _);
            try
            {
                for (int y = 0; y < _format.Height; y++)
                {
                    Buffer.MemoryCopy(bgra + ((long)y * stride), dst + ((long)y * rowBytes), rowBytes, rowBytes);
                }
            }
            finally
            {
                Mf.UnlockBuffer(buffer);
            }

            Mf.SetBufferLength(buffer, length);
            sample = Mf.CreateSample();
            Mf.AddBuffer(sample, buffer);
            Mf.SetSampleTime(sample, time100ns);
            Mf.SetSampleDuration(sample, duration100ns);
            Mf.WriteSample(_writer, _videoStream, sample);
            FramesWritten++;
        }
        finally
        {
            Com.Release(ref sample);
            Com.Release(ref buffer);
        }
    }

    /// <summary>Writes one frame from a D3D11 texture (GPU path; the texture must not be reused until encoded).</summary>
    public void WriteTexture(nint texture, long time100ns, long duration100ns)
    {
        nint buffer = Mf.CreateDxgiSurfaceBuffer(texture);
        nint sample = 0;
        try
        {
            Mf.SetBufferLength(buffer, _format.Width * _format.Height * 4);
            sample = Mf.CreateSample();
            Mf.AddBuffer(sample, buffer);
            Mf.SetSampleTime(sample, time100ns);
            Mf.SetSampleDuration(sample, duration100ns);
            Mf.WriteSample(_writer, _videoStream, sample);
            FramesWritten++;
        }
        finally
        {
            Com.Release(ref sample);
            Com.Release(ref buffer);
        }
    }

    /// <summary>Writes interleaved 16-bit stereo PCM at 48 kHz.</summary>
    public void WriteAudio(ReadOnlySpan<short> interleaved, long time100ns)
    {
        if (_audioStream < 0 || interleaved.IsEmpty)
        {
            return;
        }

        int bytes = interleaved.Length * 2;
        int frames = interleaved.Length / AudioChannels;
        nint buffer = Mf.CreateMemoryBuffer(bytes);
        nint sample = 0;
        try
        {
            byte* dst = Mf.LockBuffer(buffer, out _, out _);
            try
            {
                fixed (short* src = interleaved)
                {
                    Buffer.MemoryCopy(src, dst, bytes, bytes);
                }
            }
            finally
            {
                Mf.UnlockBuffer(buffer);
            }

            Mf.SetBufferLength(buffer, bytes);
            sample = Mf.CreateSample();
            Mf.AddBuffer(sample, buffer);
            Mf.SetSampleTime(sample, time100ns);
            Mf.SetSampleDuration(sample, frames * 10_000_000L / AudioSampleRate);
            Mf.WriteSample(_writer, _audioStream, sample);
            AudioFramesWritten += frames;
        }
        finally
        {
            Com.Release(ref sample);
            Com.Release(ref buffer);
        }
    }

    /// <summary>Marks a gap in the video stream (e.g. pause) so the muxer does not wait for samples.</summary>
    public void VideoGap(long time100ns) => Mf.SendStreamTick(_writer, _videoStream, time100ns);

    /// <summary>Completes the file (writes the remaining fragments and indexes). Must be called once.</summary>
    public void Complete()
    {
        if (!_finalized && _writer != 0)
        {
            _finalized = true;
            Mf.FinalizeWriter(_writer);
        }
    }

    public void Dispose() => Com.Release(ref _writer);
}
