using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnapForge.App.Services;
using SnapForge.App.Video;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Settings;

namespace SnapForge.App.Tests;

/// <summary>
/// RC3 Record panel (SF-006, owner feedback on rc.2) with a SYNTHETIC camera (CI has none): opening the panel never
/// opens the camera; Camera and Mic start off every session; turning Camera on starts the floating live preview and
/// turning it off releases the camera; Start releases the camera and hands the preview to the recording; a camera
/// failure is explained and not retried; choices persist; every control has an accessible name.
/// </summary>
[Collection(DesktopTestGroup.Name)]
public class StudioWindowTests
{
    private const string CameraSwitch = "Camera: show yourself in the recording";
    private static readonly Mf.VideoDevice[] OneCamera = [new("Test camera (SYNTHETIC)", "synthetic-camera-1")];

    [Fact]
    public void Opening_studio_does_not_open_the_camera_and_every_control_is_named() => UiThread.Run(async () =>
    {
        ResetSession();
        AppServices services = TestServices.Create();
        using var camera = new SyntheticCameraSource(320, 180, 30);
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        studio.Show();
        await Task.Delay(300);
        Assert.False(RecordingController.SessionCamera); // Camera and Mic start off in every session
        Assert.False(RecordingController.SessionMicrophone);
        Assert.Equal(StudioMode.Screen, RecordingController.Effective(services.Settings.Video).StudioMode);
        Assert.False(RecordingController.Effective(services.Settings.Video).RecordMicrophone);
        Assert.Equal(0, camera.StartCount);
        Assert.Null(studio.PreviewCamera);
        Assert.Null(studio.Preview);
        foreach (ButtonBase b in All<ButtonBase>(studio))
        {
            string name = AutomationProperties.GetName(b);
            Assert.True(!string.IsNullOrWhiteSpace(name) || b.Content is string { Length: > 0 }, $"unnamed {b.GetType().Name}");
        }

        Assert.Equal(false, Find<ToggleButton>(studio, CameraSwitch)!.IsChecked);
        Assert.Equal(false, Find<ToggleButton>(studio, "Microphone: record your voice")!.IsChecked);
        Assert.NotNull(Find<ToggleButton>(studio, "Computer audio: record what you hear"));
        Assert.NotNull(Find<RadioButton>(studio, "What to record: A whole screen"));
        Assert.NotNull(Find<RadioButton>(studio, "What to record: One window"));
        Assert.NotNull(Find<RadioButton>(studio, "What to record: An area you draw"));
        Assert.NotNull(Find<Button>(studio, "Start recording"));
        studio.Close();
        Assert.Equal(0, camera.StartCount);
    });

    [Fact]
    public void Camera_choice_starts_the_preview_and_screen_only_releases_it() => UiThread.Run(async () =>
    {
        ResetSession();
        AppServices services = TestServices.Create();
        using var camera = new SyntheticCameraSource(320, 180, 30);
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        try
        {
            studio.Show();
            await Task.Delay(200);

            Find<RadioButton>(studio, "What to record: One window")!.IsChecked = true;
            Assert.Equal(RecordingSource.Window, services.Settings.Video.Source); // the target type is remembered
            Click(Find<ToggleButton>(studio, CameraSwitch)!, on: true);
            Assert.True(RecordingController.SessionCamera);
            Assert.Equal(StudioMode.ScreenAndCamera, RecordingController.Effective(services.Settings.Video).StudioMode);
            await WaitUntil(() => camera.IsRunning);
            CameraPreviewWindow preview = studio.Preview!;
            Assert.True(preview.IsVisible); // see yourself before recording
            await WaitUntil(() => preview.FramesShown > 2); // live frames
            Assert.Contains("bottom right", preview.Describe(), StringComparison.Ordinal); // where it will appear

            Find<RadioButton>(studio, "Camera position in the recording: Top left")!.IsChecked = true;
            Find<RadioButton>(studio, "Camera shape: Rounded rectangle")!.IsChecked = true;
            Find<RadioButton>(studio, "Camera size: Large")!.IsChecked = true;
            Assert.Equal(CameraCorner.TopLeft, services.Settings.Video.CameraCorner);
            Assert.Equal(CameraOverlayShape.RoundedRectangle, services.Settings.Video.CameraShape);
            Assert.Equal(CameraOverlaySize.Large, services.Settings.Video.CameraSize);
            Assert.Contains("top left", preview.Describe(), StringComparison.Ordinal);

            Click(Find<ToggleButton>(studio, CameraSwitch)!, on: false); // back to screen only
            await studio.CameraReleased;
            Assert.False(camera.IsRunning);
            Assert.Null(studio.PreviewCamera);
            Assert.Null(studio.Preview);
            Assert.False(preview.IsVisible);
            Assert.Equal(StudioMode.Screen, RecordingController.Effective(services.Settings.Video).StudioMode);
        }
        finally
        {
            studio.Close();
            ResetSession();
        }
    });

