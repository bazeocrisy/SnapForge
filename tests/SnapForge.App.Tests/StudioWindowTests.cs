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
/// SnapForge Studio window (SF-006) with a SYNTHETIC camera (CI has none): the camera is never opened in Screen mode
/// or merely by opening Studio, a camera choice starts the preview, Screen / Start release it, choices persist, and
/// every control has an accessible name.
/// </summary>
[Collection(DesktopTestGroup.Name)]
public class StudioWindowTests
{
    private static readonly Mf.VideoDevice[] OneCamera = [new("Test camera (SYNTHETIC)", "synthetic-camera-1")];

    [Fact]
    public void Opening_studio_does_not_open_the_camera_and_every_control_is_named() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        using var camera = new SyntheticCameraSource(320, 180, 30);
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        studio.Show();
        await Task.Delay(300);
        Assert.Equal(StudioMode.Screen, services.Settings.Video.StudioMode);
        Assert.False(services.Settings.Video.RecordMicrophone);
        Assert.Equal(0, camera.StartCount);
        Assert.Null(studio.PreviewCamera);
        foreach (ButtonBase b in All<ButtonBase>(studio))
        {
            string name = AutomationProperties.GetName(b);
            Assert.True(!string.IsNullOrWhiteSpace(name) || b.Content is string { Length: > 0 }, $"unnamed {b.GetType().Name}");
        }

        Assert.NotNull(Find<RadioButton>(studio, "Record: Screen and camera"));
        Assert.NotNull(Find<Button>(studio, "Start recording"));
        studio.Close();
        Assert.Equal(0, camera.StartCount);
    });

    [Fact]
    public void Camera_choice_starts_the_preview_and_screen_only_releases_it() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        using var camera = new SyntheticCameraSource(320, 180, 30);
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        studio.Show();
        await Task.Delay(200);

        Find<RadioButton>(studio, "Record: Screen and camera")!.IsChecked = true;
        Assert.Equal(StudioMode.ScreenAndCamera, services.Settings.Video.StudioMode);
        await WaitUntil(() => camera.IsRunning);
        Image preview = Find<Image>(studio, "Camera preview")!;
        await WaitUntil(() => preview.Source is not null);

        Find<RadioButton>(studio, "Camera position: Top left")!.IsChecked = true;
        Find<RadioButton>(studio, "Camera shape: Rounded rectangle")!.IsChecked = true;
        Find<RadioButton>(studio, "Camera size: Large")!.IsChecked = true;
        Assert.Equal(CameraCorner.TopLeft, services.Settings.Video.CameraCorner);
        Assert.Equal(CameraOverlayShape.RoundedRectangle, services.Settings.Video.CameraShape);
        Assert.Equal(CameraOverlaySize.Large, services.Settings.Video.CameraSize);

        Find<RadioButton>(studio, "Record: Screen only")!.IsChecked = true;
        await studio.CameraReleased;
        Assert.False(camera.IsRunning);
        Assert.Null(studio.PreviewCamera);
        studio.Close();
    });

    [Fact]
    public void A_remembered_camera_mode_waits_for_an_explicit_preview_and_start_releases_the_camera() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create(s => s with { Video = s.Video with { StudioMode = StudioMode.Camera } });
        using var camera = new SyntheticCameraSource(320, 180, 30);
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        bool started = false;
        studio.StartRequested += (_, _) => started = true;
        studio.Show();
        await Task.Delay(300);
        Assert.Equal(0, camera.StartCount); // opening Studio alone never turns the camera on

        Button show = Find<Button>(studio, "Turn on the camera preview")!;
        Assert.Equal(Visibility.Visible, show.Visibility);
        show.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitUntil(() => camera.IsRunning);

        Find<Button>(studio, "Start recording")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(started);
        await studio.CameraReleased;
        Assert.False(camera.IsRunning);
        studio.Close();
    });

    [Fact]
    public void Camera_failure_is_explained_and_not_retried() => UiThread.Run(async () =>
    {
        AppServices services = TestServices.Create();
        using var camera = new SyntheticCameraSource(320, 180, 30) { FailNextStart = CameraFailure.AccessDenied };
        var studio = new StudioWindow(services, (_, _) => camera, () => OneCamera);
        studio.Show();
        await Task.Delay(200);
        Find<RadioButton>(studio, "Record: Camera only")!.IsChecked = true;
        Button privacy = Find<Button>(studio, "Open camera privacy settings")!;
        await WaitUntil(() => privacy.Visibility == Visibility.Visible);
        await Task.Delay(500);
        Assert.Equal(0, camera.StartCount); // the failed attempt is not repeated behind the user's back
        Assert.False(camera.IsRunning);
        studio.Close();
    });

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
