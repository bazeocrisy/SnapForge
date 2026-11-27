using System.Buffers.Binary;
using System.Text;
using SnapForge.Core.Video;

namespace SnapForge.Core.Tests;

/// <summary>Milestone V1 core: recording clock (pause/resume), audio mixing and drift, MP4 inspection and recovery.</summary>
public class VideoCoreTests
{
    private const long Second = 10_000_000;

    // ---- RecordingClock (VID-R12)

    [Fact]
    public void Clock_maps_source_time_and_removes_paused_intervals()
    {
        var c = new RecordingClock();
        c.Start(1000 * Second);
        Assert.True(c.TryMap((1000 * Second) + (2 * Second), out long t));
        Assert.Equal(2 * Second, t);

        c.Pause(1005 * Second);
        Assert.False(c.TryMap(1006 * Second, out _)); // captured while paused → dropped
        Assert.Equal(5 * Second, c.Elapsed(1009 * Second)); // elapsed time stops during a pause
        c.Resume(1010 * Second);

        Assert.True(c.TryMap(1011 * Second, out t));
        Assert.Equal(6 * Second, t); // 11 s of wall time − 5 s paused
        Assert.Equal(5 * Second, c.PausedTotal);
        Assert.True(c.TryMap(1004 * Second, out t)); // a late sample from before the pause keeps its place
        Assert.Equal(4 * Second, t);
        Assert.False(c.TryMap(1007 * Second, out _)); // late sample from inside the pause is still dropped
        Assert.False(c.TryMap(999 * Second, out _)); // before the start
    }

    [Fact]
    public void Clock_handles_repeated_pauses_and_redundant_calls()
    {
        var c = new RecordingClock();
        c.Start(0);
        for (int i = 0; i < 100; i++)
        {
            long baseTime = i * 3 * Second;
            c.Pause(baseTime + Second);
            c.Pause(baseTime + Second + 1); // ignored: already paused
            c.Resume(baseTime + (2 * Second));
            c.Resume(baseTime + (2 * Second) + 1); // ignored: not paused
        }

        Assert.Equal(100, c.Pauses.Count);
        Assert.Equal(100 * Second, c.PausedTotal);
        Assert.True(c.TryMap(300 * Second, out long t));
        Assert.Equal(200 * Second, t);
        Assert.Throws<InvalidOperationException>(() => c.Start(5));
        Assert.Throws<InvalidOperationException>(() => new RecordingClock().Pause(0));
    }

    // ---- AudioMixer (VID-R01, VID-R07, VID-R08, VID-R13)

    private static float[] Tone(int frames, float value)
    {
        var a = new float[frames * 2];
        Array.Fill(a, value);
        return a;
    }

    [Fact]
    public void Mixer_sums_two_sources_and_clamps()
    {
        var m = new AudioMixer("system", "mic");
        m.Push(0, 0, Tone(480, 0.25f));
        m.Push(1, 0, Tone(480, 0.5f));
        var pcm = new short[960];
        Assert.Equal(480, m.Mix(m.ReadyFrame(), pcm));
        Assert.All(pcm, v => Assert.InRange(v, (short)24570, (short)24580)); // 0.75 × 32767

        m.Push(0, AudioMixer.ToTime(480), Tone(480, 0.9f));
        m.Push(1, AudioMixer.ToTime(480), Tone(480, 0.9f));
        Assert.Equal(480, m.Mix(m.ReadyFrame(), pcm));
        Assert.All(pcm, v => Assert.Equal(short.MaxValue, v)); // clamped, never wraps around
    }

    [Fact]
    public void Gap_in_a_source_becomes_silence_and_keeps_later_audio_in_place()
    {
        var m = new AudioMixer("system");
        m.Push(0, 0, Tone(4800, 0.5f)); // 0–100 ms
        // Device sends nothing for 400 ms (silent system sound, or a Bluetooth switch), then resumes at 500 ms.
        m.Push(0, AudioMixer.ToTime(24000), Tone(4800, 0.5f));
        var pcm = new short[28800 * 2];
        Assert.Equal(28800, m.Mix(m.ReadyFrame(), pcm));
        Assert.NotEqual(0, pcm[2 * 100]);
        Assert.Equal(0, pcm[2 * 10000]); // inside the gap
        Assert.NotEqual(0, pcm[2 * 24100]); // resumed audio lands at exactly 500 ms
        AudioSourceStats s = m.Stats()[0];
        Assert.Equal(1, s.Reanchors);
        Assert.Equal(19200, s.SilenceInserted);
    }

