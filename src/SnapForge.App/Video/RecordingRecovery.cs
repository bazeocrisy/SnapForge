using System.IO;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Library;
using SnapForge.Core.Video;

namespace SnapForge.App.Video;

/// <summary>Outcome of recovering one interrupted recording.</summary>
internal sealed record RecoveryOutcome(Guid Id, bool Recovered, long DurationMs, long BytesBefore, long BytesAfter, int Fragments, string Message);

/// <summary>
/// Recovers recordings interrupted by a crash, forced close, power loss or full disk (VID-R04). Runs at start-up.
/// A fragmented MP4 is cut back to its last complete fragment and checked by actually reading it with the Windows
/// decoder; only then is it added to the Library, marked "recovered". A file that cannot be made playable is
/// reported and left untouched in its folder — SnapForge never claims a recovery it did not verify.
/// </summary>
internal static class RecordingRecovery
{
    public static IReadOnlyList<RecoveryOutcome> RecoverAll(CaptureLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var outcomes = new List<RecoveryOutcome>();
        foreach (InterruptedRecording r in library.InterruptedRecordings())
        {
            outcomes.Add(Recover(library, r));
        }

        return outcomes;
    }

    private static RecoveryOutcome Recover(CaptureLibrary library, InterruptedRecording r)
    {
        if (!File.Exists(r.VideoPath) || r.Bytes == 0)
        {
            library.AbandonVideo(r.Id); // nothing was written: nothing to keep
            return new RecoveryOutcome(r.Id, false, 0, 0, 0, 0, "The recording had not written any video yet; nothing could be kept.");
        }

        Mp4Structure? s;
        try
        {
            // A finished progressive file (stopped between conversion and manifest) needs no repair.
            Mp4Structure current = Mp4Inspector.Inspect(r.VideoPath);
            s = current is { IsComplete: true, IsFragmented: false } ? current : Mp4Inspector.Recover(r.VideoPath);
        }
        catch (IOException ex)
        {
            return new RecoveryOutcome(r.Id, false, 0, r.Bytes, r.Bytes, 0, "The interrupted recording could not be read: " + ex.Message);
        }

        if (s is null)
        {
            return new RecoveryOutcome(r.Id, false, 0, r.Bytes, r.Bytes, 0, "The interrupted recording does not contain a complete part that can be played. The file was left in place.");
        }

        // Verify by decoding, not just by structure.
        (long durationMs, int width, int height, bool audio, int videoSamples) = Probe(r.VideoPath);
        if (videoSamples == 0)
        {
            return new RecoveryOutcome(r.Id, false, 0, r.Bytes, s.FileLength, s.CompleteFragments, "The recovered file could not be played back. The file was left in place.");
        }

        int fps = durationMs > 0 ? (int)Math.Round(videoSamples * 1000.0 / durationMs) : 30;
        var info = new VideoInfo(width, height, fps is 25 or 30 or 50 or 60 ? fps : Math.Clamp(fps, 1, 240), durationMs, audio, Recovered: true);
        library.CompleteVideo(r.Id, info, VideoAnalysis.DecodeFrame(r.VideoPath), r.StartedUtc);
        return new RecoveryOutcome(r.Id, true, durationMs, r.Bytes, s.FileLength, s.CompleteFragments, $"Recovered {durationMs / 1000.0:0.0} s of an interrupted recording.");
    }

    /// <summary>
    /// Video duration of a fragmented MP4 from its own boxes (sum of the stored sample durations), or null if the
    /// file is not fragmented. Media Foundation's writer omits the fragments' start time (tfdt) and the Windows reader
    /// then mis-times later fragments — decoded timestamps jump by 100–230 ms at a fragment boundary although every
    /// stored duration is exact (CI runs 96–106; see Mp4Defragmenter). The stored durations are the truth.
    /// </summary>
    internal static long? StoredFragmentedDurationMs(string path)
    {
        try
        {
            if (!Mp4Inspector.Inspect(path).IsFragmented)
            {
                return null;
            }

            Mp4TimingReport t = Mp4Timing.Read(path);
            foreach ((uint id, (string handler, uint timescale)) in t.Tracks)
            {
                if (handler == "vide" && timescale > 0)
                {
                    long total = t.Fragments.Where(f => f.TrackId == id).Sum(f => f.TotalDuration);
                    return total > 0 ? total * 1000 / timescale : null;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Duration, size, audio presence and video sample count. Duration: from the stored sample durations for a
    /// fragmented file (see <see cref="StoredFragmentedDurationMs"/>), else from the last decodable video sample.
    /// </summary>
    internal static (long DurationMs, int Width, int Height, bool Audio, int VideoSamples) Probe(string path)
    {
        nint reader = Mf.CreateSourceReader(path, 0);
        try
        {
            nint type = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            ulong size = Mf.GetUInt64(type, Mf.MT_FRAME_SIZE) ?? 0;
            Com.Release(ref type);
            bool audio;
            try
            {
                nint a = Mf.GetCurrentMediaType(reader, Mf.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
                Com.Release(ref a);
                audio = true;
            }
            catch (NativeCallException)
            {
                audio = false;
            }

            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            Mf.SetStreamSelection(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
            int samples = 0;
            long last = 0;
            while (Mf.ReadSample(reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out long ts, out nint s) && s != 0)
            {
                samples++;
                last = ts;
                Com.Release(ref s);
            }

            long frame = samples > 1 ? last / Math.Max(1, samples - 1) : 0;
            long durationMs = StoredFragmentedDurationMs(path) ?? ((last + frame) / 10_000);
            return (durationMs, (int)(size >> 32), (int)(size & 0xFFFFFFFF), audio, samples);
        }
        finally
        {
            Com.Release(ref reader);
        }
    }
}
