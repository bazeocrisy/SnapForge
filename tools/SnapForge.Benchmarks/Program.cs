using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Benchmarks;

/// <summary>
/// Grows one library to each requested size and measures it. Every write uses the production code path
/// (<see cref="CaptureLibrary.Create"/> / <see cref="CaptureLibrary.SaveContent"/>) with production durability:
/// this program never touches the test-only flush bypass. Usage:
/// <c>SnapForge.Benchmarks --root DIR --sizes 100,1000,5000 --out results.json [--width 800 --height 500] [--time-limit-minutes 80]</c>
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int Main(string[] args)
    {
        string root = Arg(args, "--root") ?? Path.Combine(Path.GetTempPath(), "snapforge-bench-" + Environment.ProcessId);
        int[] sizes = (Arg(args, "--sizes") ?? "100,1000,5000,10000,25000").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).Order().ToArray();
        string? outPath = Arg(args, "--out");
        int width = int.Parse(Arg(args, "--width") ?? "800", CultureInfo.InvariantCulture);
        int height = int.Parse(Arg(args, "--height") ?? "500", CultureInfo.InvariantCulture);
        double limitMinutes = double.Parse(Arg(args, "--time-limit-minutes") ?? "0", CultureInfo.InvariantCulture);
        bool keep = args.Contains("--keep");

        Directory.CreateDirectory(root);
        var total = Stopwatch.StartNew();
        var results = new List<SizeResult>();
        var skipped = new List<string>();
        CaptureLibrary lib = CaptureLibrary.Open(root);
        int count = lib.Records.Count;
        var createLatencies = new List<double>();
        Console.WriteLine($"Library: {root}  image {width}x{height}  sizes {string.Join(", ", sizes)}  durability: production (flush to disk on every write)");

        foreach (int target in sizes)
        {
            // Estimate whether seeding to the target fits the time budget, from the measured create rate.
            if (limitMinutes > 0 && createLatencies.Count > 20)
            {
                double perCreateMs = createLatencies.Average();
                double needMinutes = (target - count) * perCreateMs / 60000.0;
                double leftMinutes = limitMinutes - total.Elapsed.TotalMinutes;
                if (needMinutes > leftMinutes * 0.85)
                {
                    string reason = string.Create(CultureInfo.InvariantCulture, $"{target}: skipped — seeding needs ≈{needMinutes:0} min at {perCreateMs:0.0} ms/capture, {leftMinutes:0} min of budget left");
                    skipped.Add(reason);
                    Console.WriteLine(reason);
                    continue;
                }
            }

            var seed = Stopwatch.StartNew();
            var batch = new List<double>();
            while (count < target)
            {
                PixelBuffer img = SyntheticScreenshot(count, width, height);
                var sw = Stopwatch.StartNew();
                lib.Create(new CaptureContent(img, SampleAnnotations(count, width, height)), img, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(count), $"Capture {count:D6} {Word(count)}");
                double ms = sw.Elapsed.TotalMilliseconds;
                batch.Add(ms);
                createLatencies.Add(ms);
                count++;
                if (count % 1000 == 0)
                {
                    Console.WriteLine($"  seeded {count} ({total.Elapsed.TotalMinutes:0.0} min)");
                }
            }

            seed.Stop();
            lib = null!;
            results.Add(Measure(root, target, batch, seed.Elapsed));
            Print(results[^1]);
            lib = CaptureLibrary.Open(root);
        }

        var report = new Report(
            RuntimeInfo(),
            Environment.ProcessorCount,
            $"{width}x{height}",
            "production: every file written to a temp file, flushed to disk (FileStream.Flush(true)), then atomically renamed",
            results,
            skipped,
            total.Elapsed.TotalMinutes);
        string json = JsonSerializer.Serialize(report, JsonOptions);
        if (outPath is not null)
        {
            File.WriteAllText(outPath, json);
        }

        Console.WriteLine(json);
        if (!keep)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        return 0;
    }

    private static SizeResult Measure(string root, int size, List<double> createMs, TimeSpan seedTime)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);

        // Start-up: what the app does at launch. OpenDeferred is what blocks the UI; CompleteOpen runs in the background.
        var sw = Stopwatch.StartNew();
        CaptureLibrary lib = CaptureLibrary.OpenDeferred(root);
        double openDeferredMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        lib.CompleteOpen();
        double indexMs = sw.Elapsed.TotalMilliseconds;
        if (lib.Records.Count != size)
        {
            throw new InvalidOperationException($"Expected {size} records, found {lib.Records.Count}.");
        }

        long after = GC.GetTotalMemory(forceFullCollection: true);
        long workingSet = Process.GetCurrentProcess().WorkingSet64;
        var now = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        // Search: a title term matching ~1/16 of captures, a term matching one capture, and the full sorted view.
        double searchCommon = MedianOf(9, () => lib.Query(new LibraryQuery { Search = "delta" }, now));
        double searchRare = MedianOf(9, () => lib.Query(new LibraryQuery { Search = $"Capture {size / 2:D6}" }, now));
        double viewAll = MedianOf(9, () => lib.Query(new LibraryQuery(), now));

        // Thumbnails: decode 100 different thumbnails (what the Library grid does for one screenful+).
        var ids = lib.Records.Select(r => r.Id).OrderBy(g => g).Take(100).ToArray();
        sw.Restart();
        foreach (Guid id in ids)
        {
            _ = lib.LoadThumbnail(id) ?? throw new InvalidOperationException("Missing thumbnail.");
        }

        double thumbsPerItem = sw.Elapsed.TotalMilliseconds / ids.Length;

        // Autosave: an annotation edit (small files rewritten) and a crop-like edit (new base image), production flush.
        var saveSmall = new List<double>();
        var saveBase = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            Guid id = ids[i];
            CaptureContent content = lib.Load(id);
            var anns = content.Annotations.Append(new ArrowAnnotation(new PointD(10 + i, 10), new PointD(100, 60 + i))).ToList();
            var s = Stopwatch.StartNew();
            lib.SaveContent(id, null, anns, content.BaseImage, hasReversibleRedactions: false);
            saveSmall.Add(s.Elapsed.TotalMilliseconds);
            if (i < 5)
            {
                s.Restart();
                lib.SaveContent(id, content.BaseImage, anns, content.BaseImage, hasReversibleRedactions: false);
                saveBase.Add(s.Elapsed.TotalMilliseconds);
            }
        }

        double recent = createMs.Count == 0 ? 0 : createMs.Skip(Math.Max(0, createMs.Count - 200)).Average();
        return new SizeResult(
            size,
            Math.Round(openDeferredMs, 2),
            Math.Round(indexMs, 1),
            Math.Round((after - before) / 1048576.0, 1),
            Math.Round(workingSet / 1048576.0, 1),
            Math.Round(searchCommon, 2),
            Math.Round(searchRare, 2),
            Math.Round(viewAll, 2),
            Math.Round(thumbsPerItem, 2),
            Math.Round(Median(saveSmall), 2),
            Math.Round(Percentile(saveSmall, 0.95), 2),
            Math.Round(Median(saveBase), 2),
            Math.Round(recent, 2),
            Math.Round(seedTime.TotalSeconds, 1),
            DirectorySizeMb(root));
    }

    private static void Print(SizeResult r) => Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{r.Captures,6} captures | open(UI) {r.OpenDeferredMs} ms | index {r.IndexMs} ms | index memory {r.IndexMemoryMb} MB | search {r.SearchCommonMs}/{r.SearchRareMs} ms | view-all {r.ViewAllMs} ms | thumb {r.ThumbnailMsPerItem} ms | autosave {r.AutosaveMedianMs} (p95 {r.AutosaveP95Ms}) ms | base save {r.BaseSaveMedianMs} ms | create {r.CreateMsRecent} ms | disk {r.DiskMb} MB"));

    private static double MedianOf(int n, Action action)
    {
        var list = new List<double>();
        for (int i = 0; i < n; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            list.Add(sw.Elapsed.TotalMilliseconds);
        }

        return Median(list);
    }

    private static double Median(List<double> v) => Percentile(v, 0.5);

    private static double Percentile(List<double> v, double p)
    {
        if (v.Count == 0)
        {
            return 0;
        }

        var s = v.Order().ToArray();
        return s[Math.Min(s.Length - 1, (int)Math.Ceiling(p * s.Length) - 1)];
    }

    private static double DirectorySizeMb(string root) =>
        Math.Round(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1048576.0, 1);

    private static string Word(int i) => new[] { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet", "kilo", "lima", "mike", "november", "oscar", "papa" }[i % 16];

    private static List<Annotation> SampleAnnotations(int i, int w, int h) =>
    [
        new ArrowAnnotation(new PointD(w * 0.2, h * 0.2), new PointD(w * 0.5, h * 0.45)),
        new TextAnnotation(new PointD(w * 0.55, h * 0.5), $"Step {i % 9 + 1}", 18),
    ];

    /// <summary>A UI-like image (flat panels, a header, text-like stripes) so compression behaves like real screenshots.</summary>
    internal static PixelBuffer SyntheticScreenshot(int seed, int w, int h)
    {
        var img = new PixelBuffer(w, h);
        img.FillRect(new PixelRect(0, 0, w, h), new ArgbColor(255, 245, 246, 248));
        var accent = new ArgbColor(255, (byte)(40 + (seed * 37 % 180)), (byte)(80 + (seed * 53 % 150)), (byte)(120 + (seed * 71 % 130)));
        img.FillRect(new PixelRect(0, 0, w, 40), accent);
        img.FillRect(new PixelRect(0, 40, 160, h - 40), new ArgbColor(255, 32, 36, 44));
        var rng = new Random(seed);
        for (int row = 0; row < (h - 60) / 22; row++)
        {
            int y = 60 + (row * 22);
            int x = 180;
            while (x < w - 40)
            {
                int len = rng.Next(12, 70);
                img.FillRect(new PixelRect(x, y, Math.Min(len, w - 20 - x), 8), new ArgbColor(255, 90, 96, 110));
                x += len + rng.Next(5, 14);
            }
        }

        img.FillRect(new PixelRect(w - 180, h - 60, 140, 32), accent);
        return img;
    }

    private static string RuntimeInfo() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}";

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

internal sealed record SizeResult(
    int Captures,
    double OpenDeferredMs,
    double IndexMs,
    double IndexMemoryMb,
    double ProcessWorkingSetMb,
    double SearchCommonMs,
    double SearchRareMs,
    double ViewAllMs,
    double ThumbnailMsPerItem,
    double AutosaveMedianMs,
    double AutosaveP95Ms,
    double BaseSaveMedianMs,
    double CreateMsRecent,
    double SeedSeconds,
    double DiskMb);

internal sealed record Report(string Runtime, int Processors, string ImageSize, string Durability, IReadOnlyList<SizeResult> Results, IReadOnlyList<string> Skipped, double TotalMinutes);
