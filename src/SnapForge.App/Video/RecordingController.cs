using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Capture;
using SnapForge.App.Dialogs;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Video;

/// <summary>
/// Runs a recording from the UI: SnapForge Studio (set-up), choosing what to record (the same selection overlay as
/// screenshots), opening the camera, optional countdown, recording into the Library, the floating toolbar and
/// outline, hotkeys, and system events — the PC locking pauses the recording, sleep / sign-out / shutdown stop and
/// save it (VID-R10). Every request goes through one state machine (<see cref="RecordingLifecycle"/>, FA-021), so
/// start / pause / resume / stop / cancel / exit are idempotent and never race each other.
/// <para>Entry points for the UI: <see cref="OpenStudio"/> (Record buttons), <see cref="StartAsync"/> (start at once
/// with the saved Studio settings — record hotkey), <see cref="StopAsync"/>, <see cref="TogglePause"/>,
/// <see cref="Cancel"/>, <see cref="IsActive"/> / <see cref="Phase"/> and <see cref="ActiveChanged"/>.</para>
/// </summary>
internal static partial class RecordingController
{
    /// <summary>How long exit / sign-out waits for the recording to stop before leaving it to crash recovery.</summary>
    internal static readonly TimeSpan ExitBudget = TimeSpan.FromSeconds(8);

    /// <summary>How long the sleep notification waits for the file to be completed.</summary>
    internal static readonly TimeSpan SuspendBudget = TimeSpan.FromSeconds(5);

    private static readonly RecordingLifecycle Life = new();
    private static readonly List<(Window Window, WindowState State)> Hidden = [];
    private static Session? _session;
    private static StudioWindow? _studio;
    private static Task _studioCameraReleased = Task.CompletedTask;
    private static Task _ending = Task.CompletedTask;
    private static CameraPreviewWindow? _pendingPreview;

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(long point, uint flags);

    /// <summary>True from the start of the selection step until the file is saved.</summary>
    public static bool IsActive => Life.IsActive;

    /// <summary>The recording phase (Idle, Starting, Recording, Paused, Stopping, Finalizing, Faulted).</summary>
    public static RecordingPhase Phase => Life.Phase;

    /// <summary>Raised on the UI thread whenever <see cref="Phase"/> changes.</summary>
    public static event EventHandler? ActiveChanged;

    /// <summary>
    /// RC3: Camera switched on in the Record panel during this SnapForge Capture session. Starts off in every session:
    /// the camera is only ever used after a deliberate "Camera on" (also for the record hotkey).
    /// </summary>
    internal static bool SessionCamera { get; set; }

    /// <summary>RC3: Mic switched on in the Record panel during this session (starts off, like <see cref="SessionCamera"/>).</summary>
    internal static bool SessionMicrophone { get; set; }

    /// <summary>RC3: the user hid the local camera preview (the recording is unchanged); remembered for this session.</summary>
    internal static bool PreviewHidden { get; set; }

    /// <summary>The camera preview of the running recording, if any (self-tests, screenshots).</summary>
    internal static CameraPreviewWindow? ActivePreview => _session?.Preview;

    /// <summary>
    /// The saved Record settings combined with this session's Camera and Mic switches: camera off = screen only; camera
    /// on = screen + camera, or camera only when "Record the camera only" is chosen; microphone only when Mic is on.
    /// </summary>
    internal static VideoSettings Effective(VideoSettings v) => v with
    {
        StudioMode = !SessionCamera ? StudioMode.Screen : v.StudioMode == StudioMode.Camera ? StudioMode.Camera : StudioMode.ScreenAndCamera,
        RecordMicrophone = SessionMicrophone,
    };

    /// <summary>Creates camera sources (tests may substitute the synthetic camera). Arguments: symbolic link, name.</summary>
    internal static Func<string?, string, ICameraSource>? CameraFactory { get; set; }

