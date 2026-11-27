using System.Windows;
using System.Windows.Controls;
using SnapForge.App.Interop;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Tests;

/// <summary>FA-003 / SF-007 (window frame always on screen) and SF-008 (Close window vs Exit SnapForge).</summary>
[Collection(DesktopTestGroup.Name)]
public class WindowingTests
{
    private static PixelRect WorkAreaOf(Window w)
    {
        PixelRect bounds = PhysicalPlacement.GetBounds(w)!.Value;
        return PhysicalPlacement.MonitorFor(PhysicalPlacement.Layout(), bounds).WorkArea;
    }

    [Fact]
    public void FA003_oversized_window_above_the_screen_is_moved_and_shrunk_into_the_work_area() => UiThread.Run(async () =>
    {
        WorkAreaGuard.Install();
        var w = new Window
        {
            Title = "FA-003 test",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -400,
            Top = -600,
            Width = 6000,
            Height = 5000,
            MinWidth = 5000,
            MinHeight = 4000,
        };
        try
        {
            w.Show();
            await Task.Delay(300);
            PixelRect work = WorkAreaOf(w);
            PixelRect b = PhysicalPlacement.GetBounds(w)!.Value;
            Assert.True(work.Contains(b.X, b.Y) && b.Right <= work.Right && b.Bottom <= work.Bottom, $"window {b} outside work area {work}");
            Assert.True(WindowFit.CaptionVisible(b, work, 30));
        }
        finally
        {
            w.Close();
        }
    });

    [Fact]
    public void FA003_restoring_from_maximized_after_moving_off_screen_is_fitted_again() => UiThread.Run(async () =>
    {
        WorkAreaGuard.Install();
        var w = new Window { Title = "FA-003 restore", WindowStartupLocation = WindowStartupLocation.Manual, Left = 50, Top = 50, Width = 500, Height = 400 };
        try
        {
            w.Show();
            await Task.Delay(200);
            w.WindowState = WindowState.Maximized;
            await Task.Delay(200);
            PixelRect work = WorkAreaOf(w);
            w.WindowState = WindowState.Normal;
            PhysicalPlacement.SetBounds(w, new PixelRect(work.Right - 50, work.Bottom - 20, 500, 400), resize: false); // nearly off screen
            w.WindowState = WindowState.Minimized;
            await Task.Delay(200);
            w.WindowState = WindowState.Normal;
            await Task.Delay(400);
            PixelRect b = PhysicalPlacement.GetBounds(w)!.Value;
            Assert.True(b.Right <= work.Right && b.Bottom <= work.Bottom && b.X >= work.X && b.Y >= work.Y, $"window {b} outside work area {work}");
        }
        finally
        {
            w.Close();
        }
    });

    [Fact]
    public void FA003_main_window_default_size_is_inside_the_work_area_with_title_bar_visible() => UiThread.Run(async () =>
    {
        WorkAreaGuard.Install();
        Services.AppServices services = TestServices.Create();
        var w = new MainWindow(services);
        try
        {
            w.Show();
            await Task.Delay(400);
            PixelRect work = WorkAreaOf(w);
            PixelRect b = PhysicalPlacement.GetBounds(w)!.Value;
            Assert.True(WindowFit.CaptionVisible(b, work, 30), $"title bar of {b} not visible in {work}");
            Assert.True(b.Right <= work.Right && b.Bottom <= work.Bottom, $"window {b} larger than work area {work}");
        }
        finally
        {
            await w.FlushAllAsync();
            w.AllowClose = true;
            w.Close();
        }
    });

    [Fact]
    public void SF008_file_menu_separates_close_window_from_exit() => UiThread.Run(async () =>
    {
        Services.AppServices services = TestServices.Create();
        var w = new MainWindow(services);
        try
        {
            w.Show();
            await Task.Delay(100);
            MenuItem file = w.MainMenu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "_File");
            MenuItem close = file.Items.OfType<MenuItem>().Single(m => (string)m.Header == "Close _window");
            MenuItem exit = file.Items.OfType<MenuItem>().Single(m => (string)m.Header == "E_xit SnapForge");
            Assert.Contains("keeps running", (string)close.ToolTip, StringComparison.Ordinal);
            Assert.Contains("Quits SnapForge", (string)exit.ToolTip, StringComparison.Ordinal);
            Assert.True(file.Items.IndexOf(close) < file.Items.IndexOf(exit));
        }
        finally
        {
            await w.FlushAllAsync();
            w.AllowClose = true;
            w.Close();
        }
    });

    [Fact]
    public void SF014_about_reports_installation_kind_and_profile()
    {
        string text = Services.InstanceInfo.Describe();
        Assert.Contains("Installation:", text, StringComparison.Ordinal);
        Assert.Contains("Data profile:", text, StringComparison.Ordinal);
        Assert.Equal("installed for all users (Program Files)", Services.InstanceInfo.InstallationKind(
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SnapForge", "SnapForge.exe")));
        Assert.Equal("portable (not installed)", Services.InstanceInfo.InstallationKind(@"D:\Tools\SnapForge\SnapForge.exe"));
    }
}
