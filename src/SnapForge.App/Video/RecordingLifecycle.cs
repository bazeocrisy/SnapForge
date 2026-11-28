namespace SnapForge.App.Video;

/// <summary>Phase of the recording session as the UI sees it (FA-021).</summary>
internal enum RecordingPhase
{
    /// <summary>Nothing is recording (Studio may be open).</summary>
    Idle,

    /// <summary>Area selection, camera opening and countdown; nothing is written yet.</summary>
    Starting,

    Recording,

    Paused,

    /// <summary>Stop was requested; recorder threads are being joined (bounded).</summary>
    Stopping,

    /// <summary>The file is complete; it is being added to the Library / saved where the user chose.</summary>
    Finalizing,

    /// <summary>The last recording could not be finished normally (stalled thread, write failure); a new one may start.</summary>
    Faulted,
}

/// <summary>
/// The single state machine behind start / pause / resume / stop / cancel / exit (FA-021). Every transition is
/// atomic under one lock and every request is idempotent: two Stops give one finalization, a Cancel after a Stop
/// is ignored, a Stop during Starting aborts the start. Pure logic (no UI, no native code) so it is unit-tested.
/// </summary>
internal sealed class RecordingLifecycle
{
    private readonly Lock _gate = new();
    private RecordingPhase _phase = RecordingPhase.Idle;
    private bool _stopRequestedWhileStarting;
    private bool _cancelRequestedWhileStarting;

    public event EventHandler<RecordingPhase>? Changed;

    public RecordingPhase Phase
    {
        get
        {
            lock (_gate)
            {
                return _phase;
            }
        }
    }

    /// <summary>True from Starting until the file is finalized.</summary>
    public bool IsActive => Phase is RecordingPhase.Starting or RecordingPhase.Recording or RecordingPhase.Paused or RecordingPhase.Stopping or RecordingPhase.Finalizing;

    /// <summary>True while the recorder exists and runs (Recording or Paused).</summary>
    public bool IsRecording => Phase is RecordingPhase.Recording or RecordingPhase.Paused;

    /// <summary>Idle/Faulted → Starting. False if a session is already active.</summary>
    public bool TryBeginStart() => Transition(p => p is RecordingPhase.Idle or RecordingPhase.Faulted, RecordingPhase.Starting, () =>
    {
        _stopRequestedWhileStarting = false;
        _cancelRequestedWhileStarting = false;
    });

    /// <summary>True if Stop or Cancel was requested while starting: the start must be abandoned.</summary>
    public bool StartAbandoned
    {
        get
        {
            lock (_gate)
            {
                return _stopRequestedWhileStarting || _cancelRequestedWhileStarting;
            }
        }
    }

    /// <summary>Starting → Idle (selection cancelled, camera refused, countdown cancelled, start failed).</summary>
    public void AbortStart() => Transition(p => p == RecordingPhase.Starting, RecordingPhase.Idle);

    /// <summary>
    /// Starting → Recording once the recorder runs. Returns false (phase unchanged, still Starting) if a Stop or
    /// Cancel arrived meanwhile: the caller then discards the just-started recorder and calls <see cref="AbortStart"/>.
    /// </summary>
    public bool TryCompleteStart()
    {
        lock (_gate)
        {
            if (_phase != RecordingPhase.Starting || _stopRequestedWhileStarting || _cancelRequestedWhileStarting)
            {
                return false;
            }
        }

        return Transition(p => p == RecordingPhase.Starting, RecordingPhase.Recording);
    }

    public bool TryPause() => Transition(p => p == RecordingPhase.Recording, RecordingPhase.Paused);

    public bool TryResume() => Transition(p => p == RecordingPhase.Paused, RecordingPhase.Recording);

    /// <summary>
    /// Recording/Paused → Stopping: true exactly once per recording (the caller finalizes). During Starting the stop
    /// is remembered (the start is abandoned) and false is returned. Otherwise false (already stopping, or idle).
    /// </summary>
    public bool TryBeginStop()
    {
        lock (_gate)
        {
            if (_phase == RecordingPhase.Starting)
            {
                _stopRequestedWhileStarting = true;
                return false;
            }
        }

        return Transition(p => p is RecordingPhase.Recording or RecordingPhase.Paused, RecordingPhase.Stopping);
    }

    /// <summary>
    /// Recording/Paused → Stopping for a discard: true exactly once, and only if no Stop came first (a cancel after
    /// a stop is ignored: the recording is saved). During Starting the start is abandoned.
    /// </summary>
    public bool TryBeginCancel()
    {
        lock (_gate)
        {
            if (_phase == RecordingPhase.Starting)
            {
                _cancelRequestedWhileStarting = true;
                return false;
            }
        }

        return Transition(p => p is RecordingPhase.Recording or RecordingPhase.Paused, RecordingPhase.Stopping);
    }

    /// <summary>Stopping → Finalizing (recorder threads joined; the file is being saved).</summary>
    public bool TryBeginFinalizing() => Transition(p => p == RecordingPhase.Stopping, RecordingPhase.Finalizing);

    /// <summary>Stopping/Finalizing → Idle (done) or Faulted (not finished normally).</summary>
    public void Finish(bool faulted) =>
        Transition(p => p is RecordingPhase.Stopping or RecordingPhase.Finalizing, faulted ? RecordingPhase.Faulted : RecordingPhase.Idle);

    private bool Transition(Func<RecordingPhase, bool> allowed, RecordingPhase to, Action? also = null)
    {
        lock (_gate)
        {
            if (!allowed(_phase))
            {
                return false;
            }

            _phase = to;
            also?.Invoke();
        }

        Changed?.Invoke(this, to);
        return true;
    }
}