    [Fact]
    public void Small_jitter_is_measured_but_not_corrected_and_overlap_is_dropped()
    {
        var m = new AudioMixer("mic");
        long frame = 0;
        for (int i = 0; i < 100; i++)
        {
            long jitter = (i % 3) - 1; // ±1 frame of timestamp jitter
            m.Push(0, AudioMixer.ToTime(frame + jitter), Tone(480, 0.1f));
            frame += 480;
        }

        AudioSourceStats s = m.Stats()[0];
        Assert.Equal(0, s.Reanchors);
        Assert.True(s.MaxDriftMs < 1);

        // A packet timestamped 200 ms in the past overlaps placed audio: the overlap is dropped, not double-played.
        m.Push(0, AudioMixer.ToTime(frame - 9600), Tone(9600 + 480, 0.1f));
        s = m.Stats()[0];
        Assert.Equal(1, s.Reanchors);
        Assert.Equal(9600, s.FramesDropped);
    }

    [Fact]
    public void After_resume_a_small_gap_is_placed_exactly_instead_of_shifting_audio()
    {
        var m = new AudioMixer("system");
        m.Push(0, 0, Tone(4800, 0.5f)); // 0–100 ms
        // Resume: the packet straddling the resume instant was dropped, so the next one starts 10 ms later than the
        // previous end — below the 40 ms re-anchor threshold, which would normally leave the audio 10 ms early.
        m.ReanchorAll();
        m.Push(0, AudioMixer.ToTime(4800 + 480), Tone(480, 0.5f));
        var pcm = new short[(4800 + 960) * 2];
        Assert.Equal(4800 + 960, m.Mix(m.ReadyFrame(), pcm));
        Assert.Equal(0, pcm[(4800 + 100) * 2]); // the 10 ms gap is silence
        Assert.NotEqual(0, pcm[(4800 + 480 + 10) * 2]); // the packet sits exactly at its timestamp
        Assert.Equal(1, m.Stats()[0].Reanchors);

        // Without a resume, small jitter is not corrected (covered elsewhere); the flag applies to one packet only.
        m.Push(0, AudioMixer.ToTime(4800 + 960 + 100), Tone(480, 0.5f));
        Assert.Equal(1, m.Stats()[0].Reanchors);
    }

    [Fact]
    public void Mixer_memory_is_bounded_when_the_encoder_falls_behind()
    {
        var m = new AudioMixer("system");
        for (int i = 0; i < 1000; i++)
        {
            m.Push(0, AudioMixer.ToTime(i * 480L), Tone(480, 0.1f)); // 10 s, never mixed
        }

        AudioSourceStats s = m.Stats()[0];
        Assert.True(s.FramesDropped >= 480_000 - AudioMixer.MaxBufferedFrames);
        var pcm = new short[AudioMixer.MaxBufferedFrames * 2 * 3];
        int mixed = m.Mix(m.ReadyFrame(), pcm);
        Assert.Equal(480_000, mixed); // timeline still complete: dropped audio became silence
        Assert.Equal(0, pcm[2]); // the oldest audio was the part dropped
        Assert.NotEqual(0, pcm[(mixed - 1) * 2]);
    }

    [Fact]
    public void Muted_and_disconnected_sources_mix_as_silence_without_moving_the_timeline()
    {
        var m = new AudioMixer("system", "mic");
        m.SetMuted(1, true);
        m.Push(0, 0, Tone(480, 0.2f));
        m.Push(1, 0, Tone(480, 0.7f));
        var pcm = new short[960];
        Assert.Equal(480, m.Mix(m.ReadyFrame(), pcm));
        Assert.InRange(pcm[0], (short)6550, (short)6556); // only the system sound
        Assert.True(m.Peak(1) > 0.6f); // the level meter still shows the muted mic

        m.Deactivate(1); // microphone unplugged
        m.Push(0, AudioMixer.ToTime(480), Tone(480, 0.2f));
        Assert.Equal(960, m.ReadyFrame()); // no longer waits for the missing mic
        Assert.False(m.Stats()[1].Active);
        Assert.Equal(480, m.Mix(m.ReadyFrame(), pcm));

        // The mic comes back later and is placed at its timestamp.
        m.Push(1, AudioMixer.ToTime(4800), Tone(480, 0.3f));
        m.Push(0, AudioMixer.ToTime(960), Tone(4320, 0.2f));
        m.SetMuted(1, false);
        var more = new short[4320 * 2];
        Assert.Equal(4320, m.Mix(m.ReadyFrame(), more));
        Assert.InRange(more[0], (short)6550, (short)6556);
    }

