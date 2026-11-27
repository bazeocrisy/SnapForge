using SnapForge.App.Video;

namespace SnapForge.App.Tests;

/// <summary>FA-021: the recording state machine — idempotent, race-free transitions.</summary>
public class RecordingLifecycleTests
{
    [Fact]
    public void Normal_session_goes_through_every_phase()
    {
        var life = new RecordingLifecycle();
        var seen = new List<RecordingPhase>();
        life.Changed += (_, p) => seen.Add(p);
        Assert.True(life.TryBeginStart());
        Assert.True(life.TryCompleteStart());
        Assert.True(life.TryPause());
        Assert.True(life.TryResume());
        Assert.True(life.TryBeginStop());
        Assert.True(life.TryBeginFinalizing());
        life.Finish(faulted: false);
        Assert.Equal([RecordingPhase.Starting, RecordingPhase.Recording, RecordingPhase.Paused, RecordingPhase.Recording, RecordingPhase.Stopping, RecordingPhase.Finalizing, RecordingPhase.Idle], seen);
    }

    [Fact]
    public void Double_stop_finalizes_once()
    {
        var life = Recording();
        Assert.True(life.TryBeginStop());
        Assert.False(life.TryBeginStop());
        Assert.False(life.TryBeginCancel()); // a cancel after a stop is ignored: the recording is saved
        Assert.Equal(RecordingPhase.Stopping, life.Phase);
    }

    [Fact]
    public void Concurrent_stop_and_cancel_have_exactly_one_winner()
    {
        for (int round = 0; round < 200; round++)
        {
            var life = Recording();
            int winners = 0;
            using var go = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, 8).Select(i => new Thread(() =>
            {
                go.Wait();
                if (i % 2 == 0 ? life.TryBeginStop() : life.TryBeginCancel())
                {
                    Interlocked.Increment(ref winners);
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            go.Set();
            threads.ForEach(t => t.Join());
            Assert.Equal(1, winners);
            Assert.Equal(RecordingPhase.Stopping, life.Phase);
        }
    }

    [Fact]
    public void Stop_during_starting_abandons_the_start()
    {
        var life = new RecordingLifecycle();
        Assert.True(life.TryBeginStart());
        Assert.False(life.TryBeginStop()); // nothing to finalize yet
        Assert.True(life.StartAbandoned);
        Assert.False(life.TryCompleteStart());
        Assert.Equal(RecordingPhase.Starting, life.Phase);
        life.AbortStart();
        Assert.Equal(RecordingPhase.Idle, life.Phase);
        Assert.True(life.TryBeginStart()); // a new start is possible and not abandoned
        Assert.False(life.StartAbandoned);
    }

    [Fact]
    public void Cancel_during_starting_abandons_the_start()
    {
        var life = new RecordingLifecycle();
        Assert.True(life.TryBeginStart());
        Assert.False(life.TryBeginCancel());
        Assert.True(life.StartAbandoned);
    }

    [Fact]
    public void Second_start_is_refused_while_active()
    {
        var life = Recording();
        Assert.False(life.TryBeginStart());
        Assert.True(life.IsActive);
    }

    [Fact]
    public void Pause_and_resume_are_idempotent()
    {
        var life = Recording();
        Assert.True(life.TryPause());
        Assert.False(life.TryPause());
        Assert.True(life.TryResume());
        Assert.False(life.TryResume());
        Assert.True(life.TryBeginStop());
        Assert.False(life.TryPause()); // no pause while stopping
    }

    [Fact]
    public void Faulted_session_allows_a_new_start()
    {
        var life = Recording();
        Assert.True(life.TryBeginStop());
        life.Finish(faulted: true);
        Assert.Equal(RecordingPhase.Faulted, life.Phase);
        Assert.False(life.IsActive);
        Assert.True(life.TryBeginStart());
    }

    [Fact]
    public void Idle_requests_do_nothing()
    {
        var life = new RecordingLifecycle();
        Assert.False(life.TryBeginStop());
        Assert.False(life.TryBeginCancel());
        Assert.False(life.TryPause());
        Assert.False(life.TryBeginFinalizing());
        life.Finish(faulted: false);
        life.AbortStart();
        Assert.Equal(RecordingPhase.Idle, life.Phase);
    }

    private static RecordingLifecycle Recording()
    {
        var life = new RecordingLifecycle();
        Assert.True(life.TryBeginStart());
        Assert.True(life.TryCompleteStart());
        return life;
    }
}
