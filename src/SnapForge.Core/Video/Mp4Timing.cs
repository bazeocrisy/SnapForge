using System.Buffers.Binary;

namespace SnapForge.Core.Video;

/// <summary>One track fragment (traf) as stored in the file.</summary>
public sealed record FragmentTiming(int Fragment, uint TrackId, long BaseDecodeTime, int Samples, long TotalDuration, long FirstCompositionOffset, long MinCompositionOffset, long MaxCompositionOffset);

/// <summary>Per-track timing of a fragmented MP4, read from the file's own boxes (not from a decoder).</summary>
public sealed record Mp4TimingReport(IReadOnlyDictionary<uint, (string Handler, uint Timescale)> Tracks, IReadOnlyList<FragmentTiming> Fragments)
{
    /// <summary>Fragments of a track whose base decode time does not continue exactly where the previous one ended.</summary>
    public IReadOnlyList<(int Fragment, long Expected, long Actual)> Discontinuities(uint track)
    {
        var list = new List<(int, long, long)>();
        long? next = null;
        foreach (FragmentTiming f in Fragments.Where(f => f.TrackId == track))
        {
            if (next is { } n && n != f.BaseDecodeTime)
            {
                list.Add((f.Fragment, n, f.BaseDecodeTime));
            }

            next = f.BaseDecodeTime + f.TotalDuration;
        }

        return list;
    }
}

/// <summary>
/// Reads moov/trak (track ids, handler, timescale), mvex/trex defaults and every moof/traf (tfhd, tfdt, trun) of a
/// fragmented MP4. Used to verify that the timestamps SnapForge writes are stored exactly, independent of how a
/// particular decoder interprets them. Bounds-checked; reads boxes one at a time (never the whole file).
/// </summary>
public static class Mp4Timing
{
    public static Mp4TimingReport Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Read(fs);
    }

    public static Mp4TimingReport Read(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var tracks = new Dictionary<uint, (string, uint)>();
        var defaults = new Dictionary<uint, uint>();
        var fragments = new List<FragmentTiming>();
        int fragmentIndex = 0;
        foreach (Mp4Box top in Mp4Inspector.Inspect(s).Boxes)
        {
            if (top.Offset + top.Size > s.Length)
            {
                break;
            }

            if (top.Type == "moov")
            {
                byte[] moov = ReadBox(s, top);
                foreach ((string type, int start, int end) in Children(moov, 8, moov.Length))
                {
                    if (type == "trak")
                    {
                        ParseTrak(moov, start, end, tracks);
                    }
                    else if (type == "mvex")
                    {
                        foreach ((string t2, int s2, int e2) in Children(moov, start, end))
                        {
                            if (t2 == "trex" && e2 - s2 >= 24)
                            {
                                uint id = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + 4));
                                defaults[id] = BinaryPrimitives.ReadUInt32BigEndian(moov.AsSpan(s2 + 12));
                            }
                        }
                    }
                }
            }
            else if (top.Type == "moof" && top.Size < 64 * 1024 * 1024)
            {
                byte[] moof = ReadBox(s, top);
                foreach ((string type, int start, int end) in Children(moof, 8, moof.Length))
                {
                    if (type == "traf")
                    {
                        fragments.Add(ParseTraf(moof, start, end, fragmentIndex, defaults));
                    }
                }

                fragmentIndex++;
            }
        }

        return new Mp4TimingReport(tracks, fragments);
    }

    private static void ParseTrak(byte[] b, int start, int end, Dictionary<uint, (string, uint)> tracks)
    {
        uint id = 0, timescale = 0;
        string handler = "?";
        foreach ((string type, int s, int e) in Children(b, start, end))
        {
            if (type == "tkhd")
            {
                int version = b[s];
                id = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(s + (version == 1 ? 20 : 12)));
            }
            else if (type == "mdia")
            {
                foreach ((string t2, int s2, int e2) in Children(b, s, e))
                {
                    if (t2 == "mdhd")
                    {
                        int version = b[s2];
                        timescale = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(s2 + (version == 1 ? 20 : 12)));
                    }
                    else if (t2 == "hdlr" && e2 - s2 >= 12)
                    {
                        handler = System.Text.Encoding.ASCII.GetString(b, s2 + 8, 4);
                    }
                }
            }
        }

        tracks[id] = (handler, timescale);
    }

    private static FragmentTiming ParseTraf(byte[] b, int start, int end, int index, Dictionary<uint, uint> trexDefaults)
    {
        uint track = 0;
        long baseTime = 0;
        uint defaultDuration = 0;
        int samples = 0;
        long total = 0, firstCto = 0, minCto = long.MaxValue, maxCto = long.MinValue;
        foreach ((string type, int s, int e) in Children(b, start, end))
        {
            switch (type)
            {
                case "tfhd":
                {
                    uint flags = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(s)) & 0xFFFFFF;
                    track = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(s + 4));
                    int p = s + 8;
                    if ((flags & 0x1) != 0)
                    {
                        p += 8;
                    }

                    if ((flags & 0x2) != 0)
                    {
                        p += 4;
                    }

                    defaultDuration = (flags & 0x8) != 0 ? BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p)) : trexDefaults.GetValueOrDefault(track);
                    break;
                }

                case "tfdt":
                    baseTime = b[s] == 1 ? (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(s + 4)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(s + 4));
                    break;
                case "trun":
                {
                    int version = b[s];
                    uint flags = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(s)) & 0xFFFFFF;
                    int count = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(s + 4));
                    int p = s + 8;
                    if ((flags & 0x1) != 0)
                    {
                        p += 4;
                    }

                    if ((flags & 0x4) != 0)
                    {
                        p += 4;
                    }

                    for (int i = 0; i < count && p <= e; i++)
                    {
                        long duration = defaultDuration;
                        if ((flags & 0x100) != 0)
                        {
                            duration = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p));
                            p += 4;
                        }

                        if ((flags & 0x200) != 0)
                        {
                            p += 4;
                        }

                        if ((flags & 0x400) != 0)
                        {
                            p += 4;
                        }

                        long cto = 0;
                        if ((flags & 0x800) != 0)
                        {
                            cto = version == 0 ? BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p)) : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p));
                            p += 4;
                        }

                        if (samples == 0 && i == 0)
                        {
                            firstCto = cto;
                        }

                        minCto = Math.Min(minCto, cto);
                        maxCto = Math.Max(maxCto, cto);
                        total += duration;
                    }

                    samples += count;
                    break;
                }
            }
        }

        return new FragmentTiming(index, track, baseTime, samples, total, firstCto, minCto == long.MaxValue ? 0 : minCto, maxCto == long.MinValue ? 0 : maxCto);
    }

    private static byte[] ReadBox(Stream s, Mp4Box box)
    {
        var buffer = new byte[box.Size];
        s.Position = box.Offset;
        s.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Child boxes inside [start, end): (type, payload start, payload end). Container headers are 8 bytes.</summary>
    private static IEnumerable<(string Type, int Start, int End)> Children(byte[] b, int start, int end)
    {
        int p = start;
        while (p + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p));
            string type = System.Text.Encoding.ASCII.GetString(b, p + 4, 4);
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

            // Full boxes (version/flags) keep their 4-byte prefix in the payload; callers account for it.
            yield return (type, p + header, (int)(p + size));
            p += (int)size;
        }
    }
}
