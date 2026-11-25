using System.Windows;
using System.Windows.Threading;

namespace SnapForge.App.Tests;

/// <summary>
/// One long-lived STA thread running a WPF dispatcher with the real <see cref="App"/> resources loaded
/// (an Application can only be created once per process). UI tests run as async functions on it.
/// </summary>
internal static class TestServices
{
    /// <summary>Settings and Capture Library in a fresh temporary folder (never the user's real data).</summary>
    public static Services.AppServices Create(Func<Core.Settings.AppSettings, Core.Settings.AppSettings>? adjust = null)
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snapforge-apptests", Guid.NewGuid().ToString("N"));
        return Services.AppServices.Create(System.IO.Path.Combine(root, "settings.json"), System.IO.Path.Combine(root, "library"),
            s => (adjust ?? (x => x))(s with { CloseToNotificationArea = false, PrivacyNoticeShown = true }));
    }
}

internal static class UiThread
{
    private static readonly Lazy<Dispatcher> Instance = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Run(Func<Task> test, int timeoutSeconds = 90)
    {
        Task task = Instance.Value.InvokeAsync(test).Task.Unwrap();
        if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException($"UI test did not finish within {timeoutSeconds} s.");
        }

        task.GetAwaiter().GetResult();
    }

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            NativeInput.TryEnablePerMonitorDpiAwareness();
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            Services.AppServices.Current = TestServices.Create();
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "SnapForge test UI thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