    // ---- Mp4Inspector (VID-R04, VID-R05)

    private static byte[] Box(string type, int payload)
    {
        var b = new byte[8 + payload];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)b.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(b, 4);
        return b;
    }

    private static byte[] FragmentedFile(int fragments)
    {
        using var ms = new MemoryStream();
        ms.Write(Box("ftyp", 16));
        ms.Write(Box("moov", 200));
        for (int i = 0; i < fragments; i++)
        {
            ms.Write(Box("moof", 60));
            ms.Write(Box("mdat", 1000));
        }

        return ms.ToArray();
    }

    [Fact]
    public void Complete_fragmented_file_is_recognised()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("a.mp4");
        File.WriteAllBytes(path, [.. FragmentedFile(3), .. Box("mfra", 20)]);
        Mp4Structure s = Mp4Inspector.Inspect(path);
        Assert.True(s.IsComplete);
        Assert.True(s.IsFragmented);
        Assert.Equal(3, s.CompleteFragments);
        Assert.Equal(["ftyp", "moov", "moof", "mdat", "moof", "mdat", "moof", "mdat", "mfra"], s.Boxes.Select(b => b.Type));
    }

    [Fact]
    public void Interrupted_fragmented_file_is_cut_back_to_its_last_complete_fragment()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("crash.mp4");
        byte[] full = FragmentedFile(4);
        int cut = full.Length - 500; // the 4th fragment's media data was only half written
        File.WriteAllBytes(path, full[..cut]);

        Mp4Structure s = Mp4Inspector.Inspect(path);
        Assert.False(s.IsComplete);
        Assert.True(s.IsRecoverable);
        Assert.Equal(3, s.CompleteFragments);
        Assert.Contains("cut off", s.Problem, StringComparison.Ordinal);

        Mp4Structure? after = Mp4Inspector.Recover(path);
        Assert.NotNull(after);
        Assert.True(after!.IsComplete);
        Assert.Equal(3, after.CompleteFragments);
        Assert.Equal(full.Length - (68 + 1008), new FileInfo(path).Length); // one whole fragment (moof + mdat) removed
    }

    [Fact]
    public void Fragment_header_without_its_media_data_is_removed()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("b.mp4");
        File.WriteAllBytes(path, [.. FragmentedFile(2), .. Box("moof", 60)]);
        Mp4Structure s = Mp4Inspector.Inspect(path);
        Assert.Equal(2, s.CompleteFragments);
        Assert.False(s.IsComplete);
        Assert.True(Mp4Inspector.Recover(path)!.IsComplete);
    }

    [Theory]
    [InlineData("header-only")]
    [InlineData("no-moov")]
    [InlineData("garbage")]
    [InlineData("regular-cut")]
    public void Files_that_cannot_be_recovered_are_left_untouched(string kind)
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("x.mp4");
        byte[] bytes = kind switch
        {
            "header-only" => [.. Box("ftyp", 16), .. Box("moov", 200)], // no fragments: nothing was recorded
            "no-moov" => [.. Box("ftyp", 16), .. Box("moof", 60), .. Box("mdat", 100)],
            "garbage" => [.. Enumerable.Range(0, 300).Select(i => (byte)(i * 7))],
            _ => [.. Box("ftyp", 16), .. Box("mdat", 5000).AsSpan(0, 3000).ToArray()], // non-fragmented MP4 cut off: moov never written
        };
        File.WriteAllBytes(path, bytes);
        Assert.Null(Mp4Inspector.Recover(path));
        Assert.Equal(bytes.Length, new FileInfo(path).Length);
    }

    [Fact]
    public void Large_box_sizes_use_the_64_bit_header()
    {
        var big = new byte[16 + 10];
        BinaryPrimitives.WriteUInt32BigEndian(big, 1);
        Encoding.ASCII.GetBytes("mdat").CopyTo(big, 4);
        BinaryPrimitives.WriteUInt64BigEndian(big.AsSpan(8), (ulong)big.Length);
        using var ms = new MemoryStream([.. Box("ftyp", 8), .. big]);
        Mp4Structure s = Mp4Inspector.Inspect(ms);
        Assert.Equal(2, s.Boxes.Count);
        Assert.Equal(26, s.Boxes[1].Size);
        Assert.Null(s.Problem);
    }
}

