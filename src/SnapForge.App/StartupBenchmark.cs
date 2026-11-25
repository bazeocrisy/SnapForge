using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.Core.Library;

namespace SnapForge.App;

/// <summary>
/// <c>SnapForge.exe --benchmark-startup &lt;libraryRoot&gt; &lt;out.json&gt;</c>: starts the real UI over an existing
/// library (seeded by tools/SnapForge.Benchmarks with production durability) and measures what a user feels:
/// time until the window is shown, time until the library index is complete, how long the UI thread is ever
/// blocked meanwhile, time to show the Library grid, a search, and memory. Settings live in a temporary folder;
/// the library is only read.
/// </summary>
internal static class StartupBenchmark
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string libraryRoot, string outPath)
    {
        try
        {
            var result = await MeasureAsync(libraryRoot).ConfigureAwait(true);
            File.WriteAllText(outPath, JsonSerializer.Serialize(result, Json));
            Console.WriteLine(JsonSerializer.Serialize(result, Json));
            return 0;
        }
#pragma warning disable CA1031 // Report any failure in the output file.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            File.WriteAllText(outPath, JsonSerializer.Serialize(new { Error = ex.ToString() }, Json));
            return 1;
        }
    }

    private static async Task<object> MeasureAsync(string libraryRoot)
    {
        DateTime processStart = Process.GetCurrentProcess().StartTime;
        string settingsDir = Path.Combine(Path.GetTempPath(), "snapforge-bench-settings-" + Guid.NewGuid().ToString("N"));
        var clock = Stopwatch.StartNew();
        AppServices services = AppServices.Create(Path.Combine(settingsDir, "settings.json"), libraryRoot,
            s => s with { PrivacyNoticeShown = true, CloseToNotificationArea = false, Widget = s.Widget with { Enabled = false } }, deferIndex: true);
        AppServices.Current = services;
        ThemeManager.Apply(services.Settings.Theme);
        double servicesMs = clock.Elapsed.TotalMilliseconds;

        var window = new MainWindow(services);
        Application.Current.MainWindow = window;
        var rendered = new TaskCompletionSource();
        window.ContentRendered += (_, _) => rendered.TrySetResult();
        window.Show();
        await rendered.Task.ConfigureAwait(true);
        double windowMs = clock.Elapsed.TotalMilliseconds;
        double sinceProcessStartMs = (DateTime.Now - processStart).TotalMilliseconds;

        // UI responsiveness probe: a high-priority tick every 15 ms; the longest gap is the worst UI freeze.
        var gaps = new List<double>();
        var probe = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            gaps.Add(probe.Elapsed.TotalMilliseconds);
            probe.Restart();
        };
        timer.Start();

        await services.StartIndexingAsync().ConfigureAwait(true);
        double indexedMs = clock.Elapsed.TotalMilliseconds;
        int count = services.Library.Records.Count;

        var libraryClock = Stopwatch.StartNew();
        window.ShowLibraryWorkspace();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        double libraryViewMs = libraryClock.Elapsed.TotalMilliseconds;
        int visible = window.LibraryPane.VisibleCount;

        var searchClock = Stopwatch.StartNew();
        window.LibraryPane.SetSearch("delta");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        double searchMs = searchClock.Elapsed.TotalMilliseconds;
        int found = window.LibraryPane.VisibleCount;
        window.LibraryPane.SetSearch(string.Empty);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        timer.Stop();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long workingSet = Process.GetCurrentProcess().WorkingSet64;
        window.AllowClose = true;
        window.Close();
        try
        {
            Directory.Delete(settingsDir, recursive: true);
        }
        catch (IOException)
        {
        }

        double[] sorted = [.. gaps.Order()];
        return new
        {
            Captures = count,
            ServicesReadyMs = Math.Round(servicesMs, 1),
            WindowShownMs = Math.Round(windowMs, 1),
            WindowShownSinceProcessStartMs = Math.Round(sinceProcessStartMs, 1),
            IndexCompleteMs = Math.Round(indexedMs, 1),
            LongestUiFreezeMs = sorted.Length == 0 ? 0 : Math.Round(sorted[^1], 1),
            UiTickP95Ms = sorted.Length == 0 ? 0 : Math.Round(sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1], 1),
            LibraryViewMs = Math.Round(libraryViewMs, 1),
            LibraryTiles = visible,
            SearchMs = Math.Round(searchMs, 1),
            SearchResults = found,
            WorkingSetMb = Math.Round(workingSet / 1048576.0, 1),
        };
    }
}
