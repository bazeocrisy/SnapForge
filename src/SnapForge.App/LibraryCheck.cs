using System.IO;
using SnapForge.Core;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;

namespace SnapForge.App;

/// <summary>
/// <c>SnapForge.exe --library-check &lt;report&gt; [--expect-images N] [--expect-videos N]</c>: opens the CURRENT user's
/// real settings and Library (or the selected --profile) without changing anything a normal start would not, verifies
/// every image capture loads with valid hashes, and writes a report. Used by CI to prove upgrades and migrations keep
/// user data; also useful for testers. Exit code 0 = all checks passed.
/// </summary>
internal static class LibraryCheck
{
    public static int Run(string[] args)
    {
        string report = args[1];
        int? expectImages = Option(args, "--expect-images"), expectVideos = Option(args, "--expect-videos");
        var lines = new List<string> { $"SnapForge library check {DateTime.Now:O}", $"Data folder: {ProductInfo.DataRoot}" };
        bool ok = true;
        try
        {
            string settingsPath = AppSettings.DefaultPath();
            AppSettings settings = AppSettings.Load(settingsPath);
            lines.Add($"PASS settings: {(File.Exists(settingsPath) ? "loaded" : "not present (defaults)")} from {settingsPath}");
            string root = settings.LibraryPath ?? CaptureLibrary.DefaultRoot();
            CaptureLibrary library = CaptureLibrary.Open(root);
            IReadOnlyList<CaptureRecord> all = library.Records;
            int images = all.Count(r => !r.IsVideo && r.Damage is null), videos = all.Count(r => r.IsVideo && r.Damage is null);
            int damaged = all.Count(r => r.Damage is not null), trash = all.Count(r => r.IsDeleted);
            lines.Add($"INFO library: {root}");
            lines.Add($"INFO items: {images} image(s), {videos} video(s), {damaged} damaged, {trash} in Trash");
            foreach (CaptureRecord r in all.Where(r => r.Damage is not null))
            {
                lines.Add($"INFO damaged item {r.Id}: {r.Damage}");
            }

            int verified = 0;
            foreach (CaptureRecord r in all.Where(r => !r.IsVideo && r.Damage is null))
            {
                try
                {
                    library.Load(r.Id);
                    verified++;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    ok = false;
                    lines.Add($"FAIL image {r.Id}: {ex.Message}");
                }
            }

            lines.Add($"{(verified == images ? "PASS" : "FAIL")} image integrity: {verified}/{images} loaded with valid hashes");
            if (expectImages is int ei)
            {
                ok &= Expect(lines, "image count", images, ei);
            }

            if (expectVideos is int ev)
            {
                ok &= Expect(lines, "video count", videos, ev);
            }
        }
#pragma warning disable CA1031 // Every failure is reported in the file and the exit code.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ok = false;
            lines.Add($"FAIL {ex.GetType().Name}: {ex.Message}");
        }

        lines.Add(ok ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllLines(report, lines);
        return ok ? 0 : 1;
    }

    private static bool Expect(List<string> lines, string what, int actual, int expected)
    {
        bool pass = actual == expected;
        lines.Add($"{(pass ? "PASS" : "FAIL")} {what}: {actual} (expected {expected})");
        return pass;
    }

    private static int? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : null;
    }
}
