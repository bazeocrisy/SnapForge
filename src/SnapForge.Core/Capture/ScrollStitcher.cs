using System.Runtime.InteropServices;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Capture;

/// <summary>Which way the captured viewport scrolls.</summary>
public enum ScrollDirection
{
    /// <summary>Content moves up between frames (scrolling down); the result grows downwards.</summary>
    Vertical,

    /// <summary>Content moves left between frames (scrolling right); the result grows to the right.</summary>
    Horizontal,
}

/// <summary>Outcome of <see cref="ScrollStitcher.AddFrame"/>.</summary>
public enum StitchStatus
{
    /// <summary>The frame was aligned and its new rows were appended.</summary>
    Appended,

    /// <summary>The content did not move (duplicate frame, at most caret-sized differences). Retry or stop.</summary>
    NoMovement,

    /// <summary>No reliable overlap with the previous frame was found; nothing was appended. Fall back to manual mode or stop.</summary>
    Unaligned,

    /// <summary>The output size limit was reached; rows that fit were appended and further frames are ignored.</summary>
    LimitReached,
}

/// <summary>Result of adding one frame to a <see cref="ScrollStitcher"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="NewRows">Rows (columns, for horizontal scrolling) appended by this frame.</param>
/// <param name="Offset">Detected scroll distance in rows; 0 when not aligned.</param>
public readonly record struct StitchResult(StitchStatus Status, int NewRows, int Offset);

/// <summary>Settings for <see cref="ScrollStitcher"/>. "Rows" are columns when the direction is horizontal.</summary>
public sealed record ScrollStitcherOptions
{
    /// <summary>Scroll direction.</summary>
    public ScrollDirection Direction { get; init; } = ScrollDirection.Vertical;

    /// <summary>Upper bound on the stitched image's pixel count (default 100 million ≈ 400 MB of BGRA).</summary>
    public long MaxOutputPixels { get; init; } = 100_000_000;

    /// <summary>Upper bound on the stitched length in rows; 0 means only <see cref="MaxOutputPixels"/> applies.</summary>
    public int MaxLength { get; init; }

    /// <summary>Fewest rows two consecutive frames must share for an alignment to be accepted.</summary>
    public int MinOverlapRows { get; init; } = 12;

    /// <summary>
    /// Fewest distinct exactly-matching rows the overlap must contain. Guards against "aligning" on blank or
    /// repetitive areas, where many offsets would fit equally well.
    /// </summary>
    public int MinDistinctRows { get; init; } = 4;

    /// <summary>Fraction of overlap pixels allowed to differ (blinking carets, antialiasing changes). Default 0.5%.</summary>
    public double MismatchTolerance { get; init; } = 0.005;

    /// <summary>Detect a sticky header and footer and keep each only once.</summary>
    public bool DetectStickyRegions { get; init; } = true;
}

/// <summary>
/// Stitches successive screenshots of a scrolling viewport into one long image ("scrolling capture").
/// </summary>
/// <remarks>
/// <para>
/// <b>Sticky regions.</b> At the first frame that moved, the rows identical at the top and at the bottom of
/// the two frames are taken as a sticky header and footer and excluded from matching. The header is kept
/// once from the first frame and the footer once from the last appended frame, so if plain content was
/// mistaken for a header or footer (for example blank rows that happened to stay blank), the result is still
/// complete and gap-free: those rows of the first and last frames are real content in their right place.
/// If no alignment is found with the detected regions, matching is retried without them.
/// </para>
/// <para>
/// <b>Alignment.</b> Every row gets a 64-bit hash, and every run of 8 consecutive rows a window hash. Windows
/// of the new frame that are rare in the previous frame vote for the offsets that would line them up (windows
/// stay distinctive where blank rows, solid bands and patterns meet, even when no single row is); the
/// best-voted positive offsets are verified on the whole
/// overlap band. Verification tolerates <see cref="ScrollStitcherOptions.MismatchTolerance"/> differing
/// pixels and requires <see cref="ScrollStitcherOptions.MinDistinctRows"/> distinct rows that match exactly
/// and an overlap of at least <see cref="ScrollStitcherOptions.MinOverlapRows"/> rows. If more than one
/// offset passes, the content is ambiguous (for example a repeating pattern filling the viewport) and the
/// frame is reported <see cref="StitchStatus.Unaligned"/> rather than guessed.
/// </para>
/// <para>
/// <b>Output.</b> Exactly the rows below the overlap are appended, so nothing is duplicated or skipped.
/// Only those row strips, the header, the first frame and the previous frame are kept in memory.
/// Only forward scrolling is supported; scrolling back reports <see cref="StitchStatus.Unaligned"/>.
/// Pixels are compared as raw BGRA values.
/// </para>
/// </remarks>
public sealed class ScrollStitcher
{
    private const int MaxHashOccurrences = 8;
    private const int MaxCandidates = 16;
    private const int WindowRows = 8;