/// <summary>Milestone V1 recording settings: owner defaults, validation, bit rates, hotkey conflicts, migration.</summary>
public class VideoSettingsTests
{
    [Fact]
    public void Defaults_follow_the_owner_decisions()
    {
        var v = new SnapForge.Core.Settings.VideoSettings();
        Assert.True(v.RecordSystemAudio);
        Assert.False(v.RecordMicrophone);
        Assert.True(v.CaptureCursor);
        Assert.Equal(30, v.FramesPerSecond);
        Assert.True(v.Countdown);
        Assert.Equal(3, v.CountdownSeconds);
        Assert.Equal(SnapForge.Core.Settings.VideoQuality.Standard, v.Quality);
        Assert.Equal("Shift+F9", v.RecordHotkey!.ToString()); // VID-06: start / pause / resume
        Assert.Equal("Shift+F10", v.StopHotkey!.ToString()); // VID-06: stop
        Assert.True(v.SaveToLibrary);
    }

    [Fact]
    public void Old_settings_file_without_video_gets_defaults_and_bad_values_are_repaired()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("settings.json");
        File.WriteAllText(path, """{ "Version": 1, "ShowRecentTray": false }""");
        SnapForge.Core.Settings.AppSettings s = SnapForge.Core.Settings.AppSettings.Load(path);
        Assert.False(s.ShowRecentTray);
        Assert.Equal(new SnapForge.Core.Settings.VideoSettings(), s.Video);

        File.WriteAllText(path, """{ "Version": 1, "Video": { "FramesPerSecond": 144, "CountdownSeconds": 7, "MinimumFreeSpaceMegabytes": 1 } }""");
        s = SnapForge.Core.Settings.AppSettings.Load(path);
        Assert.Equal(30, s.Video.FramesPerSecond);
        Assert.Equal(3, s.Video.CountdownSeconds);
        Assert.Equal(100, s.Video.MinimumFreeSpaceMegabytes);

        s = s with { Video = s.Video with { FramesPerSecond = 60, RecordMicrophone = true } };
        s.Save(path);
        Assert.Equal(60, SnapForge.Core.Settings.AppSettings.Load(path).Video.FramesPerSecond);
        Assert.True(SnapForge.Core.Settings.AppSettings.Load(path).Video.RecordMicrophone);
    }

    [Theory]
    [InlineData(1920, 1080, 30, SnapForge.Core.Settings.VideoQuality.Standard, 5_000_000)]
    [InlineData(1920, 1080, 30, SnapForge.Core.Settings.VideoQuality.High, 10_000_000)]
    [InlineData(2560, 1440, 60, SnapForge.Core.Settings.VideoQuality.Standard, 17_700_000)]
    [InlineData(3840, 2160, 60, SnapForge.Core.Settings.VideoQuality.High, 79_600_000)]
    [InlineData(320, 240, 30, SnapForge.Core.Settings.VideoQuality.Standard, 1_000_000)]
    public void Bit_rate_scales_with_pixels_frame_rate_and_quality(int w, int h, int fps, SnapForge.Core.Settings.VideoQuality q, int expected) =>
        Assert.Equal(expected, SnapForge.Core.Settings.VideoSettings.BitRate(w, h, fps, q));

    [Fact]
    public void Recording_hotkeys_take_part_in_conflict_detection()
    {
        var s = new SnapForge.Core.Settings.AppSettings();
        s = s with { CaptureHotkey = s.Video.RecordHotkey };
        var conflicts = SnapForge.Core.Settings.HotkeyGesture.FindConflicts(s.HotkeyAssignments());
        Assert.Contains(conflicts, c => c.Contains("Capture") && c.Contains("Record / pause / resume"));
    }
}

