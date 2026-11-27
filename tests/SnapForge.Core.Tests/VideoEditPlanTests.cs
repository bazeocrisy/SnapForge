using SnapForge.Core.Video;
using Xunit;

namespace SnapForge.Core.Tests;

public class VideoEditPlanTests
{
    [Fact]
    public void New_plan_keeps_everything()
    {
        var p = new VideoEditPlan(10_000);
        Assert.True(p.IsUnchanged);
        Assert.Equal([new TimeRangeMs(0, 10_000)], p.KeptRanges);
        Assert.Equal(10_000, p.OutputDurationMs);
        Assert.Equal(1234, p.MapToOutput(1234));
    }

    [Fact]
    public void Trim_then_remove_sections_produces_ordered_kept_ranges_and_mapping()
    {
        var p = new VideoEditPlan(60_000);
        p.SetTrim(2_000, 50_000);
        p.RemoveSection(20_000, 10_000); // reversed order is accepted
        p.RemoveSection(30_000, 35_000);
        p.RemoveSection(32_000, 40_000); // overlaps the previous one
        Assert.Equal([new TimeRangeMs(2_000, 10_000), new TimeRangeMs(20_000, 30_000), new TimeRangeMs(40_000, 50_000)], p.KeptRanges);
        Assert.Equal(28_000, p.OutputDurationMs);
        Assert.Null(p.MapToOutput(1_000));
        Assert.Equal(0, p.MapToOutput(2_000));
        Assert.Equal(7_999, p.MapToOutput(9_999));
        Assert.Null(p.MapToOutput(15_000));
        Assert.Equal(8_000, p.MapToOutput(20_000));
        Assert.Equal(18_000, p.MapToOutput(40_000));
        Assert.Null(p.MapToOutput(50_000));
        Assert.Equal(80_000_000, p.MapToOutput100ns(20_000 * 10_000L));
    }

    [Fact]
    public void Audio_blocks_are_cut_at_sample_precision()
    {
        var p = new VideoEditPlan(10_000);
        p.RemoveSection(1_000, 2_000);
        // Block 0.9 s .. 2.1 s: keeps 0.9-1.0 (output 0.9 s) and 2.0-2.1 (output 1.0 s).
        var parts = p.KeptPartsOfBlock(9_000_000, 12_000_000);
        Assert.Equal(2, parts.Count);
        Assert.Equal((0L, 1_000_000L, 9_000_000L), parts[0]);
        Assert.Equal((11_000_000L, 1_000_000L, 10_000_000L), parts[1]);
    }

    [Fact]
    public void Edits_that_leave_nothing_are_rejected_and_not_applied()
    {
        var p = new VideoEditPlan(5_000);
        Assert.Throws<ArgumentException>(() => p.SetTrim(1_000, 1_050));
        Assert.Throws<ArgumentException>(() => p.RemoveSection(0, 4_950));
        Assert.Empty(p.RemovedSections);
        p.RemoveSection(0, 1_000);
        p.UndoLastRemoval();
        Assert.True(p.IsUnchanged);
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoEditPlan(0));
    }

    [Fact]
    public void Removed_sections_are_clipped_to_the_video_and_trim()
    {
        var p = new VideoEditPlan(10_000);
        p.RemoveSection(-500, 1_000);
        p.RemoveSection(9_000, 20_000);
        Assert.Equal([new TimeRangeMs(1_000, 9_000)], p.KeptRanges);
        p.Reset();
        Assert.True(p.IsUnchanged);
    }
}
