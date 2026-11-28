using System.IO;
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
            WorkAreaGuard.Attach(w); // the app installs the guard for all windows; tests attach explicitly
            await Task.Delay(1500);
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
        var w = new Window { Title = "FA-003 restore", WindowStartupLocation = WindowStartupLocation.Manual, Left = 50, Top = 50, Width = 500, Height = 400 };
        try
        {
            w.Show();
            WorkAreaGuard.Attach(w);
            await Task.Delay(1200);
            w.WindowState = WindowState.Maximized;
            await Task.Delay(200);
            PixelRect work = WorkAreaOf(w);
            w.WindowState = WindowState.Normal;
            PhysicalPlacement.SetBounds(w, new PixelRect(work.Right - 50, work.Bottom - 20, 500, 400), resize: false); // nearly off screen
            w.WindowState = WindowState.Minimized;
            await Task.Delay(200);
            w.WindowState = WindowState.Normal;
            await Task.Delay(1200);
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
        Services.AppServices services = TestServices.Create();
        var w = new MainWindow(services);
        try
        {
            w.Show();
            WorkAreaGuard.Attach(w); // the app installs the guard for all windows; tests attach explicitly
            await Task.Delay(1500);
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

/// <summary>FA-002: the per-user → system-wide migration only ever touches the user's own old per-user copy.</summary>
public class LegacyInstallMigrationTests
{
    [Fact]
    public void FA002_only_an_install_inside_the_users_Programs_folder_with_its_uninstaller_is_accepted()
    {
        string root = Path.Combine(Path.GetTempPath(), "sf-legacy-" + Guid.NewGuid().ToString("N"));
        string programs = Path.Combine(root, "Local", "Programs");
        string old = Path.Combine(programs, "SnapForge");
        Directory.CreateDirectory(old);
        try
        {
            Assert.Null(Services.LegacyInstallMigration.Validate(old, "1.0.0-rc.1", null, programs)); // no uninstaller yet
            File.WriteAllText(Path.Combine(old, "unins000.exe"), "x");
            var found = Services.LegacyInstallMigration.Validate(old, "1.0.0-rc.1", @"C:\Program Files\SnapForge\SnapForge.exe", programs);
            Assert.NotNull(found);
            Assert.Equal(Path.Combine(old, "unins000.exe"), found!.Uninstaller);

            // Never: another folder, traversal out of Programs, or the running copy itself.
            Assert.Null(Services.LegacyInstallMigration.Validate(@"C:\Windows\System32", null, null, programs));
            Assert.Null(Services.LegacyInstallMigration.Validate(Path.Combine(programs, "..", "..", "Other"), null, null, programs));
            Assert.Null(Services.LegacyInstallMigration.Validate(old, null, Path.Combine(old, "SnapForge.exe"), programs));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FA002_running_marker_is_visible_machine_wide_while_held()
    {
        Interop.RunningMarker.Hold();
        Assert.True(Interop.RunningMarker.IsHeldAnywhere());
    }
}