/// <summary>Mp4Timing parses fragment timing boxes exactly (hand-built fMP4).</summary>
public class Mp4TimingTests
{
    private static byte[] Box(string type, params byte[][] parts)
    {
        int len = 8 + parts.Sum(p => p.Length);
        var b = new byte[len];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b, (uint)len);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(b, 4);
        int o = 8;
        foreach (byte[] p in parts)
        {
            p.CopyTo(b, o);
            o += p.Length;
        }

        return b;
    }

    private static byte[] U32(params uint[] v)
    {
        var b = new byte[v.Length * 4];
        for (int i = 0; i < v.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(i * 4), v[i]);
        }

        return b;
    }

    private static byte[] Fragment(uint track, ulong baseTime, uint[] durations)
    {
        byte[] tfhd = Box("tfhd", U32(0x020000, track));
        var tfdtPayload = new byte[12];
        tfdtPayload[0] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(tfdtPayload.AsSpan(4), baseTime);
        byte[] tfdt = Box("tfdt", tfdtPayload);
        byte[] trun = Box("trun", U32(0x000100, (uint)durations.Length), U32(durations));
        return [.. Box("moof", Box("traf", tfhd, tfdt, trun)), .. Box("mdat", new byte[16])];
    }

    [Fact]
    public void Reads_tracks_and_fragment_times_and_finds_discontinuities()
    {
        byte[] tkhd = Box("tkhd", U32(0, 0, 0, 1)); // version 0: track id at payload offset 12
        byte[] mdhd = Box("mdhd", U32(0, 0, 0, 90000));
        byte[] hdlr = Box("hdlr", U32(0, 0), System.Text.Encoding.ASCII.GetBytes("vide"), new byte[12]);
        byte[] moov = Box("moov", Box("trak", tkhd, Box("mdia", mdhd, hdlr)));
        byte[] file = [.. Box("ftyp", new byte[8]), .. moov, .. Fragment(1, 0, [3000, 3000, 3000]), .. Fragment(1, 9000, [3000, 3000]), .. Fragment(1, 18000, [3000])];
        using var ms = new MemoryStream(file);
        Mp4TimingReport r = Mp4Timing.Read(ms);
        Assert.Equal(("vide", 90000u), r.Tracks[1]);
        Assert.Equal(3, r.Fragments.Count);
        Assert.Equal((0L, 3, 9000L), (r.Fragments[0].BaseDecodeTime, r.Fragments[0].Samples, r.Fragments[0].TotalDuration));
        var gap = Assert.Single(r.Discontinuities(1));
        Assert.Equal((2, 15000L, 18000L), gap); // fragment 2 should start at 9000+6000
    }
}

/// <summary>Fragmented → progressive MP4 conversion keeps every sample byte, duration, sync flag and the interleaving.</summary>
public class Mp4DefragmenterTests
{
    private static byte[] B(string type, params byte[][] parts) => Mp4Defragmenter.Box(type, parts);

    private static byte[] U(params uint[] v) => Mp4Defragmenter.U32(v);

    private static byte[] Trak(uint id, uint timescale, string handler) =>
        B("trak",
            B("tkhd", U(0, 0, 0, id, 0, 0), new byte[60]),
            B("mdia",
                B("mdhd", U(0, 0, 0, timescale, 0), new byte[4]),
                B("hdlr", U(0, 0), System.Text.Encoding.ASCII.GetBytes(handler), new byte[13]),
                B("minf", B("dinf", new byte[4]), B("stbl", B("stsd", U(0, 1), B("test", new byte[8])), B("stts", U(0, 0)), B("stsc", U(0, 0)), B("stsz", U(0, 0, 0)), B("stco", U(0, 0))))));

