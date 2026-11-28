using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.App.Ocr;
using SnapForge.App.Video;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Settings;
using SnapForge.Core.Video;
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
internal static unsafe class FeatureSelfTest
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
        VideoEditing(r);
        ImageTransforms(r);
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

    /// <summary>
    /// Writes a 6-second MP4 whose every second has its own solid colour (red, green, blue, yellow, cyan, magenta)
    /// with a 440 Hz tone, then: trim 0.5 s from the start and end, remove second 2–3, export, and verify by decoding
    /// the result (duration, the colour of each kept part, audio present). Then a GIF of seconds 1–4.
    /// </summary>
    private static void VideoEditing(Report r)
    {
        string src = Path.Combine(r.Folder, "edit-source.mp4");
        (byte R, byte G, byte B)[] colours = [(220, 30, 30), (30, 200, 30), (30, 30, 220), (230, 220, 30), (30, 220, 220), (220, 30, 220)];
        r.Check("video-source", () =>
        {
            _ = Video.Interop.Mf.MFStartup(Video.Interop.Mf.MF_VERSION, 0);
            const int w = 320, h = 240, fps = 30;
            using (var writer = new Mp4Writer(src, new VideoStreamFormat(w, h, fps, 1_500_000), withAudio: true, fragmented: false, EncoderInput.SystemMemory, 0))
            {
                byte[] frame = new byte[w * h * 4];
                short[] audio = new short[Mp4Writer.AudioSampleRate / fps * 2];
                long frameDuration = 10_000_000L / fps;
                for (int i = 0; i < fps * colours.Length; i++)
                {
                    (byte cr, byte cg, byte cb) = colours[i / fps];
                    for (int o = 0; o < frame.Length; o += 4)
                    {
                        frame[o] = cb;
                        frame[o + 1] = cg;
                        frame[o + 2] = cr;
                        frame[o + 3] = 255;
                    }

                    fixed (byte* p = frame)
                    {
                        writer.WriteFrame(p, w * 4, i * frameDuration, frameDuration);
                    }

                    for (int k = 0; k < audio.Length / 2; k++)
                    {
                        long n = ((long)i * (audio.Length / 2)) + k;
                        short v = (short)(Math.Sin(2 * Math.PI * 440 * n / Mp4Writer.AudioSampleRate) * 8000);
                        audio[k * 2] = v;
                        audio[(k * 2) + 1] = v;
                    }

                    writer.WriteAudio(audio, i * frameDuration);
                }

                writer.Complete();
            }

            VideoFileInfo info = VideoExporter.Inspect(src);
            return $"{info.Width}x{info.Height} {info.FramesPerSecond} fps {info.DurationMs} ms audio={info.HasAudio}";
        });

        string edited = Path.Combine(r.Folder, "edited.mp4");
        r.Check("video-trim-and-cut", () =>
        {
            var plan = new VideoEditPlan(6000);
            plan.SetTrim(500, 5500);
            plan.RemoveSection(2000, 3000);
            VideoExporter.ExportEdited(src, edited, plan, VideoQuality.Standard, null, CancellationToken.None);
            VideoFileInfo info = VideoExporter.Inspect(edited);
            if (Math.Abs(info.DurationMs - 4000) > 150)
            {
                throw new InvalidOperationException($"expected ~4000 ms, got {info.DurationMs} ms");
            }

            if (!info.HasAudio)
            {
                throw new InvalidOperationException("the edited video lost its sound");
            }

            // Output timeline: 0–1.5 s = source 0.5–2 (red then green), 1.5–4 s = source 3–5.5 (yellow, cyan).
            (double At, int Colour)[] expected = [(200, 0), (1200, 1), (1700, 3), (3000, 4)];
            foreach ((double at, int c) in expected)
            {
                PixelBuffer? f = VideoAnalysis.DecodeFrame(edited, at) ?? throw new InvalidOperationException($"no frame at {at} ms");
                ArgbColor px = f.GetPixel(f.Width / 2, f.Height / 2);
                (byte er, byte eg, byte eb) = colours[c];
                if (Math.Abs(px.R - er) > 40 || Math.Abs(px.G - eg) > 40 || Math.Abs(px.B - eb) > 40)
                {
                    throw new InvalidOperationException($"at {at} ms expected colour {c} ({er},{eg},{eb}) but got ({px.R},{px.G},{px.B})");
                }
            }

            return $"6.0 s → {info.DurationMs} ms; kept parts in order (red, green | yellow, cyan); audio kept; {new FileInfo(edited).Length / 1024} KB";
        });

        r.Check("video-gif-export", () =>
        {
            string gif = Path.Combine(r.Folder, "clip.gif");
            long bytes = GifExport.Write(src, gif, 1000, 4000, new GifExportSettings(10, 160, Dither: false, Loop: true), null, CancellationToken.None);
            byte[] head = File.ReadAllBytes(gif).AsSpan(0, 6).ToArray();
            if (System.Text.Encoding.ASCII.GetString(head) != "GIF89a")
            {
                throw new InvalidOperationException("not a GIF89a file");
            }

            int frames = CountGifImages(File.ReadAllBytes(gif));
            return $"{bytes} bytes, {frames} image blocks (3 s at 10 fps with identical frames merged)";
        });

        r.Check("video-frame-extract", () =>
        {
            PixelBuffer f = VideoAnalysis.DecodeFrame(src, 4500) ?? throw new InvalidOperationException("no frame");
            ArgbColor px = f.GetPixel(10, 10);
            // Second 4–5 of the source is cyan (30, 220, 220).
            if (px.R > 80 || px.G < 150 || px.B < 150)
            {
                throw new InvalidOperationException($"frame at 4.5 s should be cyan, got ({px.R},{px.G},{px.B})");
            }

            return $"{f.Width}x{f.Height} frame at 4.5 s has the right colour";
        });
    }

    /// <summary>Counts image descriptors (0x2C) in a GIF by walking its blocks.</summary>
    private static int CountGifImages(byte[] g)
    {
        int i = 13 + ((g[10] & 0x80) != 0 ? 3 * (1 << ((g[10] & 7) + 1)) : 0), n = 0;
        while (i < g.Length && g[i] != 0x3B)
        {
            if (g[i] == 0x21)
            {
                i += 2;
                while (g[i] != 0)
                {
                    i += g[i] + 1;
                }

                i++;
            }
            else if (g[i] == 0x2C)
            {
                n++;
                byte packed = g[i + 9];
                i += 10 + ((packed & 0x80) != 0 ? 3 * (1 << ((packed & 7) + 1)) : 0);
                i++; // LZW minimum code size
                while (g[i] != 0)
                {
                    i += g[i] + 1;
                }

                i++;
            }
            else
            {
                throw new InvalidDataException($"unexpected GIF block 0x{g[i]:X2}");
            }
        }

        return n;
    }

    /// <summary>The packaged build's image commands (rotate, resize, blur) produce the expected pixels.</summary>
    private static void ImageTransforms(Report r)
    {
        r.Check("image-performance", () =>
        {
            var big = new PixelBuffer(3840, 2160);
            for (int i = 0; i < big.Pixels.Length; i++)
            {
                big.Pixels[i] = (byte)(i * 7);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _ = ImageOps.Rotate90Clockwise(big);
            _ = ImageOps.Resize(big, 1920, 1080);
            _ = ImageOps.Resize(big, 5000, 3000);
            _ = ImageOps.BoxBlur(big, big.Bounds, 8);
            sw.Stop();
            if (sw.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new InvalidOperationException($"4K rotate + 2 resizes + blur took {sw.Elapsed.TotalSeconds:0.0} s (target < 10 s)");
            }

            return $"4K rotate + downscale + upscale to 5000x3000 + blur r8: {sw.Elapsed.TotalMilliseconds:0} ms";
        });

        r.Check("gif-encoder-performance", () =>
        {
            // Same workload as the Core unit test, measured here without coverage instrumentation.
            int w = 640, h = 360;
            var frame = new PixelBuffer(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    frame.SetPixel(x, y, ArgbColor.FromRgb((byte)(x * 255 / w), (byte)(y * 255 / h), 128));
                }
            }

            using var ms = new MemoryStream();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var encoder = new GifEncoder(ms, w, h, new GifEncoderOptions { Dithering = GifDithering.OrderedBayer4x4 }, leaveOpen: true))
            {
                for (int k = 0; k < 30; k++)
                {
                    frame.FillRect(new PixelRect(20 + (k * 15), 100, 12, 18), ArgbColor.Black);
                    frame.FillRect(new PixelRect(40, 120, 10 + (k * 8), 14), ArgbColor.FromRgb(30, 30, 200));
                    encoder.AddFrame(frame, 6);
                }

                encoder.Finish();
            }

            sw.Stop();
            if (sw.Elapsed > TimeSpan.FromSeconds(3))
            {
                throw new InvalidOperationException($"30 GIF frames of 640x360 took {sw.Elapsed.TotalSeconds:0.0} s (target < 3 s)");
            }

            return $"30 frames 640x360 (ordered dither): {sw.Elapsed.TotalMilliseconds:0} ms, {ms.Length} bytes";
        });

        r.Check("image-transforms", () =>
        {
            PixelBuffer img = RenderText(["Transform check 123"], 20);
            var doc = new EditorDocument(img);
            doc.Execute(new TransformImageCommand(ImageTransform.Rotate90));
            doc.Execute(new ResizeImageCommand(doc.Image.Width * 2, doc.Image.Height * 2));
            doc.Execute(new ApplyRegionEffectCommand(new PixelRect(0, 0, 10, 10), RegionEffect.Pixelate, 5));
            if (doc.Image.Width != img.Height * 2 || doc.Image.Height != img.Width * 2)
            {
                throw new InvalidOperationException($"size {doc.Image.Width}x{doc.Image.Height}");
            }

            doc.Undo();
            doc.Undo();
            doc.Undo();
            if (!doc.Image.Pixels.AsSpan().SequenceEqual(img.Pixels))
            {
                throw new InvalidOperationException("undo did not restore the original pixels");
            }

            return "rotate 90 + 200 % resize + pixelate, then undo ×3 restores the exact original";
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