    /// <summary>
    /// The Record button of the capture panel and the dock: shows SnapForge Studio (or brings it to the front), or,
    /// while a recording runs, brings its toolbar to the front. Studio's Start button starts the recording.
    /// </summary>
    public static void OpenStudio()
    {
        if (Life.IsActive)
        {
            _session?.Toolbar.Reveal();
            return;
        }

        if (AppServices.Current is not { } services)
        {
            return;
        }

        if (_studio is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        _ = MediaFoundationAvailable(); // MFStartup before Studio lists cameras (Start reports a missing Media Foundation)
        var studio = new StudioWindow(services, CameraFactory);
        _studio = studio;
        studio.StartRequested += (_, _) =>
        {
            _studioCameraReleased = studio.CameraReleased;
            _pendingPreview?.Close();
            _pendingPreview = studio.TakePreview(); // stays on screen: "see yourself" before and during the recording
            studio.Close();
            _ = StartAsync();
        };
        studio.Closed += (_, _) =>
        {
            _studioCameraReleased = studio.CameraReleased;
            if (ReferenceEquals(_studio, studio))
            {
                _studio = null;
            }
        };
        studio.Show();
        studio.Activate();
    }

    /// <summary>Record hotkey (VID-06): starts a recording with the saved Studio settings; while recording, pauses and resumes it.</summary>
    public static void RecordOrPause()
    {
        if (Life.IsRecording)
        {
            TogglePause();
        }
        else if (!Life.IsActive)
        {
            _ = StartAsync();
        }
    }

    /// <summary>Dock Record/Stop button: opens Studio, or stops and saves the running recording.</summary>
    public static void ToggleFromHotkey()
    {
        if (Life.IsRecording)
        {
            _ = StopAsync();
        }
        else
        {
            OpenStudio();
        }
    }

    public static void TogglePause()
    {
        if (_session is not { } s)
        {
            return;
        }

        if (Life.TryPause())
        {
            s.Recorder.Pause();
            s.ReleaseCamera(); // camera light off while paused
            s.UpdatePreview(paused: true);
        }
        else if (Life.TryResume())
        {
            s.Recorder.Resume();
            s.RestartCamera();
            s.UpdatePreview(paused: false);
        }

        RaiseChanged();
        UpdateToolbar();
    }

    /// <summary>Starts a recording at once with the saved Studio settings (mode, area, camera, sound, video).</summary>
    public static async Task StartAsync()
    {
        if (Life.IsActive || CaptureController.IsBusy || AppServices.Current is not { } services)
        {
            _session?.Toolbar.Reveal();
            return;
        }

        VideoSettings v = Effective(services.Settings.Video);
        if (!MediaFoundationAvailable())
        {
            MessageBox.Show("Recording needs the Windows media components (Media Foundation), which are not installed on this PC. " +
                "On Windows \"N\" editions install the Media Feature Pack (Settings → Apps → Optional features); on Windows Server add the Media Foundation feature.",
                "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (v.StudioMode != StudioMode.Camera && !GraphicsCapture.IsSupported())
        {
            MessageBox.Show("Screen recording needs Windows 10 version 2004 or later (Windows 11 recommended). This PC does not offer the Windows screen-capture service.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Life.TryBeginStart())
        {
            return;
        }

        RaiseChanged();
        ICameraSource? camera = null;
        bool handedOver = false;
        try
        {
            if (_studio is { } studio)
            {
                _studioCameraReleased = studio.CameraReleased;
                _pendingPreview?.Close();
                _pendingPreview = studio.TakePreview();
                studio.Close();
            }

            if (v.StudioMode != StudioMode.Screen && _pendingPreview is null)
            {
                // Record hotkey with Camera on: show the preview too.
                _pendingPreview = new CameraPreviewWindow();
                _pendingPreview.SetShape(v.CameraShape, v.CameraMirror);
                _pendingPreview.SetState(CameraPreviewWindow.Placement(v), inRecording: true, placeholder: "Starting the recording…");
                if (!PreviewHidden)
                {
                    _pendingPreview.Show();
                }
            }

            HideOwnWindows();
            await Task.Delay(180).ConfigureAwait(true);
            CaptureSnapshot? snapshot = null;
            CaptureMode mode = CaptureMode.Region;
            PixelRect rect = default;
            if (v.StudioMode != StudioMode.Camera)
            {
                snapshot = ScreenCaptureService.TakeSnapshot(includeCursor: false);
                mode = v.Source switch
                {
                    RecordingSource.Window => CaptureMode.Window,
                    RecordingSource.Display => CaptureMode.SingleScreen,
                    _ => CaptureMode.Region,
                };
                PixelRect? selection = await OverlaySession.RunAsync(snapshot, mode).ConfigureAwait(true);
                Array.Clear(snapshot.Image.Pixels); // the frozen frame is not kept
                if (selection is not { } r || r.Width < 16 || r.Height < 16)
                {
                    if (selection is { })
                    {
                        App.NotifyBalloon("SnapForge", "The selected area is too small to record (at least 16 × 16 pixels).", warning: true);
                    }

                    return;
                }

                rect = r;
            }

            if (Life.StartAbandoned)
            {
                return;
            }

            string? cameraNotice = null;
            if (v.StudioMode != StudioMode.Screen)
            {
                (camera, cameraNotice, bool proceed) = await OpenCameraAsync(v).ConfigureAwait(true);
                if (!proceed)
                {
                    return;
                }

                if (camera is null)
                {
                    ClosePendingPreview(); // recording the screen without the camera, as the user chose
                }
                else
                {
                    _pendingPreview?.SetSource(camera); // you can see yourself during the countdown
                }
            }

            if (Life.StartAbandoned)
            {
                return;
            }

            if (v.Countdown && v.CountdownSeconds > 0 && !await CountdownWindow.RunAsync(v.CountdownSeconds).ConfigureAwait(true))
            {
                return;
            }

            if (Life.StartAbandoned)
            {
                return; // Stop or Cancel was pressed during the countdown
            }

            handedOver = camera is not null;
            Begin(services, v, snapshot, mode, rect, camera, cameraNotice);
            if (!Life.TryCompleteStart())
            {
                // A stop arrived while the recorder was being created: discard it.
                Session s = _session!;
                _session = null;
                _ = Task.Run(() =>
                {
                    s.Recorder.Cancel();
                    s.Recorder.Dispose();
                });
                s.CloseWindows();
                services.Library.AbandonVideo(s.VideoId);
            }
        }
        catch (Exception ex) when (ex is NativeCallException or IOException or UnauthorizedAccessException or InvalidOperationException or CameraException)
        {
            if (_session is { } s)
            {
                _session = null;
                s.CloseWindows();
                _ = Task.Run(s.Recorder.Dispose);
            }

            MessageBox.Show("The recording could not start: " + ex.Message, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!handedOver && camera is { } unused)
            {
                _ = Task.Run(unused.Dispose); // camera light off
            }

            if (Life.Phase == RecordingPhase.Starting)
            {
                Life.AbortStart();
            }

            if (_session is null)
            {
                ClosePendingPreview();
                RestoreOwnWindows();
            }

            RaiseChanged();
        }
    }

    private static void ClosePendingPreview()
    {
        _pendingPreview?.Close();
        _pendingPreview = null;
    }

    /// <summary>
    /// Opens the camera for Screen + camera / Camera only. On a problem the user is told what happened and how to fix
    /// it (privacy settings, another app using the camera) — nothing is retried silently. Screen + camera can continue
    /// without the camera if the user agrees. Returns (camera or null, notice for the toolbar, continue?).
    /// </summary>
    private static async Task<(ICameraSource? Camera, string? Notice, bool Proceed)> OpenCameraAsync(VideoSettings v)
    {
        bool screenToo = v.StudioMode == StudioMode.ScreenAndCamera;
        string? notice = null;
        ICameraSource? camera = null;
        try
        {
            // The Studio preview may still be releasing the same device.
            await Task.WhenAny(_studioCameraReleased, Task.Delay(TimeSpan.FromSeconds(8))).ConfigureAwait(true);
            if (PrivacySettings.CameraBlocked())
            {
                throw new CameraException(CameraFailure.AccessDenied, PrivacySettings.CameraBlockedMessage, CameraException.E_ACCESSDENIED);
            }

            IReadOnlyList<Mf.VideoDevice> devices = CameraFactory is null ? MediaFoundationCameraSource.Enumerate() : [new Mf.VideoDevice("Camera", v.CameraDeviceId ?? string.Empty)];
            if (devices.Count == 0)
            {
                throw new CameraException(CameraFailure.NoCamera, "No camera was found. Connect a camera, or choose Screen in SnapForge Studio.");
            }

            Mf.VideoDevice device = devices.FirstOrDefault(d => string.Equals(d.SymbolicLink, v.CameraDeviceId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(device.SymbolicLink))
            {
                device = devices[0];
                if (v.CameraDeviceId is not null && CameraFactory is null)
                {
                    notice = $"The chosen camera is not connected; recording with {device.Name}.";
                }
            }

            camera = (CameraFactory ?? ((id, name) => new MediaFoundationCameraSource(id, name)))(device.SymbolicLink is { Length: > 0 } link ? link : null, device.Name);
            ICameraSource starting = camera;
            await Task.Run(starting.Start).ConfigureAwait(true);
            return (camera, notice, true);
        }
        catch (CameraException ex)
        {
            if (camera is { } failed)
            {
                _ = Task.Run(failed.Dispose);
            }

            return (null, null, ReportCameraProblem(ex, screenToo));
        }
        catch (NativeCallException ex)
        {
            if (camera is { } failed)
            {
                _ = Task.Run(failed.Dispose);
            }

            return (null, null, ReportCameraProblem(CameraException.FromHResult(ex.HResult, "The camera"), screenToo));
        }
    }

    /// <summary>Explains a camera problem. Returns true if the user chose to record the screen without the camera.</summary>
    private static bool ReportCameraProblem(CameraException ex, bool screenToo)
    {
        RestoreOwnWindows();
        bool privacy = ex.Kind == CameraFailure.AccessDenied;
        if (screenToo)
        {
            string text = ex.Message + "\n\n" + (privacy
                ? "Yes — record the screen without the camera.\nNo — open the camera privacy settings.\nCancel — do not record."
                : "Record the screen without the camera?");
            MessageBoxResult answer = MessageBox.Show(text, "SnapForge Studio", privacy ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.No && privacy)
            {
                PrivacySettings.Open(PrivacySettings.CameraUri);
            }

            bool withoutCamera = answer == MessageBoxResult.Yes;
            if (withoutCamera)
            {
                HideOwnWindows();
            }

            return withoutCamera;
        }

        if (privacy)
        {
            if (MessageBox.Show(ex.Message + "\n\nOpen the camera privacy settings now?", "SnapForge Studio", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                PrivacySettings.Open(PrivacySettings.CameraUri);
            }
        }
        else
        {
            MessageBox.Show(ex.Message, "SnapForge Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return false;
    }

    private static void Begin(AppServices services, VideoSettings v, CaptureSnapshot? snapshot, CaptureMode mode, PixelRect rect, ICameraSource? camera, string? cameraNotice)
    {
        IFrameSource video;
        CompositeFrameSource? composite = null;
        PixelRect? recorded = null;
        PixelRect work;
        bool windowTarget = false;
        if (v.StudioMode == StudioMode.Camera)
        {
            video = new CameraFrameSource(camera!, v.CameraMirror);
            ScreenLayout layout = PhysicalPlacement.Layout();
            MonitorInfo primary = layout.Monitors.FirstOrDefault(m => m.IsPrimary) ?? layout.Monitors[0];
            work = primary.WorkArea;
        }
        else
        {
            ScreenLayout layout = snapshot!.Layout;
            MonitorInfo monitor = layout.Monitors.OrderByDescending(m => m.Bounds.Intersect(rect).Area).First();
            work = monitor.WorkArea;
            ScreenFrameSource screen;
            // Window mode: the overlay returns the window's rectangle clipped to the desktop, so match the frontmost
            // window that contains the selection's centre and covers it (exact equality fails for maximised windows).
            int cx = rect.X + (rect.Width / 2), cy = rect.Y + (rect.Height / 2);
            WindowCandidate? window = mode == CaptureMode.Window
                ? snapshot.Windows.Where(w => w.Bounds.Contains(cx, cy) && w.Bounds.Intersect(rect) == rect).OrderBy(w => w.ZOrder).FirstOrDefault()
                : null;
            try
            {
                if (window is not null)
                {
                    screen = new ScreenFrameSource(window.Handle, isWindow: true, crop: null, v.CaptureCursor);
                    recorded = rect;
                    windowTarget = true;
                }
                else
                {
                    // Region / display: recorded on the monitor holding most of it (a region spanning monitors is clipped).
                    PixelRect r = rect.Intersect(monitor.Bounds);
                    recorded = r;
                    nint hmon = MonitorFromPoint(PackPoint(monitor.Bounds.X + (monitor.Bounds.Width / 2), monitor.Bounds.Y + (monitor.Bounds.Height / 2)), 2 /* NEAREST */);
                    screen = new ScreenFrameSource(hmon, isWindow: false, crop: (r.X - monitor.Bounds.X, r.Y - monitor.Bounds.Y, r.Width, r.Height), v.CaptureCursor);
                }
            }
            catch
            {
                camera?.Dispose();
                throw;
            }

            video = camera is null ? screen : composite = new CompositeFrameSource(screen, camera, v.CameraShape, v.CameraSize, v.CameraCorner, v.CameraMirror);
        }

        IAudioSource[] audio = v.RecordSystemAudio || v.RecordMicrophone
            ? [new WasapiSource(ScreenRecorder.AudioNames.System, loopback: true, null), new WasapiSource(ScreenRecorder.AudioNames.Microphone, loopback: false, v.MicrophoneDeviceId)]
            : [];
        var options = new RecorderOptions(v.FramesPerSecond, v.Quality, v.RecordSystemAudio, v.RecordMicrophone, v.MicrophoneDeviceId, v.MinimumFreeSpaceMegabytes * 1024L * 1024, v.LowSpaceWarningMinutes);
        DateTime startedUtc = DateTime.UtcNow;
        (Guid id, string path) = services.Library.BeginVideo(startedUtc);
        ScreenRecorder recorder;
        try
        {
            recorder = ScreenRecorder.Create(video, audio, options, path);
        }
        catch
        {
            video.Dispose(); // also releases the camera
            foreach (IAudioSource a in audio)
            {
                a.Dispose();
            }

            services.Library.AbandonVideo(id);
            throw;
        }

        RecordingLog.FilePath ??= Path.Combine(Path.GetDirectoryName(services.SettingsPath) ?? Path.GetTempPath(), "recording.log");
        string format = $"{recorder.Width}×{recorder.Height} · {v.FramesPerSecond} FPS";
        var toolbar = new RecordingToolbar(format, options.AudioTrack, v.RecordSystemAudio, v.RecordMicrophone,
            v.StopHotkey is { } st ? st.ToString() : null, v.RecordHotkey is { } p ? p.ToString() : null, cameraOverlay: composite is not null);
        var session = new Session(recorder, toolbar, id, startedUtc, camera, composite, v.StudioMode == StudioMode.Camera)
        {
            Preview = camera is null ? null : _pendingPreview,
            Placement = CameraPreviewWindow.Placement(v),
        };
        if (camera is null)
        {
            ClosePendingPreview();
        }

        _pendingPreview = null;
        _session = session;
        if (session.Preview is { } preview)
        {
            preview.SetSource(camera);
            preview.HideRequested += (_, _) => SetPreviewVisible(false);
            toolbar.SetPreview(available: true, visible: !PreviewHidden);
            toolbar.PreviewToggled += (_, on) => SetPreviewVisible(on);
            session.UpdatePreview(paused: false);
        }

        recorder.Notice += (_, message) => Application.Current.Dispatcher.BeginInvoke(() => session.Toolbar.ShowStatus(message));
        recorder.StoppedBySelf += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => _ = StopAsync());
        if (camera is not null)
        {
            camera.Lost += (_, reason) => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Screen + camera: the recording continues without the overlay. Camera only: the recorder stops by itself.
                session.Toolbar.ShowStatus(reason);
                session.Toolbar.SetCameraUnavailable(reason);
                session.Preview?.SetSource(null);
                session.Preview?.SetState("Camera disconnected — not in the recording from here on", inRecording: false, placeholder: reason);
            });
        }

        if (recorded is { } area && !windowTarget)
        {
            session.Outline = new RecordingOutline(area);
            session.Outline.Show();
        }

        toolbar.PauseClicked += (_, _) => TogglePause();
        toolbar.StopClicked += (_, _) => _ = StopAsync();
        toolbar.CancelClicked += (_, _) => CancelWithConfirmation();
        toolbar.SystemToggled += (_, on) => session.Recorder.SetSourceEnabled(0, on);
        toolbar.MicToggled += (_, on) => session.Recorder.SetSourceEnabled(1, on);
        toolbar.CameraToggled += (_, on) =>
        {
            session.SetOverlayVisible(on);
            session.UpdatePreview(Life.Phase == RecordingPhase.Paused);
        };
        toolbar.Show();
        toolbar.PlaceNear(recorded ?? new PixelRect(work.X + (work.Width / 2) - 1, work.Bottom - 120, 2, 2), work);
        if (!toolbar.Excluded)
        {
            toolbar.ShowStatus("This version of Windows cannot hide the toolbar from recordings; it is placed outside the recorded area where possible.");
        }
        else if (cameraNotice is not null)
        {
            toolbar.ShowStatus(cameraNotice);
        }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        session.Timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) => UpdateToolbar(), Application.Current.Dispatcher);
        recorder.Start();
        session.Timer.Start();
    }

    /// <summary>
    /// Stops, saves the MP4 into the Library and opens it. Idempotent: while a stop is in progress, further calls
    /// return the same task; during the start-up steps the start is abandoned instead.
    /// </summary>
    public static Task StopAsync(string? reason = null)
    {
        if (!Life.TryBeginStop() || _session is not { } session)
        {
            return _ending;
        }

        RaiseChanged();
        _ending = StopCoreAsync(session, reason);
        return _ending;
    }

    private static async Task StopCoreAsync(Session session, string? reason)
    {
        AppServices? services = AppServices.Current;
        session.Toolbar.ShowSaving();
        Task<RecordingResult> work = Task.Run(() => session.Recorder.Stop(reason));
        session.StopWork = work;
        RecordingResult result = await work.ConfigureAwait(true);
        Life.TryBeginFinalizing();
        RaiseChanged();
        CaptureRecord? saved = null;
        string message;
        if (services is null || !session.TryClaimCompletion())
        {
            message = "Recording saved."; // completed by the exit path
            saved = services?.Library.Find(session.VideoId);
        }
        else if (result.Stalled)
        {
            message = "The recording could not be finished normally because part of the recorder stopped responding. What was recorded is kept and will be recovered" +
                " as soon as the recorder lets go of the file (or the next time SnapForge starts).";
            Guid id = session.VideoId;
            _ = session.Recorder.ThreadsExited.ContinueWith(
                _ => Application.Current?.Dispatcher.BeginInvoke(() => _ = RecoverLateAsync(services, id)),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        else
        {
            try
            {
                if (result.Finalized)
                {
                    var info = new VideoInfo(result.Width, result.Height, result.FramesPerSecond, result.DurationMs, result.HasAudio);
                    saved = await services.Writer.RunAsync(() => services.Library.CompleteVideo(session.VideoId, info, result.FirstFrame, session.StartedUtc)).ConfigureAwait(true);
                    message = $"Recording saved: {TimeSpan.FromMilliseconds(result.DurationMs):h\\:mm\\:ss}, {result.Width}×{result.Height}.";
                }
                else
                {
                    // The file could not be completed (e.g. disk full): recover it like after a crash.
                    IReadOnlyList<RecoveryOutcome> outcomes = await services.Writer.RunAsync(() => RecordingRecovery.RecoverAll(services.Library)).ConfigureAwait(true);
                    message = outcomes.FirstOrDefault(o => o.Id == session.VideoId)?.Message ?? "The recording could not be saved.";
                    saved = services.Library.Find(session.VideoId);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                message = "The recording could not be added to the Library: " + ex.Message + " The file is kept and will be offered for recovery at the next start.";
            }
        }

        if (result.EndReason is { } why)
        {
            message = why + " " + message;
        }

        Cleanup(session);
        RestoreOwnWindows();
        string? outside = null;
        if (services is not null && saved is { } inLibrary && !services.Settings.Video.SaveToLibrary)
        {
            (CaptureRecord? kept, string note, outside) = await SaveOutsideLibraryAsync(services, inLibrary).ConfigureAwait(true);
            saved = kept;
            message = kept is null ? note : message + " " + note;
        }

        Life.Finish(faulted: !result.Finalized);
        RaiseChanged();
        bool ok = result.Finalized && (saved is not null || outside is not null);
        string? file = outside ?? (services is null ? null : ExistingVideoFile(services, saved?.Id ?? session.VideoId));
        if (ok && saved is not null && services?.Settings.Video.OpenAfterRecording == true)
        {
            App.NotifyBalloon("SnapForge recording", message, warning: result.EndReason is not null);
            App.OpenInEditor(new HandoffRequest(Handoff.EditVideo, saved.Id));
            return;
        }

        string title = ok ? "Recording saved" : result.Stalled ? "Recording is being recovered" : "The recording could not be finished";
        RecordingSavedWindow.ShowNew(title, message, ok ? saved?.Id : null, file, ok);
    }

    /// <summary>The recording's MP4 in the Library, if the file exists (also for one kept for recovery).</summary>
    private static string? ExistingVideoFile(AppServices services, Guid id)
    {
        try
        {
            string path = services.Library.VideoPath(id);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>After a stalled stop: once the recorder threads have exited, recover the file now.</summary>
    private static async Task RecoverLateAsync(AppServices services, Guid id)
    {
        try
        {
            IReadOnlyList<RecoveryOutcome> outcomes = await services.Writer.RunAsync(() => RecordingRecovery.RecoverAll(services.Library)).ConfigureAwait(true);
            if (outcomes.FirstOrDefault(o => o.Id == id) is { } outcome)
            {
                App.NotifyBalloon("SnapForge recording", outcome.Message, warning: !outcome.Recovered);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RecordingLog.Write("late recovery failed: " + ex.Message);
        }
    }

    /// <summary>
    /// VID-08 "without history" (FA-022): asks where to save, copies the MP4 there through a verified temporary file
    /// and an atomic replace, and only then removes it from the Library. Returns (null, note) when it was saved
    /// outside, or (the record, note) if it stays in the Library (dialog cancelled, or the copy failed — then the
    /// Library copy and any existing file at the destination are untouched).
    /// </summary>
    private static async Task<(CaptureRecord? Record, string Note, string? Path)> SaveOutsideLibraryAsync(AppServices services, CaptureRecord saved)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save recording",
            Filter = "MP4 video (*.mp4)|*.mp4",
            FileName = Core.Export.ExportFileNames.For(saved.Title, saved.CreatedUtc.ToLocalTime(), ".mp4", new HashSet<string>()),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return (saved, "It was kept in the Library because no location was chosen.", null);
        }

        string target = dialog.FileName;
        string source = services.Library.VideoPath(saved.Id);
        try
        {
            await Task.Run(() => VerifiedFileCopy.Copy(source, target)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"The recording could not be saved to {target}:\n\n{ex.Message}\n\nIt was kept in the Library, and nothing at that location was changed.",
                "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return (saved, "It could not be saved to the chosen location and was kept in the Library.", null);
        }

        try
        {
            await services.Writer.RunAsync(() => services.Library.DeletePermanently([saved.Id])).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (saved, $"Saved to {target}; the Library copy could not be removed ({ex.Message}).", target);
        }

        return (null, $"Recording saved to {target} (not kept in the Library).", target);
    }

    private static void CancelWithConfirmation()
    {
        if (_session is not { } s || !Life.IsRecording)
        {
            return;
        }

        bool paused = Life.TryPause();
        if (paused)
        {
            s.Recorder.Pause();
        }

        MessageBoxResult answer = MessageBox.Show(s.Toolbar, "Discard this recording? It will not be saved.", "SnapForge", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            if (paused && Life.TryResume())
            {
                s.Recorder.Resume();
            }

            UpdateToolbar();
            return;
        }

        Cancel();
    }

    /// <summary>Discards the running recording without asking (cancel hotkey, or the confirmation above). Ignored once a stop has begun.</summary>
    public static void Cancel()
    {
        if (!Life.TryBeginCancel() || _session is not { } session)
        {
            return;
        }

        RaiseChanged();
        _ending = CancelCoreAsync(session);
    }

    private static async Task CancelCoreAsync(Session session)
    {
        session.Toolbar.ShowSaving();
        Task<RecordingResult> work = Task.Run(() =>
        {
            session.Recorder.Cancel();
            return session.Recorder.Stop(); // the cancel's result (Stop after Cancel returns it)
        });
        session.StopWork = work;
        RecordingResult result = await work.ConfigureAwait(true);
        AppServices? services = AppServices.Current;
        session.TryClaimCompletion();
        if (services is not null)
        {
            if (result.Stalled)
            {
                Guid id = session.VideoId;
                _ = session.Recorder.ThreadsExited.ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(() => Abandon(services, id)), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
            else
            {
                Abandon(services, session.VideoId);
            }
        }

        Cleanup(session);
        RestoreOwnWindows();
        Life.Finish(faulted: false);
        RaiseChanged();
    }

    private static void Abandon(AppServices services, Guid id)
    {
        try
        {
            services.Library.AbandonVideo(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecordingLog.Write($"the cancelled recording {id} could not be removed yet: {ex.Message}");
        }
    }

    /// <summary>
    /// On application exit / sign-out: stop and save, waiting at most <see cref="ExitBudget"/> on the UI thread. If
    /// the recorder does not stop in time, the fragmented MP4 is left in the Library for crash recovery at the next
    /// start (it is never lost or half-converted).
    /// </summary>
    public static void StopForExit()
    {
        _studio?.Close();
        ClosePendingPreview();
        if (Life.Phase == RecordingPhase.Starting)
        {
            Life.TryBeginCancel(); // nothing was recorded yet: the start is abandoned
            return;
        }

        if (_session is not { } session || AppServices.Current is not { } services)
        {
            return;
        }

        if (Life.TryBeginStop())
        {
            session.StopWork = Task.Run(() => session.Recorder.Stop("SnapForge was closed while recording."));
        }

        if (session.StopWork is not { } work)
        {
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (!work.Wait(ExitBudget))
        {
            RecordingLog.Write($"exit: the recording did not stop within {ExitBudget.TotalSeconds:0} s; the file is left for crash recovery at the next start.");
            session.CloseWindows();
            return;
        }

        RecordingResult r = work.GetAwaiter().GetResult();
        if (r.Cancelled)
        {
            Abandon(services, session.VideoId); // a discard in progress at exit stays discarded (not "recovered" later)
        }

        if (r.Finalized && !r.Cancelled && session.TryClaimCompletion())
        {
            var info = new VideoInfo(r.Width, r.Height, r.FramesPerSecond, r.DurationMs, r.HasAudio);
            Task complete = Task.Run(() => services.Library.CompleteVideo(session.VideoId, info, r.FirstFrame, session.StartedUtc));
            TimeSpan left = ExitBudget - clock.Elapsed;
            try
            {
                if (!complete.Wait(left > TimeSpan.FromSeconds(15) ? left : TimeSpan.FromSeconds(15)))
                {
                    RecordingLog.Write("exit: saving the recording to the Library did not finish in time; it is recovered at the next start.");
                }
            }
            catch (AggregateException ex)
            {
                RecordingLog.Write("exit: the recording could not be added to the Library: " + ex.GetBaseException().Message);
            }
        }

        Cleanup(session);
    }

    private static void UpdateToolbar()
    {
        if (_session is { } s && Life.IsRecording)
        {
            s.Toolbar.Update(s.Recorder.Elapsed, Life.Phase == RecordingPhase.Paused, s.Recorder.Level(0), s.Recorder.Level(1));
        }
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_session is { } s && Life.TryPause())
                {
                    s.Recorder.Pause();
                    s.ReleaseCamera();
                    s.Toolbar.ShowStatus("Paused because the PC was locked. Press Resume to continue.");
                    RaiseChanged();
                    UpdateToolbar();
                }
            });
        }
    }

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            // Called before sleep: finish the file now (WGC sessions and cameras do not survive sleep reliably). The
            // wait is bounded and does not need the UI thread (the recorder threads do the work).
            Application.Current.Dispatcher.Invoke(() =>
            {
                _ = StopAsync("Recording stopped because the PC went to sleep.");
                if (_session?.StopWork is { } work && !work.Wait(SuspendBudget))
                {
                    RecordingLog.Write("sleep: the recording did not stop before the PC suspended; it is completed or recovered afterwards.");
                }
            });
        }
    }

    private static void OnSessionEnding(object? sender, SessionEndingEventArgs e) =>
        Application.Current.Dispatcher.Invoke(StopForExit);

    private static void Cleanup(Session session)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        session.CloseWindows();
        if (ReferenceEquals(_session, session))
        {
            _session = null;
        }

        // Off the UI thread: releasing the camera / capture can take a moment (and waits for stalled threads).
        _ = Task.Run(session.Recorder.Dispose);
    }

    /// <summary>Shows or hides the local camera preview; the recording itself is not changed.</summary>
    internal static void SetPreviewVisible(bool visible)
    {
        PreviewHidden = !visible;
        if (_session is not { Preview: { } preview } s)
        {
            return;
        }

        if (visible)
        {
            preview.Show();
            preview.KeepOnScreen();
        }
        else
        {
            preview.Hide();
        }

        s.Toolbar.SetPreview(available: true, visible: visible);
    }

    /// <summary>Notification-area menu: brings the recording controls (and the preview, unless hidden) back on screen.</summary>
    internal static void ShowControls()
    {
        if (_session is not { } s)
        {
            OpenStudio();
            return;
        }

        s.Toolbar.Reveal();
        if (!PreviewHidden && s.Preview is { } p)
        {
            p.Show();
            p.KeepOnScreen();
        }
    }

    /// <summary>True while the recording runs or is paused (not while starting or saving).</summary>
    internal static bool IsRecording => Life.IsRecording;

    /// <summary>True while the running recording has a camera preview.</summary>
    internal static bool HasPreview => _session?.Preview is not null;

    private static void RaiseChanged() => ActiveChanged?.Invoke(null, EventArgs.Empty);

    private static void HideOwnWindows()
    {
        Hidden.Clear();
        foreach (Window w in Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible && w is not CameraPreviewWindow).ToList())
        {
            Hidden.Add((w, w.WindowState));
            w.Hide();
        }
    }

    private static void RestoreOwnWindows()
    {
        foreach ((Window w, WindowState state) in Hidden)
        {
            if (w.IsLoaded)
            {
                w.Show();
                w.WindowState = state == WindowState.Minimized ? WindowState.Normal : state;
            }
        }

        Hidden.Clear();
    }

    /// <summary>True if Media Foundation is present and starts (absent on Windows N editions without the Media Feature Pack).</summary>
    internal static bool MediaFoundationAvailable()
    {
        try
        {
            return Mf.MFStartup(Mf.MF_VERSION, 0) >= 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static long PackPoint(int x, int y) => ((long)y << 32) | (uint)x;

    /// <summary>One recording from Begin to Cleanup: recorder, windows, camera and the Library entry.</summary>
    private sealed class Session(ScreenRecorder recorder, RecordingToolbar toolbar, Guid videoId, DateTime startedUtc, ICameraSource? camera, CompositeFrameSource? composite, bool cameraOnly)
    {
        private int _completionClaimed;
        private bool _overlayWanted = true;

        public ScreenRecorder Recorder { get; } = recorder;

        public RecordingToolbar Toolbar { get; } = toolbar;

        public RecordingOutline? Outline { get; set; }

        /// <summary>RC3: the floating camera preview (camera on), closed with the other recording windows.</summary>
        public CameraPreviewWindow? Preview { get; init; }

        /// <summary>Where the camera appears in the recording (preview caption).</summary>
        public string Placement { get; init; } = string.Empty;

        /// <summary>Tells the preview whether the camera is in the recording right now.</summary>
        public void UpdatePreview(bool paused)
        {
            if (Preview is not { } p || camera is not { } c || c.LostReason is not null)
            {
                return;
            }

            if (paused)
            {
                p.SetState("Paused — nothing is being recorded", inRecording: false, placeholder: "The camera is off while the recording is paused.");
            }
            else if (composite is not null && !_overlayWanted)
            {
                p.SetState("Camera hidden — not in the recording", inRecording: false, placeholder: "Turn \"Camera in the recording\" back on under More in the recording controls.");
            }
            else
            {
                p.SetState("● Recording · " + Placement.Replace("In the recording · ", "camera at the ", StringComparison.Ordinal), inRecording: true);
            }
        }

        public DispatcherTimer? Timer { get; set; }

        public Guid VideoId { get; } = videoId;

        public DateTime StartedUtc { get; } = startedUtc;

        /// <summary>The recorder's Stop/Cancel work once begun (exit and sleep wait on it, bounded).</summary>
        public Task<RecordingResult>? StopWork { get; set; }

        /// <summary>True for exactly one caller: whoever adds the file to the Library (stop path or exit path).</summary>
        public bool TryClaimCompletion() => Interlocked.Exchange(ref _completionClaimed, 1) == 0;

        /// <summary>Toolbar camera toggle: hiding the overlay also releases the camera (light off); showing reopens it.</summary>
        public void SetOverlayVisible(bool on)
        {
            _overlayWanted = on;
            if (composite is not null)
            {
                composite.OverlayVisible = on;
            }

            if (on)
            {
                RestartCamera();
            }
            else
            {
                ReleaseCamera();
            }
        }

        public void ReleaseCamera()
        {
            if (camera is { } c)
            {
                _ = Task.Run(c.Stop);
            }
        }

        public void RestartCamera()
        {
            if (camera is not { LostReason: null } c || (composite is not null && !_overlayWanted))
            {
                return;
            }

            _ = Task.Run(c.Start).ContinueWith(t =>
            {
                if (t.Exception?.GetBaseException() is { } error)
                {
                    string reason = (error as CameraException)?.Message ?? error.Message;
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        Toolbar.ShowStatus("The camera could not be turned back on: " + reason);
                        Toolbar.SetCameraUnavailable(reason);
                        if (cameraOnly)
                        {
                            _ = StopAsync("The camera could not be turned back on after the pause.");
                        }
                    });
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        public void CloseWindows()
        {
            Timer?.Stop();
            Timer = null;
            Toolbar.Close();
            Outline?.Close();
            Outline = null;
            Preview?.SetSource(null);
            Preview?.Close();
        }
    }
}
