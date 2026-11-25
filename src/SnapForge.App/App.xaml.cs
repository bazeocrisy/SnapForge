using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SnapForge.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 1 && e.Args[0] == "--self-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string report = e.Args.Length >= 2 ? e.Args[1] : Path.Combine(Environment.CurrentDirectory, "snapforge-self-test.txt");
            Shutdown(SelfTest.Run(report));
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string? logPath = TryWriteCrashLog(e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}" + (logPath is null ? string.Empty : $"\n\nDetails were saved to:\n{logPath}"),
            "SnapForge",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keep running: the current screenshot is still in memory and the user may still save it.
        e.Handled = true;
    }

    /// <summary>Local-only diagnostics. Nothing is ever sent over the network.</summary>
    private static string? TryWriteCrashLog(Exception exception)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapForge", "logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"error-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, $"{DateTime.Now:O}\n{Environment.OSVersion}\n{exception}");
            return path;
        }
#pragma warning disable CA1031 // Logging must never throw from the crash handler.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
