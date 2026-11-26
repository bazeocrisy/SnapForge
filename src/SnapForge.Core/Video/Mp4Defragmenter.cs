using System.Buffers.Binary;
using System.Text;

namespace SnapForge.Core.Video;

/// <summary>
/// Converts a fragmented MP4 (as written while recording, for crash safety) into a standard progressive MP4 with
/// complete sample tables (stts/stsz/stsc/co64/stss/ctts) and the index at the front. No re-encoding: compressed
/// samples are copied byte for byte. Why: the Windows media reader mis-times fragments that carry no explicit start
/// time (tfdt), which Media Foundation's writer omits — measured as up to 300 ms A/V offset on CI. Progressive MP4 is
/// read identically by every player and editor.
/// </summary>
public static class Mp4Defragmenter
{
    private sealed class Sample
    {
        public long Position;
        public uint Size;
        public uint Duration;
        public bool Sync;
        public int Cto;
    }

    private sealed class Chunk(long position, int samples)
    {
        public long Position { get; } = position;

        public int Samples { get; } = samples;

        public long NewOffset { get; set; }

        public long Bytes { get; set; }
    }

    private sealed class Track
    {
        public uint Id;
        public byte[] Trak = [];
        public uint Timescale;
        public (uint Index, uint Duration, uint Size, uint Flags) Defaults;
        public List<Sample> Samples { get; } = [];
        public List<Chunk> Chunks { get; } = [];
    }

    /// <summary>Result of a conversion.</summary>
    public sealed record Result(int Tracks, long Samples, long BytesWritten, IReadOnlyDictionary<uint, long> DurationsMs);

    /// <summary>
    /// Writes <paramref name="output"/> from the fragmented <paramref name="input"/>. Throws <see cref="InvalidDataException"/>
    /// if the input is not a fragmented MP4 this converter understands; the input is never modified.
    /// </summary>
    public static Result Convert(string input, string output)
    {
        using var src = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var dst = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        Result r = Convert(src, dst);
        dst.Flush(flushToDisk: true);
        return r;
    }

    public static Result Convert(Stream src, Stream dst)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(dst);
        Mp4Structure structure = Mp4Inspector.Inspect(src);
        if (!structure.HasMovie || !structure.IsFragmented)
        {
            throw new InvalidDataException("Not a fragmented MP4.");
        }

        byte[]? moov = null;
        var tracks = new Dictionary<uint, Track>();
        foreach (Mp4Box box in structure.Boxes)
        {
            if (box.Offset + box.Size > src.Length)
            {
                break; // a cut-off box at the end (recovered files are already trimmed)
            }

            if (box.Type == "moov")
            {
                moov = Read(src, box);
                ParseMoov(moov, tracks);
            }
            else if (box.Type == "moof")
            {
                if (box.Size > 64L * 1024 * 1024)
                {
                    throw new InvalidDataException("Fragment header too large.");
                }

                ParseMoof(Read(src, box), box.Offset, tracks);
            }
        }

        if (moov is null || tracks.Count == 0)
        {
            throw new InvalidDataException("The MP4 has no tracks.");
        }

        foreach (Track t in tracks.Values)
        {
            foreach (Chunk c in t.Chunks)
            {
                if (c.Position < 0 || c.Position + c.Bytes > src.Length)
                {
                    throw new InvalidDataException("Sample data lies outside the file.");
                }
            }
        }

        // Lay out media data in original file order (keeps audio/video interleaving), then build the index.
        List<Chunk> ordered = [.. tracks.Values.SelectMany(t => t.Chunks).OrderBy(c => c.Position)];
        long mediaBytes = ordered.Sum(c => c.Bytes);
        byte[] ftyp = Box("ftyp", Ascii("isom"), U32(0x200), Ascii("isom"), Ascii("iso2"), Ascii("avc1"), Ascii("mp41"));
        bool large = mediaBytes + 16 > uint.MaxValue;
        int mdatHeader = large ? 16 : 8;

