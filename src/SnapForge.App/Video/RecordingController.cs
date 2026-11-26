using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Capture;
using SnapForge.App.Dialogs;
using SnapForge.App.Services;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Video;

/// <summary>
/// Runs a screen recording from the UI: choose what to record (the same selection overlay as screenshots), optional
/// countdown, recording into the Library, the floating toolbar and outline, hotkeys, and system events — the PC
/// locking pauses the recording, sleep / sign-out / shutdown stop and save it (VID-R10).
/// </summary>
internal static partial class RecordingController
{
    private static ScreenRecorder? _recorder;
    private static RecordingToolbar? _toolbar;
    private static RecordingOutline? _outline;
    private static DispatcherTimer? _timer;
    private static Guid _videoId;
    private static DateTime _startedUtc;
    private static bool _starting;
    private static bool _stopping;
    private static readonly List<(Window Window, WindowState State)> Hidden = [];

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(long point, uint flags);

    /// <summary>True from the selection step until the file is saved.</summary>
    public static bool IsActive => _starting || _recorder is not null;

    public static event EventHandler? ActiveChanged;

    /// <summary>Record hotkey (VID-06): starts a recording; while recording, pauses and resumes it.</summary>
    public static void RecordOrPause()
    {
        if (_recorder is not null)
        {
            TogglePause();
        }
        else
        {
            _ = StartAsync();
        }
    }

    /// <summary>Widget Record/Stop button: starts a recording, or stops and saves the running one.</summary>
    public static void ToggleFromHotkey()
    {
        if (_recorder is not null)
        {
            _ = StopAsync();
        }
        else
        {
            _ = StartAsync();
        }
    }

    public static void TogglePause()
    {
        if (_recorder is null)
        {
            return;
        }

        if (_recorder.State == RecorderState.Paused)
        {
            _recorder.Resume();
        }
        else
        {
            _recorder.Pause();
        }

        UpdateToolbar();
    }