    /// <summary>
    /// moof with one traf per track (default-base-is-moof), sample data in the mdat right after it. Video samples are
    /// filled with 0xA0+n, audio with 0x10+n so the copy can be checked byte for byte.
    /// </summary>
    private static byte[] Fragment(int seq, (uint Track, uint[] Sizes, uint Duration, bool FirstSync)[] trafs, ref byte fill, List<(uint Track, byte[] Data)> expected)
    {
        var payloads = new List<byte[]>();
        foreach (var t in trafs)
        {
            foreach (uint size in t.Sizes)
            {
                byte[] d = new byte[size];
                Array.Fill(d, fill++);
                payloads.Add(d);
                expected.Add((t.Track, d));
            }
        }

        // First pass to know the moof size, then fill data offsets.
        byte[] Build(int moofSize)
        {
            var trafBoxes = new List<byte[]>();
            int dataOffset = moofSize + 8;
            foreach (var t in trafs)
            {
                byte[] tfhd = B("tfhd", U(0x020008, t.Track, t.Duration));
                uint flags = 0x000001 | 0x000004 | 0x000200 | 0x000400;
                var entries = new List<byte[]>();
                for (int i = 0; i < t.Sizes.Length; i++)
                {
                    bool sync = t.Track == 2 || (i == 0 && t.FirstSync); // audio: every sample is a sync sample
                    entries.Add(U(t.Sizes[i], sync ? 0x02000000u : 0x01010000u));
                }

                byte[] trun = B("trun", U(flags, (uint)t.Sizes.Length, (uint)dataOffset, t.FirstSync ? 0x02000000u : 0x01010000u), [.. entries.SelectMany(e => e)]);
                trafBoxes.Add(B("traf", tfhd, trun));
                dataOffset += (int)t.Sizes.Sum(x => x);
            }

            return B("moof", [B("mfhd", U(0, (uint)seq)), .. trafBoxes]);
        }

        int size0 = Build(0).Length;
        byte[] moof = Build(size0);
        return [.. moof, .. B("mdat", [.. payloads.SelectMany(p => p)])];
    }

    /// <summary>A small valid two-track fragmented MP4 (used by the Library tests too).</summary>
    public static byte[] SampleFragmentedFile() => BuildSample(out _);

    private static byte[] BuildSample(out List<(uint Track, byte[] Data)> expected)
    {
        byte fill = 1;
        expected = [];
        byte[] moov = B("moov", B("mvhd", U(0, 0, 0, 1000, 0), new byte[80]), Trak(1, 30000, "vide"), Trak(2, 48000, "soun"),
            B("mvex", B("trex", U(0, 1, 1, 1000, 0, 0x01010000)), B("trex", U(0, 2, 1, 1024, 0, 0))));
        return [.. B("ftyp", System.Text.Encoding.ASCII.GetBytes("iso5"), U(0)), .. moov,
            .. Fragment(1, [(1, [500, 120, 130], 1000, true), (2, [40, 41, 42], 1024, true)], ref fill, expected),
            .. Fragment(2, [(1, [480, 110], 1000, true), (2, [43, 44], 1024, true)], ref fill, expected),
            .. B("mfra", new byte[8])];
    }