        // Offsets depend on the moov size, which depends on the offsets only through co64 (fixed 8 bytes each),
        // so one sizing pass is exact.
        byte[] newMoov = BuildMoov(moov, tracks);
        long dataStart = ftyp.Length + newMoov.Length + mdatHeader;
        long cursor = dataStart;
        foreach (Chunk c in ordered)
        {
            c.NewOffset = cursor;
            cursor += c.Bytes;
        }

        newMoov = BuildMoov(moov, tracks);
        if (ftyp.Length + newMoov.Length + mdatHeader != dataStart)
        {
            throw new InvalidOperationException("MP4 index size changed between passes.");
        }

        dst.Write(ftyp);
        dst.Write(newMoov);
        Span<byte> header = stackalloc byte[16];
        if (large)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, 1);
            Encoding.ASCII.GetBytes("mdat", header[4..8]);
            BinaryPrimitives.WriteUInt64BigEndian(header[8..], (ulong)(mediaBytes + 16));
            dst.Write(header);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(mediaBytes + 8));
            Encoding.ASCII.GetBytes("mdat", header[4..8]);
            dst.Write(header[..8]);
        }

        byte[] buffer = new byte[1 << 20];
        foreach (Chunk c in ordered)
        {
            src.Position = c.Position;
            long left = c.Bytes;
            while (left > 0)
            {
                int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n <= 0)
                {
                    throw new EndOfStreamException();
                }

                dst.Write(buffer, 0, n);
                left -= n;
            }
        }

        var durations = tracks.Values.ToDictionary(t => t.Id, t => t.Timescale == 0 ? 0 : t.Samples.Sum(s => (long)s.Duration) * 1000 / t.Timescale);
        return new Result(tracks.Count, tracks.Values.Sum(t => (long)t.Samples.Count), dst.Position, durations);
    }

    // ------------------------------------------------------------------ parsing

    private static void ParseMoov(byte[] moov, Dictionary<uint, Track> tracks)
    {
        foreach ((string type, int start, int end, int boxStart) in Children(moov, 8, moov.Length))
        {
            if (type == "trak")
            {
                var t = new Track { Trak = moov[boxStart..end] };
                foreach ((string t2, int s2, int e2, _) in Children(moov, start, end))
                {
                    if (t2 == "tkhd")
                    {
                        t.Id = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + (moov[s2] == 1 ? 20 : 12)));
                    }
                    else if (t2 == "mdia")
                    {
                        foreach ((string t3, int s3, _, _) in Children(moov, s2, e2))
                        {
                            if (t3 == "mdhd")
                            {
                                t.Timescale = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s3 + (moov[s3] == 1 ? 20 : 12)));
                            }
                        }
                    }
                }

                tracks[t.Id] = t;
            }
        }

        foreach ((string type, int start, int end, _) in Children(moov, 8, moov.Length))
        {
            if (type != "mvex")
            {
                continue;
            }

            foreach ((string t2, int s2, int e2, _) in Children(moov, start, end))
            {
                if (t2 == "trex" && e2 - s2 >= 24)
                {
                    uint id = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + 4));
                    if (tracks.TryGetValue(id, out Track? t))
                    {
                        t.Defaults = (BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + 8)), BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + 12)),
                            BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + 16)), BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + 20)));
                    }
                }
            }
        }
    }

    private static void ParseMoof(byte[] moof, long moofOffset, Dictionary<uint, Track> tracks)
    {
        foreach ((string type, int start, int end, _) in Children(moof, 8, moof.Length))
        {
            if (type != "traf")
            {
                continue;
            }

            Track? track = null;
            long baseOffset = moofOffset;
            uint duration = 0, size = 0, flags = 0;
            foreach ((string t2, int s, int e, _) in Children(moof, start, end))
            {
                if (t2 == "tfhd")
                {
                    uint tf = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(s)) & 0xFFFFFF;
                    uint id = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(s + 4));
                    if (!tracks.TryGetValue(id, out track))
                    {
                        throw new InvalidDataException($"Fragment for unknown track {id}.");
                    }

                    (_, duration, size, flags) = track.Defaults;
                    int p = s + 8;
                    if ((tf & 0x1) != 0)
                    {
                        baseOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(moof.AsSpan(p));
                        p += 8;
                    }

                    if ((tf & 0x2) != 0)
                    {
                        p += 4;
                    }

                    if ((tf & 0x8) != 0)
                    {
                        duration = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p));
                        p += 4;
                    }

                    if ((tf & 0x10) != 0)
                    {
                        size = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p));
                        p += 4;
                    }

                    if ((tf & 0x20) != 0)
                    {
                        flags = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p));
                    }
                }
                else if (t2 == "trun" && track is not null)
                {
                    int version = moof[s];
                    uint rf = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(s)) & 0xFFFFFF;
                    int count = (int)BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(s + 4));
                    if (count < 0 || count > 10_000_000)
                    {
                        throw new InvalidDataException("Implausible sample count.");
                    }

                    int p = s + 8;
                    long dataOffset = 0;
                    if ((rf & 0x1) != 0)
                    {
                        dataOffset = BinaryPrimitives.ReadInt32BigEndian(moof.AsSpan(p));
                        p += 4;
                    }

                    uint? firstFlags = null;
                    if ((rf & 0x4) != 0)
                    {
                        firstFlags = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p));
                        p += 4;
                    }

                    long position = baseOffset + dataOffset;
                    var chunk = new Chunk(position, count);
                    long bytes = 0;
                    for (int i = 0; i < count; i++)
                    {
                        if (p > e)
                        {
                            throw new InvalidDataException("Truncated sample table.");
                        }

                        var sample = new Sample { Duration = duration, Size = size };
                        uint sf = i == 0 && firstFlags is { } ff ? ff : flags;
                        if ((rf & 0x100) != 0)
                        {
                            sample.Duration = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p));
                            p += 4;
                        }

                        if ((rf & 0x200) != 0)
                        {
                            sample.Size = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p));
                            p += 4;
                        }

                        if ((rf & 0x400) != 0)
                        {
                            sf = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p));
                            p += 4;
                        }

                        if ((rf & 0x800) != 0)
                        {
                            sample.Cto = version == 0 ? (int)BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p)) : BinaryPrimitives.ReadInt32BigEndian(moof.AsSpan(p));
                            p += 4;
                        }

                        sample.Sync = (sf & 0x00010000) == 0; // sample_is_non_sync_sample clear
                        sample.Position = position + bytes;
                        bytes += sample.Size;
                        track.Samples.Add(sample);
                    }

                    chunk.Bytes = bytes;
                    if (count > 0)
                    {
                        track.Chunks.Add(chunk);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ writing

    private static byte[] BuildMoov(byte[] moov, Dictionary<uint, Track> tracks)
    {
        uint movieTimescale = 1000;
        var children = new List<byte[]>();
        long movieDuration = 0;
        foreach ((string type, int start, int end, int boxStart) in Children(moov, 8, moov.Length))
        {
            if (type == "mvhd")
            {
                movieTimescale = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(start + (moov[start] == 1 ? 20 : 12)));
            }
        }

        foreach (Track t in tracks.Values)
        {
            long media = t.Samples.Sum(s => (long)s.Duration);
            movieDuration = Math.Max(movieDuration, t.Timescale == 0 ? 0 : media * movieTimescale / t.Timescale);
        }

        foreach ((string type, int start, int end, int boxStart) in Children(moov, 8, moov.Length))
        {
            switch (type)
            {
                case "mvhd":
                    children.Add(PatchDuration(moov[boxStart..end], movieDuration));
                    break;
                case "trak":
                    uint id = 0;
                    foreach ((string t2, int s2, _, _) in Children(moov, start, end))
                    {
                        if (t2 == "tkhd")
                        {
                            id = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + (moov[s2] == 1 ? 20 : 12)));
                        }
                    }

                    children.Add(BuildTrak(tracks[id], movieTimescale));
                    break;
                case "mvex":
                    break; // fragment defaults: not used by progressive files
                default:
                    children.Add(moov[boxStart..end]);
                    break;
            }
        }

        return Box("moov", [.. children]);
    }

    private static byte[] BuildTrak(Track t, uint movieTimescale)
    {
        byte[] trak = t.Trak;
        long media = t.Samples.Sum(s => (long)s.Duration);
        long movie = t.Timescale == 0 ? 0 : media * movieTimescale / t.Timescale;
        var children = new List<byte[]>();
        foreach ((string type, int start, int end, int boxStart) in Children(trak, 8, trak.Length))
        {
            if (type == "tkhd")
            {
                children.Add(PatchTkhd(trak[boxStart..end], movie));
            }
            else if (type == "edts")
            {
                // Edit lists written for fragments are dropped; samples start at 0.
            }
            else if (type == "mdia")
            {
                var mdia = new List<byte[]>();
                foreach ((string t2, int s2, int e2, int b2) in Children(trak, start, end))
                {
                    if (t2 == "mdhd")
                    {
                        mdia.Add(PatchDuration(trak[b2..e2], media));
                    }
                    else if (t2 == "minf")
                    {
                        var minf = new List<byte[]>();
                        foreach ((string t3, int s3, int e3, int b3) in Children(trak, s2, e2))
                        {
                            minf.Add(t3 == "stbl" ? BuildStbl(trak, s3, e3, t) : trak[b3..e3]);
                        }

                        mdia.Add(Box("minf", [.. minf]));
                    }
                    else
                    {
                        mdia.Add(trak[b2..e2]);
                    }
                }

                children.Add(Box("mdia", [.. mdia]));
            }
            else
            {
                children.Add(trak[boxStart..end]);
            }
        }

        return Box("trak", [.. children]);
    }

    private static byte[] BuildStbl(byte[] b, int start, int end, Track t)
    {
        byte[]? stsd = null;
        foreach ((string type, int s, int e, int bs) in Children(b, start, end))
        {
            if (type == "stsd")
            {
                stsd = b[bs..e];
            }
        }

        if (stsd is null)
        {
            throw new InvalidDataException("Track has no sample description.");
        }

        // stts: run-length sample durations.
        var stts = new List<(uint Count, uint Delta)>();
        foreach (Sample s in t.Samples)
        {
            if (stts.Count > 0 && stts[^1].Delta == s.Duration)
            {
                stts[^1] = (stts[^1].Count + 1, s.Duration);
            }
            else
            {
                stts.Add((1, s.Duration));
            }
        }

        var parts = new List<byte[]> { stsd, FullBox("stts", 0, 0, U32((uint)stts.Count), [.. stts.SelectMany(x => U32(x.Count, x.Delta))]) };

        if (t.Samples.Any(s => s.Cto != 0))
        {
            var ctts = new List<(uint Count, int Offset)>();
            foreach (Sample s in t.Samples)
            {
                if (ctts.Count > 0 && ctts[^1].Offset == s.Cto)
                {
                    ctts[^1] = (ctts[^1].Count + 1, s.Cto);
                }
                else
                {
                    ctts.Add((1, s.Cto));
                }
            }

            parts.Add(FullBox("ctts", 1, 0, U32((uint)ctts.Count), [.. ctts.SelectMany(x => U32(x.Count, unchecked((uint)x.Offset)))]));
        }

        if (t.Samples.Any(s => !s.Sync))
        {
            uint[] sync = [.. t.Samples.Select((s, i) => (s, i)).Where(x => x.s.Sync).Select(x => (uint)(x.i + 1))];
            parts.Add(FullBox("stss", 0, 0, U32((uint)sync.Length), U32(sync)));
        }

        // stsc: run-length samples-per-chunk.
        var stsc = new List<(uint First, uint PerChunk)>();
        for (int i = 0; i < t.Chunks.Count; i++)
        {
            uint n = (uint)t.Chunks[i].Samples;
            if (stsc.Count == 0 || stsc[^1].PerChunk != n)
            {
                stsc.Add(((uint)(i + 1), n));
            }
        }

        parts.Add(FullBox("stsc", 0, 0, U32((uint)stsc.Count), [.. stsc.SelectMany(x => U32(x.First, x.PerChunk, 1))]));
        parts.Add(FullBox("stsz", 0, 0, U32(0, (uint)t.Samples.Count), [.. t.Samples.SelectMany(s => U32(s.Size))]));
        byte[] co64 = new byte[8 * t.Chunks.Count];
        for (int i = 0; i < t.Chunks.Count; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(co64.AsSpan(i * 8), (ulong)t.Chunks[i].NewOffset);
        }

        parts.Add(FullBox("co64", 0, 0, U32((uint)t.Chunks.Count), co64));
        return Box("stbl", [.. parts]);
    }

    /// <summary>mvhd/mdhd: duration after creation/modification times and timescale.</summary>
    private static byte[] PatchDuration(byte[] box, long duration)
    {
        byte[] copy = (byte[])box.Clone();
        int payload = 8;
        if (copy[payload] == 1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(copy.AsSpan(payload + 4 + 8 + 8 + 4), (ulong)duration);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(payload + 4 + 4 + 4 + 4), (uint)Math.Min(duration, uint.MaxValue));
        }

        return copy;
    }

    private static byte[] PatchTkhd(byte[] box, long duration)
    {
        byte[] copy = (byte[])box.Clone();
        int payload = 8;
        if (copy[payload] == 1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(copy.AsSpan(payload + 4 + 8 + 8 + 4 + 4), (ulong)duration);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(payload + 4 + 4 + 4 + 4 + 4), (uint)Math.Min(duration, uint.MaxValue));
        }

        return copy;
    }

    // ------------------------------------------------------------------ box helpers

    private static byte[] Read(Stream s, Mp4Box box)
    {
        var b = new byte[box.Size];
        s.Position = box.Offset;
        s.ReadExactly(b);
        return b;
    }

    internal static byte[] Box(string type, params byte[][] parts)
    {
        long len = 8 + parts.Sum(p => (long)p.Length);
        var b = new byte[len];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)len);
        Encoding.ASCII.GetBytes(type).CopyTo(b, 4);
        int o = 8;
        foreach (byte[] p in parts)
        {
            p.CopyTo(b, o);
            o += p.Length;
        }

        return b;
    }

    private static byte[] FullBox(string type, byte version, uint flags, params byte[][] parts) =>
        Box(type, [U32(((uint)version << 24) | flags), .. parts]);

    internal static byte[] U32(params uint[] v)
    {
        var b = new byte[v.Length * 4];
        for (int i = 0; i < v.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(i * 4), v[i]);
        }

        return b;
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>Child boxes in [start, end): (type, payload start, end, box start).</summary>
    private static IEnumerable<(string Type, int Start, int End, int BoxStart)> Children(byte[] b, int start, int end)
    {
        int p = start;
        while (p + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p));
            string type = Encoding.ASCII.GetString(b, p + 4, 4);
            int header = 8;
            if (size == 1 && p + 16 <= end)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(p + 8));
                header = 16;
            }
            else if (size == 0)
            {
                size = end - p;
            }

            if (size < header || p + size > end)
            {
                yield break;
            }

            yield return (type, p + header, (int)(p + size), p);
            p += (int)size;
        }
    }
}
