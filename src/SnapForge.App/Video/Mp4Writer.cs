using System.Globalization;
using System.IO;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Video;

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
    private int _videoStream;
    private int _audioStream = -1;
    private readonly VideoStreamFormat _format;
    private bool _finalized;

    /// <param name="tuning">Encoder settings to request (null = <see cref="VideoEncoderTuning.Default"/>). When the
    /// encoder refuses them, the writer steps down <see cref="VideoEncoderTuning.FallbackLadder"/> and logs what it
    /// used (<see cref="SettingsSummary"/>).</param>
    public Mp4Writer(string path, VideoStreamFormat format, bool withAudio, bool fragmented, EncoderInput input, nint dxgiDeviceManager, bool allowHardware = true, VideoEncoderTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.Width % 2 != 0 || format.Height % 2 != 0 || format.Width < 16 || format.Height < 16)
        {
            throw new ArgumentException("Video size must be even and at least 16×16.", nameof(format));
        }

        _format = format;
        Input = input;
        RequestedTuning = tuning ?? VideoEncoderTuning.Default;
        IReadOnlyList<VideoEncoderTuning> ladder = RequestedTuning.FallbackLadder();
        var refused = new List<string>();
        for (int rung = 0; ; rung++)
        {
            VideoEncoderTuning attempt = ladder[rung];
            bool last = rung == ladder.Count - 1;
            try
            {
                Open(path, withAudio, fragmented, input, dxgiDeviceManager, allowHardware, attempt);
                if (attempt.RequiresConfirmedNoBFrames && Applied?.BFrames != 0 && !last)
                {
                    // Low-latency mode off is only safe when the encoder confirms it makes no B-frames (exact timestamps).
                    throw new EncoderSettingsRefusedException($"the encoder did not confirm 0 B-frames (it reports {Applied?.BFrames?.ToString(CultureInfo.InvariantCulture) ?? "nothing"})");
                }

                AppliedTuning = attempt;
                break;
            }
            catch (Exception ex) when (ex is NativeCallException or EncoderSettingsRefusedException)
            {
                Close();
                if (last)
                {
                    throw;
                }

                refused.Add($"{attempt.Name}: {ex.Message}");
                TryDelete(path);
            }
        }

        Fallbacks = refused;
        SettingsSummary = Summarize();
        RecordingLog.Write($"video encoder {Encoder?.Name ?? "unknown"} ({(Encoder?.Hardware == true ? "hardware" : "software")}), {format.Width}×{format.Height} {format.FramesPerSecond} FPS: {SettingsSummary}");
    }

    public EncoderInput Input { get; }

    /// <summary>The video encoder Media Foundation chose (name, hardware?), if Windows reports it.</summary>
    public (string Name, bool Hardware)? Encoder { get; private set; }

    /// <summary>The tuning asked for.</summary>
    public VideoEncoderTuning RequestedTuning { get; }

    /// <summary>The rung of the fallback ladder the encoder accepted.</summary>
    public VideoEncoderTuning AppliedTuning { get; private set; } = VideoEncoderTuning.Default;

    /// <summary>What the encoder reported back through ICodecAPI (null: no stream opened).</summary>
    public EncoderReadback? Applied { get; private set; }

    /// <summary>Rungs that were refused, with the reason (empty when the requested tuning worked).</summary>
    public IReadOnlyList<string> Fallbacks { get; }

    /// <summary>One line: applied settings, what the encoder reports back, and refused fallbacks (logs and reports).</summary>
    public string SettingsSummary { get; }

    private void Open(string path, bool withAudio, bool fragmented, EncoderInput input, nint dxgiDeviceManager, bool allowHardware, VideoEncoderTuning tuning)
    {
        nint attrs = Mf.CreateAttributes(4);
        try
        {
            Mf.SetGuid(attrs, Mf.TRANSCODE_CONTAINERTYPE, fragmented ? Mf.TranscodeContainerType_FMPEG4 : Mf.TranscodeContainerType_MPEG4);
            Mf.SetUInt32(attrs, Mf.READWRITE_ENABLE_HARDWARE_TRANSFORMS, allowHardware ? 1u : 0u);

            // No B-frames, so every frame is stored in presentation order and its timestamp is exact in the file. With
            // B-frames the decoded timestamps of fragmented MP4s were measured up to 300 ms off (CI A/V sync test,
            // 2026-11-26). RC2 used low-latency mode for that; the RC3 default asks for 0 B-frames explicitly instead
            // (low-latency mode also costs quality on many encoders) and goes back to low-latency mode when the
            // encoder cannot confirm 0 B-frames.
            Mf.SetUInt32(attrs, Mf.LOW_LATENCY, tuning.LowLatency ? 1u : 0u);
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

        _videoStream = AddVideoStream(tuning);
        _audioStream = withAudio ? AddAudioStream() : -1;
        Mf.BeginWriting(_writer);
        Encoder = Mf.GetEncoder(_writer, _videoStream);
    }

    private void Close()
    {
        Com.Release(ref _writer);
        _audioStream = -1;
        Applied = null;
        Encoder = null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Video samples handed to the writer but not yet encoded (encoder backlog), and the time gap between them.</summary>
    public (long Pending, long LatencyMs) Backlog()
    {
        Mf.SinkWriterStatistics? s = _writer == 0 ? null : Mf.GetStatistics(_writer, _videoStream);
        return s is { } st ? ((long)(st.NumSamplesReceived - st.NumSamplesEncoded), Math.Max(0, (st.LastTimestampReceived - st.LastTimestampEncoded) / 10_000)) : (0, 0);
    }

    public bool HasAudio => _audioStream >= 0;

    public long FramesWritten { get; private set; }

    public long AudioFramesWritten { get; private set; }

    private int AddVideoStream(VideoEncoderTuning tuning)
    {
        nint output = Mf.CreateMediaType();
        nint input = Mf.CreateMediaType();
        nint encoding = 0;
        try
        {
            Mf.SetGuid(output, Mf.MT_MAJOR_TYPE, Mf.MediaType_Video);
            Mf.SetGuid(output, Mf.MT_SUBTYPE, Mf.VideoFormat_H264);
            Mf.SetUInt32(output, Mf.MT_AVG_BITRATE, (uint)_format.BitsPerSecond);
            Mf.SetUInt32(output, Mf.MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
            Mf.SetUInt32(output, Mf.MT_MPEG2_PROFILE, (uint)tuning.Profile);
            Mf.SetRatio(output, Mf.MT_FRAME_SIZE, (uint)_format.Width, (uint)_format.Height);
            Mf.SetRatio(output, Mf.MT_FRAME_RATE, (uint)_format.FramesPerSecond, 1);
            Mf.SetRatio(output, Mf.MT_PIXEL_ASPECT_RATIO, 1, 1);
            // A key frame every 2 s bounds what an interrupted recording can lose to the last fragment.
            Mf.SetUInt32(output, Mf.MT_MAX_KEYFRAME_SPACING, (uint)tuning.GopFrames(_format.FramesPerSecond));
            int stream = Mf.AddStream(_writer, output);

            Mf.SetGuid(input, Mf.MT_MAJOR_TYPE, Mf.MediaType_Video);
            Mf.SetGuid(input, Mf.MT_SUBTYPE, Mf.VideoFormat_RGB32);
            Mf.SetUInt32(input, Mf.MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
            Mf.SetRatio(input, Mf.MT_FRAME_SIZE, (uint)_format.Width, (uint)_format.Height);
            Mf.SetRatio(input, Mf.MT_FRAME_RATE, (uint)_format.FramesPerSecond, 1);
            Mf.SetRatio(input, Mf.MT_PIXEL_ASPECT_RATIO, 1, 1);
            Mf.SetUInt32(input, Mf.MT_DEFAULT_STRIDE, (uint)(_format.Width * 4)); // positive = top-down rows
            Mf.SetUInt32(input, Mf.MT_ALL_SAMPLES_INDEPENDENT, 1);

            // Encoder properties travel as the sink writer's encoding parameters (applied when it creates the encoder,
            // before streaming); afterwards they are read back and, where the encoder does not report them, set once
            // directly through ICodecAPI.
            IReadOnlyList<EncoderProperty> wanted = EncoderProperty.For(tuning, _format.BitsPerSecond, _format.FramesPerSecond);
            if (wanted.Count > 0)
            {
                encoding = Mf.CreateAttributes((uint)wanted.Count);
                foreach (EncoderProperty p in wanted)
                {
                    Mf.SetUInt32(encoding, p.Key, p.Value);
                }
            }

            Mf.SetInputMediaType(_writer, stream, input, encoding);
            Applied = ConfirmProperties(stream, wanted);
            return stream;
        }
        finally
        {
            Com.Release(ref output);
            Com.Release(ref input);
            Com.Release(ref encoding);
        }
    }

    /// <summary>Reads the encoder's properties back; a requested value it does not report is set once directly.</summary>
    private EncoderReadback ConfirmProperties(int stream, IReadOnlyList<EncoderProperty> wanted)
    {
        nint api = Mf.GetEncoderCodecApi(_writer, stream);
        if (api == 0)
        {
            return new EncoderReadback(false, null, null, null, null, null, null, wanted.Count == 0 ? [] : ["the encoder exposes no ICodecAPI: properties were sent as encoding parameters, not confirmed"]);
        }

        try
        {
            var notes = new List<string>();
            foreach (EncoderProperty p in wanted)
            {
                long? now = Mf.GetCodecValue(api, p.Key, out int hr);
                if (now == p.Value)
                {
                    continue;
                }

                int set = Mf.SetCodecValue(api, p.Key, p.Value, p.Boolean);
                long? after = Mf.GetCodecValue(api, p.Key, out int hr2);
                notes.Add(after == p.Value
                    ? $"{p.Name}={p.Value} applied directly (as an encoding parameter it read {Show(now, hr)})"
                    : $"{p.Name}={p.Value} refused (set 0x{set:X8}; encoder reports {Show(after, hr2)})");
            }

            long? Read(Guid key) => Mf.GetCodecValue(api, key, out _);
            return new EncoderReadback(
                true,
                Read(Mf.CODECAPI_AVEncCommonRateControlMode),
                Read(Mf.CODECAPI_AVEncCommonMeanBitRate),
                Read(Mf.CODECAPI_AVEncCommonMaxBitRate),
                Read(Mf.CODECAPI_AVEncMPVGOPSize),
                Read(Mf.CODECAPI_AVEncH264CABACEnable),
                Read(Mf.CODECAPI_AVEncMPVDefaultBPictureCount),
                notes);
        }
        finally
        {
            Com.Release(ref api);
        }

        static string Show(long? v, int hr) => v is { } x ? x.ToString(CultureInfo.InvariantCulture) : $"nothing (0x{hr:X8})";
    }

    private string Summarize()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(AppliedTuning.Describe());
        sb.Append(CultureInfo.InvariantCulture, $"; mean {_format.BitsPerSecond / 1e6:0.0#} Mbit/s");
        if (AppliedTuning.RateControl == VideoRateControl.PeakConstrainedVbr)
        {
            sb.Append(CultureInfo.InvariantCulture, $", peak {AppliedTuning.PeakBitRate(_format.BitsPerSecond) / 1e6:0.0#} Mbit/s");
        }

        if (Applied is { } a)
        {
            sb.Append("; encoder reports ").Append(a.Describe());
        }

        if (Fallbacks.Count > 0)
        {
            sb.Append("; refused: ").Append(string.Join(" | ", Fallbacks));
        }

        return sb.ToString();
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

/// <summary>An encoder could not confirm a requested setting (the writer tries the next fallback).</summary>
internal sealed class EncoderSettingsRefusedException : Exception
{
    public EncoderSettingsRefusedException()
    {
    }

    public EncoderSettingsRefusedException(string message)
        : base(message)
    {
    }

    public EncoderSettingsRefusedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>One CODECAPI_* property requested from the encoder.</summary>
internal readonly record struct EncoderProperty(string Name, Guid Key, uint Value, bool Boolean)
{
    /// <summary>The properties a tuning asks for (none for the RC2 tuning).</summary>
    public static IReadOnlyList<EncoderProperty> For(VideoEncoderTuning tuning, int meanBitRate, int fps)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var list = new List<EncoderProperty>();
        if (tuning.RateControl != VideoRateControl.EncoderDefault)
        {
            list.Add(new("RateControlMode", Mf.CODECAPI_AVEncCommonRateControlMode, (uint)tuning.RateControl, false));
            list.Add(new("MeanBitRate", Mf.CODECAPI_AVEncCommonMeanBitRate, (uint)meanBitRate, false));
            if (tuning.RateControl == VideoRateControl.PeakConstrainedVbr)
            {
                list.Add(new("MaxBitRate", Mf.CODECAPI_AVEncCommonMaxBitRate, (uint)tuning.PeakBitRate(meanBitRate), false));
            }
        }

        if (tuning.GopSeconds > 0)
        {
            list.Add(new("GOPSize", Mf.CODECAPI_AVEncMPVGOPSize, (uint)tuning.GopFrames(fps), false));
        }

        if (tuning.Cabac is { } cabac)
        {
            list.Add(new("CABAC", Mf.CODECAPI_AVEncH264CABACEnable, cabac ? 1u : 0u, true));
        }

        if (tuning.BFrames is { } b)
        {
            list.Add(new("BPictureCount", Mf.CODECAPI_AVEncMPVDefaultBPictureCount, (uint)b, false));
        }

        return list;
    }
}

/// <summary>Encoder properties as the encoder reports them through ICodecAPI::GetValue (null = not reported).</summary>
internal sealed record EncoderReadback(bool HasCodecApi, long? RateControl, long? MeanBitRate, long? MaxBitRate, long? GopSize, long? Cabac, long? BFrames, IReadOnlyList<string> Notes)
{
    public string RateControlName => RateControl switch
    {
        0 => "CBR",
        1 => "PeakConstrainedVBR",
        2 => "UnconstrainedVBR",
        3 => "Quality",
        4 => "LowDelayVBR",
        5 => "GlobalVBR",
        6 => "GlobalLowDelayVBR",
        null => "not reported",
        { } x => x.ToString(CultureInfo.InvariantCulture),
    };

    public string Describe()
    {
        if (!HasCodecApi)
        {
            return string.Join("; ", Notes.DefaultIfEmpty("no ICodecAPI (nothing to confirm)"));
        }

        static string V(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        string text = $"rate control {RateControlName}, mean {V(MeanBitRate)}, max {V(MaxBitRate)}, GOP {V(GopSize)}, CABAC {V(Cabac)}, B-frames {V(BFrames)}";
        return Notes.Count == 0 ? text : text + " (" + string.Join("; ", Notes) + ")";
    }
}