    [Fact]
    public void Converts_two_track_fragments_to_a_progressive_file_with_exact_samples()
    {
        byte fill = 1;
        var expected = new List<(uint Track, byte[] Data)>();
        byte[] moov = B("moov", B("mvhd", U(0, 0, 0, 1000, 0), new byte[80]), Trak(1, 30000, "vide"), Trak(2, 48000, "soun"),
            B("mvex", B("trex", U(0, 1, 1, 1000, 0, 0x01010000)), B("trex", U(0, 2, 1, 1024, 0, 0))));
        byte[] file = [.. B("ftyp", System.Text.Encoding.ASCII.GetBytes("iso5"), U(0)), .. moov,
            .. Fragment(1, [(1, [500, 120, 130], 1000, true), (2, [40, 41, 42], 1024, true)], ref fill, expected),
            .. Fragment(2, [(1, [480, 110], 1000, true), (2, [43, 44], 1024, true)], ref fill, expected),
            .. B("mfra", new byte[8])];

        using var input = new MemoryStream(file);
        using var output = new MemoryStream();
        Mp4Defragmenter.Result r = Mp4Defragmenter.Convert(input, output);
        Assert.Equal(2, r.Tracks);
        Assert.Equal(10, r.Samples);
        Assert.Equal(166, r.DurationsMs[1]); // 5 frames × 1000/30000 s
        Assert.Equal(106, r.DurationsMs[2]); // 5 × 1024/48000 s

        byte[] o = output.ToArray();
        Mp4Structure s = Mp4Inspector.Inspect(new MemoryStream(o));
        Assert.Equal(["ftyp", "moov", "mdat"], s.Boxes.Select(b => b.Type));
        Assert.True(s.IsComplete);
        Assert.False(s.IsFragmented);

        // Read the sample tables back and check every sample's bytes.
        Dictionary<uint, (uint[] Sizes, long[] Offsets, uint[] Stts, uint[]? Stss)> tables = ReadTables(o);
        foreach (uint track in new uint[] { 1, 2 })
        {
            (uint[] sizes, long[] offsets, uint[] stts, uint[]? stss) = tables[track];
            byte[][] mine = [.. expected.Where(e => e.Track == track).Select(e => e.Data)];
            Assert.Equal(mine.Length, sizes.Length);
            long pos = offsets[0];
            int chunk = 0;
            for (int i = 0; i < mine.Length; i++)
            {
                // chunks: one per trun (3 then 2 samples for video, 3 then 2 for audio)
                if (i == 3)
                {
                    chunk = 1;
                    pos = offsets[1];
                }

                Assert.Equal(mine[i], o.AsSpan((int)pos, (int)sizes[i]).ToArray());
                pos += sizes[i];
            }

            Assert.Equal(track == 1 ? new uint[] { 5, 1000 } : new uint[] { 5, 1024 }, stts);
            Assert.Equal(1, chunk);
            if (track == 1)
            {
                Assert.Equal(new uint[] { 1, 4 }, stss); // sync samples: first of each fragment
            }
            else
            {
                Assert.Null(stss); // all audio samples are sync
            }
        }

        // Interleaving kept: video chunk 1 < audio chunk 1 < video chunk 2 < audio chunk 2.
        Assert.True(tables[1].Offsets[0] < tables[2].Offsets[0] && tables[2].Offsets[0] < tables[1].Offsets[1] && tables[1].Offsets[1] < tables[2].Offsets[1]);
    }

    [Fact]
    public void Rejects_non_fragmented_and_damaged_input_without_writing_garbage()
    {
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => Mp4Defragmenter.Convert(new MemoryStream([.. B("ftyp", new byte[8]), .. B("moov", new byte[8]), .. B("mdat", new byte[8])]), output));
        Assert.Equal(0, output.Length);
    }

    private static Dictionary<uint, (uint[] Sizes, long[] Offsets, uint[] Stts, uint[]? Stss)> ReadTables(byte[] f)
    {
        var result = new Dictionary<uint, (uint[], long[], uint[], uint[]?)>();
        int moov = 0;
        while (System.Text.Encoding.ASCII.GetString(f, moov + 4, 4) != "moov")
        {
            moov += (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(moov));
        }

        foreach ((string t, int s, int e) in Kids(f, moov + 8, moov + (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(moov))))
        {
            if (t != "trak")
            {
                continue;
            }

            uint id = 0;
            uint[] sizes = [], stts = [];
            uint[]? stss = null;
            long[] offsets = [];
            void Walk(int a, int b)
            {
                foreach ((string t2, int s2, int e2) in Kids(f, a, b))
                {
                    uint U32(int at) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(at));
                    switch (t2)
                    {
                        case "tkhd": id = U32(s2 + 12); break;
                        case "mdia" or "minf" or "stbl": Walk(s2, e2); break;
                        case "stsz": sizes = [.. Enumerable.Range(0, (int)U32(s2 + 8)).Select(i => U32(s2 + 12 + (i * 4)))]; break;
                        case "stts": stts = [.. Enumerable.Range(0, (int)U32(s2 + 4) * 2).Select(i => U32(s2 + 8 + (i * 4)))]; break;
                        case "stss": stss = [.. Enumerable.Range(0, (int)U32(s2 + 4)).Select(i => U32(s2 + 8 + (i * 4)))]; break;
                        case "co64": offsets = [.. Enumerable.Range(0, (int)U32(s2 + 4)).Select(i => (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(f.AsSpan(s2 + 8 + (i * 8))))]; break;
                    }
                }
            }

            Walk(s, e);
            result[id] = (sizes, offsets, stts, stss);
        }

        return result;
    }

    private static IEnumerable<(string, int, int)> Kids(byte[] f, int a, int b)
    {
        int p = a;
        while (p + 8 <= b)
        {
            int size = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(p));
            yield return (System.Text.Encoding.ASCII.GetString(f, p + 4, 4), p + 8, p + size);
            p += size;
        }
    }
}
