using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using TestRng = SnapForge.Core.Tests.GifEncoderTests.TestRng;

namespace SnapForge.Core.Tests;

public class ScrollStitcherTests
{
    private const int DocWidth = 160;
    private const int Viewport = 180;

    /// <summary>A synthetic page: text-like lines, blank gaps, solid bands and short repeating patterns.</summary>
    private static PixelBuffer Document(int height, ulong seed, int leadingBlank = 0, int trailingBand = 0)
    {
        var rng = new TestRng(seed);
        var doc = new PixelBuffer(DocWidth, height);
        doc.FillRect(doc.Bounds, ArgbColor.White);
        int y = leadingBlank;
        int end = height - trailingBand;
        while (y < end)
        {
            int kind = rng.Next(10);
            if (kind < 5)
            {
                // A line of "text": every row is different, sparse dark marks on white.
                int rows = 10 + rng.Next(5);
                ArgbColor ink = ArgbColor.FromRgb((byte)rng.Next(80), (byte)rng.Next(80), (byte)rng.Next(120));
                for (int r = 0; r < rows && y < end; r++, y++)
                {
                    for (int x = 4; x < DocWidth - 4; x++)
                    {
                        if (rng.Next(100) < 22)
                        {
                            doc.SetPixel(x, y, ink);
                        }
                    }
                }
            }
            else if (kind < 7)
            {
                y += 3 + rng.Next(6); // blank gap
            }
            else if (kind < 9)
            {
                int rows = 8 + rng.Next(18);
                doc.FillRect(new PixelRect(0, y, DocWidth, Math.Min(rows, end - y)), ArgbColor.FromRgb((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)));
                y += rows;
            }
            else
            {
                // A repeating pattern with period 3.
                int rows = 9 + rng.Next(18);
                ArgbColor a = ArgbColor.FromRgb(200, 30, 30), b = ArgbColor.FromRgb(30, 30, 200);
                for (int r = 0; r < rows && y < end; r++, y++)
                {
                    for (int x = 0; x < DocWidth; x++)
                    {
                        doc.SetPixel(x, y, (x + r % 3) % 3 == 0 ? a : b);
                    }
                }
            }
        }

        if (trailingBand > 0)
        {
            doc.FillRect(new PixelRect(0, height - trailingBand, DocWidth, trailingBand), ArgbColor.FromRgb(90, 140, 90));
        }

        return doc;
    }

    private static PixelBuffer Noise(int w, int h, ulong seed)
    {
        var rng = new TestRng(seed);
        var b = new PixelBuffer(w, h);
        for (int i = 0; i < b.Pixels.Length; i += 4)
        {
            uint v = rng.Next();
            b.Pixels[i] = (byte)v;
            b.Pixels[i + 1] = (byte)(v >> 8);
            b.Pixels[i + 2] = (byte)(v >> 16);
            b.Pixels[i + 3] = 255;
        }

        return b;
    }

    /// <summary>What the screen shows with the document scrolled to <paramref name="top"/>.</summary>
    private static PixelBuffer Frame(PixelBuffer doc, int top, PixelBuffer? header = null, PixelBuffer? footer = null, bool caret = false)
    {
        int h = header?.Height ?? 0, f = footer?.Height ?? 0;
        var frame = new PixelBuffer(doc.Width, h + Viewport + f);
        if (header is not null)
        {
            frame.Blit(header, 0, 0);
        }

        frame.Blit(doc.Crop(new PixelRect(0, top, doc.Width, Viewport)), 0, h);
        if (footer is not null)
        {
            frame.Blit(footer, 0, h + Viewport);
        }

        if (caret)
        {
            frame.FillRect(new PixelRect(70, h + 30, 2, 12), ArgbColor.Black);
        }

        return frame;
    }

    private static List<int> Positions(int docHeight, params int[] steps)
    {
        var positions = new List<int> { 0 };
        int k = 0;
        while (positions[^1] < docHeight - Viewport)
        {
            positions.Add(Math.Min(docHeight - Viewport, positions[^1] + steps[k++ % steps.Length]));
        }

        return positions;
    }

