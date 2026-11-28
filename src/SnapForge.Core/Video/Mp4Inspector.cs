using System.Buffers.Binary;

namespace SnapForge.Core.Video;

/// <summary>A top-level box of an MP4 file.</summary>
public sealed record Mp4Box(string Type, long Offset, long Size);

/// <summary>What <see cref="Mp4Inspector.Inspect"/> found in an MP4 file.</summary>
public sealed record Mp4Structure(
    IReadOnlyList<Mp4Box> Boxes,
    bool HasFileType,
    bool HasMovie,
    bool IsFragmented,
    int CompleteFragments,
    long CompleteLength,
    long FileLength,
    string? Problem)
{
    /// <summary>True if the file is structurally complete: header, movie box and nothing cut off.</summary>
    public bool IsComplete => Problem is null && HasFileType && HasMovie && CompleteLength == FileLength;

    /// <summary>True if a fragmented file can be cut back to its last complete fragment and still play.</summary>
    public bool IsRecoverable => HasFileType && HasMovie && IsFragmented && CompleteFragments > 0;
}

/// <summary>
/// Reads the top-level box structure of MP4 / fragmented MP4 files without loading them into memory, and recovers
/// recordings interrupted by a crash, power loss or full disk (VID-R04, VID-R05): a fragmented MP4 is a header
/// (<c>ftyp</c> + <c>moov</c>) followed by <c>moof</c>+<c>mdat</c> pairs, each of which is self-contained, so cutting
/// the file at the end of the last complete pair leaves a playable file. Recovery is only claimed when the file has
/// that shape; anything else is reported as not recoverable.
/// </summary>
public static class Mp4Inspector
{
    public static Mp4Structure Inspect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        return Inspect(stream);
    }

    public static Mp4Structure Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long length = stream.Length;
        var boxes = new List<Mp4Box>();
        Span<byte> header = stackalloc byte[16];
        long pos = 0;
        string? problem = null;
        while (pos < length)
        {
            if (length - pos < 8)
            {
                problem = "File ends inside a box header.";
                break;
            }

            stream.Position = pos;
            stream.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = TypeName(header.Slice(4, 4));
            int headerLength = 8;
            if (size == 1)
            {
                if (length - pos < 16)
                {
                    problem = "File ends inside a box header.";
                    break;
                }

                stream.ReadExactly(header.Slice(8, 8));
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = length - pos; // box extends to the end of the file
            }

            if (size < headerLength || !IsPrintable(type))
            {
                problem = $"Invalid box at offset {pos}.";
                break;
            }

            if (pos + size > length)
            {
                boxes.Add(new Mp4Box(type, pos, size));
                problem = $"The '{type}' box at offset {pos} is cut off ({length - pos} of {size} bytes).";
                break;
            }

            boxes.Add(new Mp4Box(type, pos, size));
            pos += size;
        }

        return Analyse(boxes, length, problem);
    }

    private static Mp4Structure Analyse(List<Mp4Box> boxes, long length, string? problem)
    {
        bool ftyp = boxes.Count > 0 && boxes[0].Type == "ftyp";
        bool moov = boxes.Any(b => b.Type == "moov" && b.Offset + b.Size <= length);
        bool fragmented = boxes.Any(b => b.Type == "moof");
        int fragments = 0;
        long complete = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            Mp4Box b = boxes[i];
            bool whole = b.Offset + b.Size <= length;
            if (!whole)
            {
                break;
            }

            if (b.Type == "moof")
            {
                // A fragment is complete only with its media data right after it.
                if (i + 1 < boxes.Count && boxes[i + 1].Type == "mdat" && boxes[i + 1].Offset + boxes[i + 1].Size <= length)
                {
                    fragments++;
                    i++;
                    complete = boxes[i].Offset + boxes[i].Size;
                    continue;
                }

                break;
            }

            if (b.Type == "mdat" && fragmented)
            {
                break; // media data without a fragment header before it
            }

            complete = b.Offset + b.Size;
        }

        if (problem is null && complete != length)
        {
            problem = "The file ends with an incomplete fragment.";
        }

        return new Mp4Structure(boxes, ftyp, moov, fragmented, fragments, complete, length, problem);
    }

    /// <summary>
    /// Cuts an interrupted fragmented MP4 back to its last complete fragment, in place. Returns the structure after
    /// recovery, or null (file untouched) if it is not a recoverable fragmented MP4.
    /// </summary>
    public static Mp4Structure? Recover(string path)
    {
        Mp4Structure s = Inspect(path);
        if (!s.IsRecoverable)
        {
            return null;
        }

        if (s.CompleteLength < s.FileLength)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(s.CompleteLength);
            stream.Flush(flushToDisk: true);
        }

        return Inspect(path);
    }

    private static string TypeName(ReadOnlySpan<byte> b) => string.Create(4, b.ToArray(), (span, bytes) =>
    {
        for (int i = 0; i < 4; i++)
        {
            span[i] = (char)bytes[i];
        }
    });

    private static bool IsPrintable(string type) => type.All(c => c is >= ' ' and <= '~' or '©');
}