    private readonly ScrollStitcherOptions _options;
    private readonly List<uint[]> _strips = [];

    private int _frameWidth, _frameHeight;
    private int _lineWidth, _lineCount;
    private long _maxLines;
    private uint[]? _first;
    private uint[]? _prev;
    private ulong[] _prevHashes = [];
    private uint[] _headerLines = [];
    private bool _committed;
    private bool _truncated;
    private int _header, _footer;
    private long _contentLines;

    /// <summary>Creates a stitcher; null options use the defaults.</summary>
    public ScrollStitcher(ScrollStitcherOptions? options = null)
    {
        options ??= new ScrollStitcherOptions();
        if (!Enum.IsDefined(options.Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown scroll direction.");
        }

        if (options.MaxOutputPixels < 1 || options.MaxLength < 0 || options.MinOverlapRows < 1 || options.MinDistinctRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Limits must be positive (MaxLength may be 0 for no limit).");
        }

        if (!(options.MismatchTolerance >= 0 && options.MismatchTolerance < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MismatchTolerance must be in [0, 1).");
        }

        _options = options;
    }

    /// <summary>Frames passed to <see cref="AddFrame"/> so far.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Length of the image <see cref="Build"/> would return, in rows (columns when horizontal).</summary>
    public long Length => !_committed
        ? Math.Min(_lineCount, _maxLines)
        : _header + _contentLines + (_truncated ? 0 : _footer);

    /// <summary>Detected sticky header size in rows (0 until the first movement).</summary>
    public int HeaderRows => _header;

    /// <summary>Detected sticky footer size in rows (0 until the first movement).</summary>
    public int FooterRows => _footer;

    /// <summary>Aligns <paramref name="frame"/> with the previous accepted frame and appends its new rows.</summary>
    /// <exception cref="ArgumentException">The frame size differs from the first frame's.</exception>
    public StitchResult AddFrame(PixelBuffer frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (FrameCount > 0 && (frame.Width != _frameWidth || frame.Height != _frameHeight))
        {
            throw new ArgumentException($"Frame is {frame.Width}×{frame.Height}; the first frame was {_frameWidth}×{_frameHeight}.", nameof(frame));
        }

        if (_truncated)
        {
            FrameCount++;
            return new StitchResult(StitchStatus.LimitReached, 0, 0);
        }

        if (FrameCount == 0)
        {
            return AddFirst(frame);
        }

        FrameCount++;
        uint[] cur = Orient(frame);
        ulong[] curHashes = HashLines(cur);
        uint[] prev = _prev!;

        if (curHashes.AsSpan().SequenceEqual(_prevHashes) && cur.AsSpan().SequenceEqual(prev))
        {
            return new StitchResult(StitchStatus.NoMovement, 0, 0);
        }

        int header = _header, footer = _footer, offset;
        if (_committed)
        {
            offset = FindOffset(prev, cur, curHashes, header, footer);
        }
        else
        {
            offset = 0;
            if (_options.DetectStickyRegions)
            {
                (header, footer) = DetectSticky(curHashes, prev, cur);
                offset = FindOffset(prev, cur, curHashes, header, footer);
            }

            if (offset == 0 && (header != 0 || footer != 0 || !_options.DetectStickyRegions))
            {
                (header, footer) = (0, 0);
                offset = FindOffset(prev, cur, curHashes, 0, 0);
            }
        }

        if (offset == 0)
        {
            bool still = MatchesAt(prev, cur, curHashes, _header, _lineCount - _header - _footer, 0, out _);
            return new StitchResult(still ? StitchStatus.NoMovement : StitchStatus.Unaligned, 0, 0);
        }

        if (!_committed)
        {
            Commit(header, footer);
        }

        int contentEnd = _lineCount - _footer;
        int newStart = contentEnd - offset;
        long room = _maxLines - _header - _contentLines - _footer;
        int take = offset;
        if (offset > room)
        {
            take = (int)Math.Max(0, _maxLines - _header - _contentLines);
            take = Math.Min(take, offset);
            _truncated = true;
        }

        if (take > 0)
        {
            _strips.Add(cur.AsSpan(newStart * _lineWidth, take * _lineWidth).ToArray());
            _contentLines += take;
        }

        _prev = cur;
        _prevHashes = curHashes;
        return new StitchResult(_truncated ? StitchStatus.LimitReached : StitchStatus.Appended, take, offset);
    }

    /// <summary>Assembles the stitched image: header, appended content, footer.</summary>
    /// <exception cref="InvalidOperationException">No frame was added.</exception>
    public PixelBuffer Build()
    {
        if (_prev is null)
        {
            throw new InvalidOperationException("No frames were added.");
        }

        int length = checked((int)Length);
        var lines = new uint[checked(length * _lineWidth)];
        if (!_committed)
        {
            _first.AsSpan(0, lines.Length).CopyTo(lines);
        }
        else
        {
            int pos = 0;
            _headerLines.CopyTo(lines, pos);
            pos += _headerLines.Length;
            foreach (uint[] strip in _strips)
            {
                strip.CopyTo(lines, pos);
                pos += strip.Length;
            }

            if (!_truncated)
            {
                _prev.AsSpan((_lineCount - _footer) * _lineWidth).CopyTo(lines.AsSpan(pos));
            }
        }

        if (_options.Direction == ScrollDirection.Vertical)
        {
            var result = new PixelBuffer(_lineWidth, length);
            MemoryMarshal.AsBytes(lines.AsSpan()).CopyTo(result.Pixels);
            return result;
        }

        var transposed = new PixelBuffer(length, _lineWidth);
        Span<uint> dest = MemoryMarshal.Cast<byte, uint>(transposed.Pixels.AsSpan());
        for (int line = 0; line < length; line++)
        {
            for (int k = 0; k < _lineWidth; k++)
            {
                dest[k * length + line] = lines[line * _lineWidth + k];
            }
        }

        return transposed;
    }

    private StitchResult AddFirst(PixelBuffer frame)
    {
        _frameWidth = frame.Width;
        _frameHeight = frame.Height;
        bool vertical = _options.Direction == ScrollDirection.Vertical;
        _lineWidth = vertical ? frame.Width : frame.Height;
        _lineCount = vertical ? frame.Height : frame.Width;
        _maxLines = _options.MaxOutputPixels / _lineWidth;
        if (_options.MaxLength > 0)
        {
            _maxLines = Math.Min(_maxLines, _options.MaxLength);
        }

        if (_maxLines < 1)
        {
            throw new ArgumentException("A single row of the frame exceeds MaxOutputPixels.", nameof(frame));
        }

        FrameCount = 1;
        _first = Orient(frame);
        _prev = _first;
        _prevHashes = HashLines(_first);
        if (_lineCount > _maxLines)
        {
            _truncated = true;
            return new StitchResult(StitchStatus.LimitReached, (int)_maxLines, 0);
        }

        return new StitchResult(StitchStatus.Appended, _lineCount, 0);
    }

    private void Commit(int header, int footer)
    {
        uint[] first = _first!;
        _header = header;
        _footer = footer;
        _headerLines = first.AsSpan(0, header * _lineWidth).ToArray();
        int contentLines = _lineCount - header - footer;
        _strips.Add(first.AsSpan(header * _lineWidth, contentLines * _lineWidth).ToArray());
        _contentLines = contentLines;
        _committed = true;
        _first = null;
    }

    /// <summary>Rows identical at the top and bottom of two frames that differ somewhere.</summary>
    private (int Header, int Footer) DetectSticky(ulong[] curHashes, uint[] prev, uint[] cur)
    {
        int top = 0;
        while (top < _lineCount && curHashes[top] == _prevHashes[top] && Line(prev, top).SequenceEqual(Line(cur, top)))
        {
            top++;
        }

        int bottom = 0;
        while (bottom < _lineCount - top && curHashes[_lineCount - 1 - bottom] == _prevHashes[_lineCount - 1 - bottom]
            && Line(prev, _lineCount - 1 - bottom).SequenceEqual(Line(cur, _lineCount - 1 - bottom)))
        {
            bottom++;
        }

        return (top, bottom);
    }

    /// <summary>Returns the verified positive scroll offset, or 0 if there is none or it is ambiguous.</summary>
    private int FindOffset(uint[] prev, uint[] cur, ulong[] curHashes, int header, int footer)
    {
        int length = _lineCount - header - footer;
        int maxOffset = length - _options.MinOverlapRows;
        if (maxOffset < 1)
        {
            return 0;
        }

        // Vote with hashes of short windows of rows rather than single rows: a window spanning the edge
        // between two repetitive areas (blank → solid band → pattern) is distinctive even when no row is.
        int window = Math.Min(WindowRows, _options.MinOverlapRows);
        ulong[] prevKeys = WindowHashes(_prevHashes, header, length, window);
        ulong[] curKeys = WindowHashes(curHashes, header, length, window);
        var occurrences = new Dictionary<ulong, List<int>>();
        for (int j = 0; j < prevKeys.Length; j++)
        {
            if (!occurrences.TryGetValue(prevKeys[j], out List<int>? list))
            {
                occurrences[prevKeys[j]] = list = [];
            }

            if (list.Count <= MaxHashOccurrences)
            {
                list.Add(j);
            }
        }

        var votes = new int[maxOffset + 1];
        for (int i = 0; i < curKeys.Length; i++)
        {
            if (!occurrences.TryGetValue(curKeys[i], out List<int>? list) || list.Count > MaxHashOccurrences)
            {
                continue;
            }

            foreach (int j in list)
            {
                int d = j - i;
                if (d >= 1 && d <= maxOffset)
                {
                    votes[d]++;
                }
            }
        }

        var candidates = new List<int>();
        for (int d = 1; d <= maxOffset; d++)
        {
            if (votes[d] > 0)
            {
                candidates.Add(d);
            }
        }

        candidates.Sort((a, b) => votes[a] != votes[b] ? votes[b].CompareTo(votes[a]) : a.CompareTo(b));
        int found = 0;
        foreach (int d in candidates.Take(MaxCandidates))
        {
            if (MatchesAt(prev, cur, curHashes, header, length, d, out int distinct) && distinct >= _options.MinDistinctRows)
            {
                if (found != 0)
                {
                    return 0; // two offsets fit: ambiguous
                }

                found = d;
            }
        }

        return found;
    }

    private static ulong[] WindowHashes(ulong[] rowHashes, int start, int length, int window)
    {
        var keys = new ulong[Math.Max(0, length - window + 1)];
        for (int i = 0; i < keys.Length; i++)
        {
            ulong h = 0xCBF29CE484222325ul;
            for (int k = 0; k < window; k++)
            {
                h = (h ^ rowHashes[start + i + k]) * 0x100000001B3ul;
                h ^= h >> 29;
            }

            keys[i] = h;
        }

        return keys;
    }

    /// <summary>
    /// Compares the overlap when the content moved by <paramref name="offset"/> rows: cur row i against prev row i + offset,
    /// within the area starting at <paramref name="start"/> spanning <paramref name="length"/> rows.
    /// </summary>
    private bool MatchesAt(uint[] prev, uint[] cur, ulong[] curHashes, int start, int length, int offset, out int distinctExactRows)
    {
        distinctExactRows = 0;
        int overlap = length - offset;
        if (overlap < 1)
        {
            return false;
        }

        long budget = (long)(_options.MismatchTolerance * overlap * _lineWidth);
        long mismatches = 0;
        var distinct = new HashSet<ulong>();
        for (int i = 0; i < overlap; i++)
        {
            int c = start + i, p = start + i + offset;
            ReadOnlySpan<uint> a = Line(cur, c), b = Line(prev, p);
            if (_prevHashes[p] == curHashes[c] && a.SequenceEqual(b))
            {
                if (distinct.Count < _options.MinDistinctRows)
                {
                    distinct.Add(_prevHashes[p]);
                }

                continue;
            }

            for (int x = 0; x < a.Length; x++)
            {
                if (a[x] != b[x] && ++mismatches > budget)
                {
                    return false;
                }
            }
        }

        distinctExactRows = distinct.Count;
        return true;
    }

    private ReadOnlySpan<uint> Line(uint[] lines, int index) => lines.AsSpan(index * _lineWidth, _lineWidth);

    private uint[] Orient(PixelBuffer frame)
    {
        ReadOnlySpan<uint> src = MemoryMarshal.Cast<byte, uint>(frame.Pixels.AsSpan());
        if (_options.Direction == ScrollDirection.Vertical)
        {
            return src.ToArray();
        }

        int w = frame.Width, h = frame.Height;
        var lines = new uint[src.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                lines[x * h + y] = src[y * w + x];
            }
        }

        return lines;
    }

    private ulong[] HashLines(uint[] lines)
    {
        var hashes = new ulong[_lineCount];
        for (int i = 0; i < _lineCount; i++)
        {
            hashes[i] = HashLine(Line(lines, i));
        }

        return hashes;
    }

    private static ulong HashLine(ReadOnlySpan<uint> line)
    {
        ReadOnlySpan<ulong> wide = MemoryMarshal.Cast<uint, ulong>(line);
        ulong h = 0x9E3779B97F4A7C15ul ^ (ulong)line.Length;
        foreach (ulong v in wide)
        {
            h = (System.Numerics.BitOperations.RotateLeft(h ^ (v * 0xC2B2AE3D27D4EB4Ful), 31)) * 0x9E3779B185EBCA87ul;
        }

        if ((line.Length & 1) != 0)
        {
            h = (System.Numerics.BitOperations.RotateLeft(h ^ (line[^1] * 0xC2B2AE3D27D4EB4Ful), 31)) * 0x9E3779B185EBCA87ul;
        }

        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDul;
        h ^= h >> 29;
        return h;
    }
}