    private static PixelBuffer Stack(params PixelBuffer[] parts)
    {
        var result = new PixelBuffer(parts[0].Width, parts.Sum(p => p.Height));
        int y = 0;
        foreach (PixelBuffer p in parts)
        {
            result.Blit(p, 0, y);
            y += p.Height;
        }

        return result;
    }

    private static void AssertSameImage(PixelBuffer expected, PixelBuffer actual)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        if (!expected.Pixels.AsSpan().SequenceEqual(actual.Pixels))
        {
            int first = 0;
            while (expected.Pixels[first] == actual.Pixels[first])
            {
                first++;
            }

            int p = first / 4;
            Assert.Fail($"First difference at ({p % expected.Width},{p / expected.Width})");
        }
    }

    private static PixelBuffer Transpose(PixelBuffer b)
    {
        var t = new PixelBuffer(b.Height, b.Width);
        for (int y = 0; y < b.Height; y++)
        {
            for (int x = 0; x < b.Width; x++)
            {
                t.SetPixel(y, x, b.GetPixel(x, y));
            }
        }

        return t;
    }

    [Theory]
    [InlineData(1, 2400, 1, 1, 1, 2, 3)]
    [InlineData(2, 2400, 7)]
    [InlineData(3, 2400, 40, 13, 90)]
    [InlineData(4, 2400, 120)]
    [InlineData(5, 2400, 150, 140)]
    [InlineData(6, 1000, 61)]
    public void Scrolled_document_is_reassembled_exactly(int seed, int docHeight, params int[] steps)
    {
        PixelBuffer doc = Document(docHeight, (ulong)seed);
        var stitcher = new ScrollStitcher();
        List<int> positions = Positions(docHeight, steps);
        for (int k = 0; k < positions.Count; k++)
        {
            StitchResult r = stitcher.AddFrame(Frame(doc, positions[k]));
            Assert.Equal(StitchStatus.Appended, r.Status);
            Assert.Equal(k == 0 ? Viewport : positions[k] - positions[k - 1], r.NewRows);
        }

        // Scrolling has hit the end: more frames show no movement.
        Assert.Equal(StitchStatus.NoMovement, stitcher.AddFrame(Frame(doc, positions[^1])).Status);
        AssertSameImage(doc, stitcher.Build());
    }

    [Fact]
    public void Random_documents_and_steps_never_misalign()
    {
        // Property: whatever happens, the output is always an exact prefix of the page. A run that is not
        // aligned stops there (as the capture UI would). Overlaps of a text row plus a solid band are refused by design
        // (too few distinct rows), so a few runs may stop early.
        int unalignedRuns = 0;
        const int Runs = 100;
        for (int run = 0; run < Runs; run++)
        {
            var rng = new TestRng((ulong)(1000 + run));
            PixelBuffer doc = Document(700 + rng.Next(900), (ulong)(5000 + run));
            PixelBuffer? header = rng.Next(2) == 0 ? Noise(DocWidth, 10 + rng.Next(30), (ulong)run) : null;
            PixelBuffer? footer = rng.Next(2) == 0 ? Noise(DocWidth, 5 + rng.Next(20), (ulong)run + 77) : null;
            bool caret = rng.Next(2) == 0;
            var stitcher = new ScrollStitcher();
            int top = 0, lastAppended = 0;
            for (int k = 0; ; k++)
            {
                StitchResult r = stitcher.AddFrame(Frame(doc, top, header, footer, caret && k % 2 == 1));
                if (r.Status == StitchStatus.Unaligned)
                {
                    unalignedRuns++;
                    break;
                }

                Assert.True(r.Status == StitchStatus.Appended, $"run {run} frame {k}: {r}");
                Assert.Equal(k == 0 ? (header?.Height ?? 0) + Viewport + (footer?.Height ?? 0) : top - lastAppended, r.NewRows);
                lastAppended = top;
                if (top == doc.Height - Viewport)
                {
                    break;
                }

                // Overlap of at least 60 rows (a typical scroll step); the caret (viewport rows 30..41) stays out of new rows.
                top = Math.Min(doc.Height - Viewport, top + 1 + rng.Next(Viewport - 60));
            }

            PixelBuffer content = doc.Crop(new PixelRect(0, 0, DocWidth, lastAppended + Viewport));
            PixelBuffer expected = Stack([.. new[] { header, content, footer }.OfType<PixelBuffer>()]);
            AssertSameImage(expected, stitcher.Build());
        }

        Assert.True(unalignedRuns <= Runs / 20, $"{unalignedRuns} of {Runs} runs stopped unaligned");
    }

    [Theory]
    [InlineData(11, 23)]
    [InlineData(12, 1)]
    [InlineData(13, 140)]
    public void Sticky_header_and_footer_are_kept_once(int seed, int step)
    {
        PixelBuffer doc = Document(1500, (ulong)seed);
        PixelBuffer header = Noise(DocWidth, 24, 100), footer = Noise(DocWidth, 16, 200);
        var stitcher = new ScrollStitcher();
        foreach (int top in Positions(doc.Height, step))
        {
            StitchResult r = stitcher.AddFrame(Frame(doc, top, header, footer));
            Assert.True(r.Status == StitchStatus.Appended, $"top {top}: {r}");
        }

        Assert.InRange(stitcher.HeaderRows, 24, 40); // blank document rows may extend it harmlessly
        Assert.InRange(stitcher.FooterRows, 16, 40);
        AssertSameImage(Stack(header, doc, footer), stitcher.Build());
    }

    [Fact]
    public void Header_detection_disabled_still_stitches_plain_content()
    {
        PixelBuffer doc = Document(900, 21);
        var stitcher = new ScrollStitcher(new ScrollStitcherOptions { DetectStickyRegions = false });
        foreach (int top in Positions(doc.Height, 33))
        {
            Assert.Equal(StitchStatus.Appended, stitcher.AddFrame(Frame(doc, top)).Status);
        }

        AssertSameImage(doc, stitcher.Build());
    }

    [Fact]
    public void Blank_top_and_solid_bottom_mistaken_for_sticky_regions_still_give_exact_output()
    {
        // The first scroll keeps 30 blank rows at the top and a 60-row band at the bottom unchanged.
        PixelBuffer doc = Document(1200, 31, leadingBlank: 40, trailingBand: 80);
        var stitcher = new ScrollStitcher();
        foreach (int top in Positions(doc.Height, 10, 55))
        {
            Assert.Equal(StitchStatus.Appended, stitcher.AddFrame(Frame(doc, top)).Status);
        }

        Assert.True(stitcher.HeaderRows > 0);
        AssertSameImage(doc, stitcher.Build());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Blinking_caret_does_not_break_alignment(bool withHeader)
    {
        PixelBuffer doc = Document(1400, 41);
        PixelBuffer? header = withHeader ? Noise(DocWidth, 20, 5) : null;
        var stitcher = new ScrollStitcher();
        List<int> positions = Positions(doc.Height, 37, 12, 90);
        for (int k = 0; k < positions.Count; k++)
        {
            // The caret sits near the top of the viewport, inside the overlap, and blinks on every other frame.
            StitchResult r = stitcher.AddFrame(Frame(doc, positions[k], header, caret: k % 2 == 1));
            Assert.True(r.Status == StitchStatus.Appended, $"frame {k} top {positions[k]}: {r}");
        }

        // Caret toggling alone is not movement.
        Assert.Equal(StitchStatus.NoMovement, stitcher.AddFrame(Frame(doc, positions[^1], header, caret: positions.Count % 2 == 1)).Status);
        AssertSameImage(header is null ? doc : Stack(header, doc), stitcher.Build());
    }

    [Fact]
    public void Duplicate_frames_report_no_movement()
    {
        PixelBuffer doc = Document(600, 51);
        var stitcher = new ScrollStitcher();
        Assert.Equal(StitchStatus.Appended, stitcher.AddFrame(Frame(doc, 0)).Status);
        Assert.Equal(StitchStatus.NoMovement, stitcher.AddFrame(Frame(doc, 0)).Status);
        Assert.Equal(StitchStatus.NoMovement, stitcher.AddFrame(Frame(doc, 0, caret: true)).Status);
        StitchResult moved = stitcher.AddFrame(Frame(doc, 50));
        Assert.Equal(new StitchResult(StitchStatus.Appended, 50, 50), moved);
        Assert.Equal(StitchStatus.NoMovement, stitcher.AddFrame(Frame(doc, 50)).Status);
        AssertSameImage(doc.Crop(new PixelRect(0, 0, DocWidth, Viewport + 50)), stitcher.Build());
    }

    [Fact]
    public void Unrelated_or_backward_frames_are_unaligned_and_do_not_change_the_result()
    {
        PixelBuffer doc = Document(800, 61), other = Document(800, 62);
        var stitcher = new ScrollStitcher();
        stitcher.AddFrame(Frame(doc, 0));
        stitcher.AddFrame(Frame(doc, 100));
        Assert.Equal(StitchStatus.Unaligned, stitcher.AddFrame(Frame(other, 100)).Status);
        Assert.Equal(StitchStatus.Unaligned, stitcher.AddFrame(Noise(DocWidth, Viewport, 3)).Status);
        Assert.Equal(StitchStatus.Unaligned, stitcher.AddFrame(Frame(doc, 40)).Status); // scrolled back
        Assert.Equal(StitchStatus.Appended, stitcher.AddFrame(Frame(doc, 170)).Status);
        AssertSameImage(doc.Crop(new PixelRect(0, 0, DocWidth, Viewport + 170)), stitcher.Build());
    }

    [Fact]
    public void Ambiguous_repetitive_content_is_unaligned()
    {
        // Text at the top, then a long stretch of a period-40 pattern: offsets d, d+40, d+80 all fit.
        var doc = new PixelBuffer(DocWidth, 900);
        PixelBuffer text = Document(150, 71);
        doc.Blit(text, 0, 0);
        for (int y = 150; y < 900; y++)
        {
            doc.FillRect(new PixelRect(0, y, DocWidth, 1), ArgbColor.FromRgb((byte)(6 * (y % 40)), 100, 200));
        }

        var stitcher = new ScrollStitcher();
        Assert.Equal(StitchStatus.Appended, stitcher.AddFrame(Frame(doc, 300)).Status);
        Assert.Equal(StitchStatus.Unaligned, stitcher.AddFrame(Frame(doc, 331)).Status);
    }

    [Fact]
    public void Blank_page_scrolled_is_no_movement()
    {
        var doc = new PixelBuffer(DocWidth, 600);
        doc.FillRect(doc.Bounds, ArgbColor.White);
        var stitcher = new ScrollStitcher();
        stitcher.AddFrame(Frame(doc, 0));
        Assert.Equal(StitchStatus.NoMovement, stitcher.AddFrame(Frame(doc, 100)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_limit_truncates_without_gaps(bool limitByPixels)
    {
        PixelBuffer doc = Document(1600, 81);
        var options = limitByPixels ? new ScrollStitcherOptions { MaxOutputPixels = 500L * DocWidth + 3 } : new ScrollStitcherOptions { MaxLength = 500 };
        var stitcher = new ScrollStitcher(options);
        var statuses = new List<StitchStatus>();
        foreach (int top in Positions(doc.Height, 70))
        {
            statuses.Add(stitcher.AddFrame(Frame(doc, top)).Status);
        }

        int firstLimit = statuses.IndexOf(StitchStatus.LimitReached);
        Assert.True(firstLimit > 0);
        Assert.All(statuses.Take(firstLimit), s => Assert.Equal(StitchStatus.Appended, s));
        Assert.All(statuses.Skip(firstLimit), s => Assert.Equal(StitchStatus.LimitReached, s));
        Assert.Equal(new StitchResult(StitchStatus.LimitReached, 0, 0), stitcher.AddFrame(Frame(doc, 0)));
        AssertSameImage(doc.Crop(new PixelRect(0, 0, DocWidth, 500)), stitcher.Build());
    }

    [Fact]
    public void Output_limit_with_sticky_regions_drops_the_footer()
    {
        PixelBuffer doc = Document(1600, 82);
        PixelBuffer header = Noise(DocWidth, 24, 7), footer = Noise(DocWidth, 16, 8);
        var stitcher = new ScrollStitcher(new ScrollStitcherOptions { MaxLength = 400 });
        StitchResult last = default;
        foreach (int top in Positions(doc.Height, 70))
        {
            last = stitcher.AddFrame(Frame(doc, top, header, footer));
        }

        Assert.Equal(StitchStatus.LimitReached, last.Status);
        AssertSameImage(Stack(header, doc.Crop(new PixelRect(0, 0, DocWidth, 376))), stitcher.Build());
    }

    [Fact]
    public void First_frame_larger_than_the_limit_is_truncated()
    {
        PixelBuffer doc = Document(600, 83);
        var stitcher = new ScrollStitcher(new ScrollStitcherOptions { MaxLength = 100 });
        Assert.Equal(new StitchResult(StitchStatus.LimitReached, 100, 0), stitcher.AddFrame(Frame(doc, 0)));
        AssertSameImage(doc.Crop(new PixelRect(0, 0, DocWidth, 100)), stitcher.Build());
    }

    [Theory]
    [InlineData(19)]
    [InlineData(1)]
    public void Horizontal_scrolling_is_stitched_left_to_right(int step)
    {
        PixelBuffer wide = Transpose(Document(1100, 91)); // 1100 × 160
        PixelBuffer header = Transpose(Noise(DocWidth, 20, 9)); // a sticky column strip on the left
        var stitcher = new ScrollStitcher(new ScrollStitcherOptions { Direction = ScrollDirection.Horizontal });
        foreach (int left in Positions(wide.Width, step))
        {
            var frame = new PixelBuffer(header.Width + Viewport, wide.Height);
            frame.Blit(header, 0, 0);
            frame.Blit(wide.Crop(new PixelRect(left, 0, Viewport, wide.Height)), header.Width, 0);
            Assert.Equal(StitchStatus.Appended, stitcher.AddFrame(frame).Status);
        }

        PixelBuffer expected = new(header.Width + wide.Width, wide.Height);
        expected.Blit(header, 0, 0);
        expected.Blit(wide, header.Width, 0);
        AssertSameImage(expected, stitcher.Build());
    }

    [Fact]
    public void Single_frame_builds_to_itself()
    {
        PixelBuffer frame = Noise(30, 20, 1);
        var stitcher = new ScrollStitcher();
        Assert.Equal(new StitchResult(StitchStatus.Appended, 20, 0), stitcher.AddFrame(frame));
        AssertSameImage(frame, stitcher.Build());
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => new ScrollStitcher().Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollStitcher(new ScrollStitcherOptions { MaxLength = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollStitcher(new ScrollStitcherOptions { MinOverlapRows = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollStitcher(new ScrollStitcherOptions { MismatchTolerance = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollStitcher(new ScrollStitcherOptions { MaxOutputPixels = 0 }));
        var stitcher = new ScrollStitcher();
        Assert.Throws<ArgumentNullException>(() => stitcher.AddFrame(null!));
        stitcher.AddFrame(new PixelBuffer(10, 10));
        Assert.Throws<ArgumentException>(() => stitcher.AddFrame(new PixelBuffer(10, 11)));
        Assert.Throws<ArgumentException>(() => new ScrollStitcher(new ScrollStitcherOptions { MaxOutputPixels = 5 }).AddFrame(new PixelBuffer(10, 10)));
    }
}
