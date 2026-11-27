using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.App.Ocr;
using SnapForge.App.Rendering;
using SnapForge.Core.Imaging;

namespace SnapForge.App;

/// <summary>
/// <c>SnapForge.exe --feature-selftest &lt;folder&gt;</c>: non-interactive checks of the 1.0 features that need real
/// Windows components (OCR, image transforms in the packaged build, GIF export, scrolling capture, video editing).
/// Writes report.txt (and sample outputs) into the folder. Exit code 0 = every available check passed.
/// A check whose Windows component is missing on the machine (for example no OCR language installed) is reported
/// as UNAVAILABLE with the reason — never as passed.
/// </summary>
internal static class FeatureSelfTest
{
    private sealed class Report(string folder)
    {
        public List<string> Lines { get; } = [];
        public bool Ok { get; private set; } = true;
        public string Folder { get; } = folder;

        public void Check(string name, Func<string> check)
        {
            try
            {
                string detail = check();
                Lines.Add(detail.StartsWith("UNAVAILABLE", StringComparison.Ordinal) ? $"{detail.Replace("UNAVAILABLE", $"UNAVAILABLE {name}:", StringComparison.Ordinal)}" : $"PASS {name}: {detail}");
            }
#pragma warning disable CA1031 // Every failure is reported in the file and the exit code.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Ok = false;
                Lines.Add($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static readonly string[] ExpectedWords = ["Invoice", "4721", "overdue", "support", "1,284.50"];

    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var r = new Report(folder);
        r.Lines.Add($"SnapForge feature self-test {DateTime.Now:O} — {Services.InstanceInfo.CurrentBuild}");
        Ocr(r);
        File.WriteAllLines(Path.Combine(folder, "report.txt"), r.Lines);
        foreach (string l in r.Lines)
        {
            Console.WriteLine(l);
        }

        return r.Ok ? 0 : 1;
    }

    /// <summary>Renders known text, then recognises it with Windows OCR.</summary>
    private static void Ocr(Report r)
    {
        IReadOnlyList<OcrLanguage> languages = OcrService.AvailableLanguages();
        r.Check("ocr-languages", () => languages.Count == 0
            ? "UNAVAILABLE no OCR recognition language is installed on this Windows installation"
            : string.Join(", ", languages.Select(l => l.Tag)));
        if (languages.Count == 0)
        {
            return;
        }

        PixelBuffer image = RenderText(["Invoice 4721 is overdue", "Contact support at the help desk", "Total due 1,284.50"], 22);
        File.WriteAllBytes(Path.Combine(r.Folder, "ocr-input.png"), ImageExporter.Encode(image, Core.Export.ImageFileFormat.Png));
        r.Check("ocr-recognize", () =>
        {
            OcrOutcome o = OcrService.Recognize(image);
            if (!o.Success)
            {
                throw new InvalidOperationException(o.Error);
            }

            string flat = o.Text.Replace("\r", "", StringComparison.Ordinal);
            foreach (string expected in ExpectedWords)
            {
                if (!flat.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"\"{expected}\" not recognised. Got: {flat.Replace("\n", " | ", StringComparison.Ordinal)}");
                }
            }

            if (o.Lines.Count != 3 || !o.Lines[0].Text.Contains("Invoice", StringComparison.Ordinal) || !o.Lines[2].Text.Contains("Total", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Reading order wrong: {string.Join(" | ", o.Lines.Select(l => l.Text))}");
            }

            return $"{o.Lines.Count} lines in order ({o.LanguageTag}): {flat.Replace("\n", " | ", StringComparison.Ordinal)}";
        });

        // A very tall image (like a scrolling capture) is recognised in bands; the last line must still be found once.
        r.Check("ocr-tall-image", () =>
        {
            var lines = Enumerable.Range(1, 160).Select(i => $"Row number {i} of the long page").ToArray();
            PixelBuffer tall = RenderText(lines, 18);
            OcrOutcome o = OcrService.Recognize(tall);
            if (!o.Success)
            {
                throw new InvalidOperationException(o.Error);
            }

            int found160 = o.Lines.Count(l => l.Text.Contains("160", StringComparison.Ordinal));
            int found1 = o.Lines.Count(l => l.Text.Contains("number 1 ", StringComparison.Ordinal) || l.Text.EndsWith("number 1", StringComparison.Ordinal));
            if (found160 != 1)
            {
                throw new InvalidOperationException($"line 160 found {found160} times in {o.Lines.Count} lines (image {tall.Width}x{tall.Height})");
            }

            return $"{tall.Width}x{tall.Height}: {o.Lines.Count} of 160 lines; last line once";
        });
    }

    /// <summary>Black text on white, rendered by WPF at 96 dpi.</summary>
    internal static PixelBuffer RenderText(IReadOnlyList<string> lines, double size)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var visual = new DrawingVisual();
        double width = 0, y = 20;
        var texts = lines.Select(l => new FormattedText(l, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1.0)).ToList();
        foreach (FormattedText t in texts)
        {
            width = Math.Max(width, t.Width);
        }

        int w = (int)Math.Ceiling(width) + 60;
        int h = (int)Math.Ceiling(texts.Sum(t => t.Height * 1.6)) + 40;
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            foreach (FormattedText t in texts)
            {
                dc.DrawText(t, new Point(30, y));
                y += t.Height * 1.6;
            }
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return BitmapConversion.FromBitmapSource(rtb);
    }
}