    [Fact]
    public void A_remembered_camera_mode_waits_for_an_explicit_preview_and_start_releases_the_camera() => UiThread.Run(async () =>
    {
        ResetSession();
        AppServices services = TestServices.Create(s => s with { Video = s.Video with { StudioMode = StudioMode.Camera } });
        using var camera = new SyntheticCameraSource(320, 180, 30);
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        bool started = false;
        CameraPreviewWindow? handedOver = null;
        studio.StartRequested += (_, _) =>
        {
            started = true;
            handedOver = studio.TakePreview();
        };
        try
        {
            studio.Show();
            await Task.Delay(300);
            Assert.Equal(0, camera.StartCount); // a remembered "camera only" never turns the camera on by itself
            Assert.Equal(StudioMode.Screen, RecordingController.Effective(services.Settings.Video).StudioMode);

            Click(Find<ToggleButton>(studio, CameraSwitch)!, on: true);
            await WaitUntil(() => camera.IsRunning);
            Assert.Equal(StudioMode.Camera, RecordingController.Effective(services.Settings.Video).StudioMode);

            Find<Button>(studio, "Start recording")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(started);
            await studio.CameraReleased;
            Assert.False(camera.IsRunning);
            Assert.NotNull(handedOver); // the preview stays on screen for the recording
            studio.Close();
            Assert.True(handedOver!.IsVisible);
        }
        finally
        {
            handedOver?.Close();
            studio.Close();
            ResetSession();
        }
    });

    [Fact]
    public void Camera_failure_is_explained_and_not_retried() => UiThread.Run(async () =>
    {
        ResetSession();
        AppServices services = TestServices.Create();
        using var camera = new SyntheticCameraSource(320, 180, 30) { FailNextStart = CameraFailure.AccessDenied };
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        try
        {
            studio.Show();
            await Task.Delay(200);
            Click(Find<ToggleButton>(studio, CameraSwitch)!, on: true);
            Button privacy = Find<Button>(studio, "Open camera privacy settings")!;
            await WaitUntil(() => privacy.Visibility == Visibility.Visible);
            await Task.Delay(500);
            Assert.Equal(0, camera.StartCount); // the failed attempt is not repeated behind the user's back
            Assert.False(camera.IsRunning);
            Assert.Contains("not available", studio.Preview!.Describe(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            studio.Close();
            ResetSession();
        }
    });

    private static void ResetSession()
    {
        RecordingController.SessionCamera = false;
        RecordingController.SessionMicrophone = false;
        RecordingController.PreviewHidden = false;
    }

    /// <summary>Like a user click on a switch: changes its state, then raises Click.</summary>
    private static void Click(ToggleButton toggle, bool on)
    {
        toggle.IsChecked = on;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < timeoutMs, "condition not reached in time");
            await Task.Delay(50);
        }
    }

    private static T? Find<T>(DependencyObject root, string name)
        where T : DependencyObject => All<T>(root).FirstOrDefault(e => AutomationProperties.GetName(e) == name);

    private static List<T> All<T>(DependencyObject root)
        where T : DependencyObject
    {
        var list = new List<T>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            DependencyObject d = stack.Pop();
            if (d is T t)
            {
                list.Add(t);
            }

            foreach (object child in LogicalTreeHelper.GetChildren(d))
            {
                if (child is DependencyObject c)
                {
                    stack.Push(c);
                }
            }
        }

        return list;
    }
}
