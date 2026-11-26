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