    public static async Task StartAsync()
    {
        if (IsActive || CaptureController.IsBusy || AppServices.Current is not { } services)
        {
            _toolbar?.Activate();
            return;
        }

        _ = Mf.MFStartup(Mf.MF_VERSION, 0);
        if (!GraphicsCapture.IsSupported())
        {
            MessageBox.Show("Screen recording needs Windows 10 version 2004 or later (Windows 11 recommended). This PC does not offer the Windows screen-capture service.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        VideoSettings v = services.Settings.Video;
        _starting = true;
        ActiveChanged?.Invoke(null, EventArgs.Empty);
        try
        {
            HideOwnWindows();
            await Task.Delay(180).ConfigureAwait(true);
            CaptureSnapshot snapshot = ScreenCaptureService.TakeSnapshot(includeCursor: false);
            CaptureMode mode = v.Source switch
            {
                RecordingSource.Window => CaptureMode.Window,
                RecordingSource.Display => CaptureMode.SingleScreen,
                _ => CaptureMode.Region,
            };
            PixelRect? selection = await OverlaySession.RunAsync(snapshot, mode).ConfigureAwait(true);
            Array.Clear(snapshot.Image.Pixels); // the frozen frame is not kept
            if (selection is not { } rect || rect.Width < 16 || rect.Height < 16)
            {
                if (selection is { })
                {
                    App.NotifyBalloon("SnapForge", "The selected area is too small to record (at least 16 × 16 pixels).", warning: true);
                }

                return;
            }

            if (v.Countdown && v.CountdownSeconds > 0 && !await CountdownWindow.RunAsync(v.CountdownSeconds).ConfigureAwait(true))
            {
                return;
            }

            Begin(services, v, snapshot, mode, rect);
        }
        catch (Exception ex) when (ex is NativeCallException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Cleanup();
            MessageBox.Show("The recording could not start: " + ex.Message, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _starting = false;
            if (_recorder is null)
            {
                RestoreOwnWindows();
            }

            ActiveChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void Begin(AppServices services, VideoSettings v, CaptureSnapshot snapshot, CaptureMode mode, PixelRect rect)
    {
        ScreenLayout layout = snapshot.Layout;
        MonitorInfo monitor = layout.Monitors.OrderByDescending(m => m.Bounds.Intersect(rect).Area).First();
        PixelRect recorded;
        ScreenFrameSource video;
        WindowCandidate? window = mode == CaptureMode.Window ? snapshot.Windows.Where(w => w.Bounds == rect).OrderBy(w => w.ZOrder).FirstOrDefault() : null;
        if (window is not null)
        {
            video = new ScreenFrameSource(window.Handle, isWindow: true, crop: null, v.CaptureCursor);
            recorded = rect;
        }
        else
        {
            // Region / display: recorded on the monitor holding most of it (a region spanning monitors is clipped).
            recorded = rect.Intersect(monitor.Bounds);
            nint hmon = MonitorFromPoint(PackPoint(monitor.Bounds.X + (monitor.Bounds.Width / 2), monitor.Bounds.Y + (monitor.Bounds.Height / 2)), 2 /* NEAREST */);
            video = new ScreenFrameSource(hmon, isWindow: false, crop: (recorded.X - monitor.Bounds.X, recorded.Y - monitor.Bounds.Y, recorded.Width, recorded.Height), v.CaptureCursor);
        }

        IAudioSource[] audio = v.RecordSystemAudio || v.RecordMicrophone
            ? [new WasapiSource(ScreenRecorder.AudioNames.System, loopback: true, null), new WasapiSource(ScreenRecorder.AudioNames.Microphone, loopback: false, v.MicrophoneDeviceId)]
            : [];
        var options = new RecorderOptions(v.FramesPerSecond, v.Quality, v.RecordSystemAudio, v.RecordMicrophone, v.MicrophoneDeviceId, v.MinimumFreeSpaceMegabytes * 1024L * 1024, v.LowSpaceWarningMinutes);
        _startedUtc = DateTime.UtcNow;
        (Guid id, string path) = services.Library.BeginVideo(_startedUtc);
        _videoId = id;
        try
        {
            _recorder = ScreenRecorder.Create(video, audio, options, path);
        }
        catch
        {
            video.Dispose();
            foreach (IAudioSource a in audio)
            {
                a.Dispose();
            }

            services.Library.AbandonVideo(id);
            throw;
        }

        _recorder.Notice += (_, message) => Application.Current.Dispatcher.BeginInvoke(() => _toolbar?.ShowStatus(message));
        _recorder.StoppedBySelf += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => _ = StopAsync());

        if (window is null)
        {
            _outline = new RecordingOutline(recorded);
            _outline.Show();
        }

        string format = $"{_recorder.Width}×{_recorder.Height} · {v.FramesPerSecond} FPS";
        _toolbar = new RecordingToolbar(format, options.AudioTrack, v.RecordSystemAudio, v.RecordMicrophone,
            v.StopHotkey is { } s ? s.ToString() : null, v.RecordHotkey is { } p ? p.ToString() : null);
        _toolbar.PauseClicked += (_, _) => TogglePause();
        _toolbar.StopClicked += (_, _) => _ = StopAsync();
        _toolbar.CancelClicked += (_, _) => CancelWithConfirmation();
        _toolbar.SystemToggled += (_, on) => _recorder?.SetSourceEnabled(0, on);
        _toolbar.MicToggled += (_, on) => _recorder?.SetSourceEnabled(1, on);
        _toolbar.Show();
        _toolbar.PlaceNear(recorded, monitor.WorkArea);
        if (!_toolbar.Excluded)
        {
            _toolbar.ShowStatus("This version of Windows cannot hide the toolbar from recordings; it is placed outside the recorded area where possible.");
        }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) => UpdateToolbar(), Application.Current.Dispatcher);
        _recorder.Start();
        _timer.Start();
        ActiveChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Stops, saves the MP4 into the Library and opens it.</summary>
    public static async Task StopAsync(string? reason = null)
    {
        if (_recorder is not { } recorder || _stopping || AppServices.Current is not { } services)
        {
            return;
        }

        _stopping = true;
        _toolbar?.ShowSaving();
        RecordingResult result = await Task.Run(() => recorder.Stop(reason)).ConfigureAwait(true);
        CaptureRecord? saved = null;
        string message;
        try
        {
            if (result.Finalized)
            {
                var info = new VideoInfo(result.Width, result.Height, result.FramesPerSecond, result.DurationMs, result.HasAudio);
                saved = await services.Writer.RunAsync(() => services.Library.CompleteVideo(_videoId, info, result.FirstFrame, _startedUtc)).ConfigureAwait(true);
                message = $"Recording saved: {TimeSpan.FromMilliseconds(result.DurationMs):h\\:mm\\:ss}, {result.Width}×{result.Height}.";
            }
            else
            {
                // The file could not be completed (e.g. disk full): recover it like after a crash.
                IReadOnlyList<RecoveryOutcome> outcomes = await services.Writer.RunAsync(() => RecordingRecovery.RecoverAll(services.Library)).ConfigureAwait(true);
                message = outcomes.FirstOrDefault(o => o.Id == _videoId)?.Message ?? "The recording could not be saved.";
                saved = services.Library.Find(_videoId);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            message = "The recording could not be added to the Library: " + ex.Message + " The file is kept and will be offered for recovery at the next start.";
        }

        if (result.EndReason is { } why)
        {
            message = why + " " + message;
        }

        Cleanup();
        RestoreOwnWindows();
        if (saved is not null && !services.Settings.Video.SaveToLibrary)
        {
            saved = await SaveOutsideLibraryAsync(services, saved).ConfigureAwait(true);
            message = saved is null ? "Recording saved (not kept in the Library)." : message + " It was kept in the Library because no location was chosen.";
        }

        App.NotifyBalloon("SnapForge recording", message, warning: !result.Finalized || result.EndReason is not null);
        if (saved is not null && services.Settings.Video.OpenAfterRecording)
        {
            App.ShowVideo(saved.Id);
        }
    }

    /// <summary>
    /// VID-08 "without history": asks where to save, copies the MP4 there and removes it from the Library.
    /// Returns null when it was saved outside, or the record if the user cancelled (then it stays in the Library).
    /// </summary>
    private static async Task<CaptureRecord?> SaveOutsideLibraryAsync(AppServices services, CaptureRecord saved)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save recording",
            Filter = "MP4 video (*.mp4)|*.mp4",
            FileName = Core.Export.ExportFileNames.For(saved.Title, saved.CreatedUtc.ToLocalTime(), ".mp4", new HashSet<string>()),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };
        if (dialog.ShowDialog() != true)
        {
            return saved;
        }

        string target = dialog.FileName;
        await Task.Run(() => File.Copy(services.Library.VideoPath(saved.Id), target, overwrite: true)).ConfigureAwait(true);
        await services.Writer.RunAsync(() => services.Library.DeletePermanently([saved.Id])).ConfigureAwait(true);
        return null;
    }

    private static void CancelWithConfirmation()
    {
        if (_recorder is null || _stopping)
        {
            return;
        }

        bool wasPaused = _recorder.State == RecorderState.Paused;
        _recorder.Pause();
        MessageBoxResult answer = MessageBox.Show(_toolbar!, "Discard this recording? It will not be saved.", "SnapForge", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            if (!wasPaused)
            {
                _recorder.Resume();
            }

            return;
        }

        Cancel();
    }

    /// <summary>Discards the running recording without asking (cancel hotkey, or the confirmation above).</summary>
    public static void Cancel()
    {
        if (_recorder is null || AppServices.Current is not { } services)
        {
            return;
        }

        _recorder.Cancel();
        services.Library.AbandonVideo(_videoId);
        Cleanup();
        RestoreOwnWindows();
    }

    /// <summary>On application exit: stop and save synchronously.</summary>
    public static void StopForExit()
    {
        if (_recorder is not { } recorder || AppServices.Current is not { } services)
        {
            return;
        }

        RecordingResult r = recorder.Stop("SnapForge was closed while recording.");
        if (r.Finalized)
        {
            services.Library.CompleteVideo(_videoId, new VideoInfo(r.Width, r.Height, r.FramesPerSecond, r.DurationMs, r.HasAudio), r.FirstFrame, _startedUtc);
        }

        Cleanup();
    }

    private static void UpdateToolbar()
    {
        if (_recorder is { } r && _toolbar is { } t)
        {
            t.Update(r.Elapsed, r.State == RecorderState.Paused, r.Level(0), r.Level(1));
        }
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_recorder is { State: RecorderState.Recording })
                {
                    _recorder.Pause();
                    _toolbar?.ShowStatus("Paused because the PC was locked. Press Resume to continue.");
                    UpdateToolbar();
                }
            });
        }
    }

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            // Called before sleep: finish the file now (WGC sessions do not survive sleep reliably).
            Application.Current.Dispatcher.Invoke(() => _ = StopAsync("Recording stopped because the PC went to sleep."));
        }
    }

    private static void OnSessionEnding(object? sender, SessionEndingEventArgs e) =>
        Application.Current.Dispatcher.Invoke(StopForExit);

    private static void Cleanup()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _timer?.Stop();
        _timer = null;
        _toolbar?.Close();
        _toolbar = null;
        _outline?.Close();
        _outline = null;
        _recorder?.Dispose();
        _recorder = null;
        _stopping = false;
        ActiveChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void HideOwnWindows()
    {
        Hidden.Clear();
        foreach (Window w in Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToList())
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

    private static long PackPoint(int x, int y) => ((long)y << 32) | (uint)x;
}
