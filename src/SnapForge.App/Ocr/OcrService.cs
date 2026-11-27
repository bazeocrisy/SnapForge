using System.Runtime.InteropServices;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Ocr;

/// <summary>One recognised line: its text and its bounding box in image pixels.</summary>
public sealed record OcrLine(string Text, double X, double Y, double Width, double Height);

/// <summary>Result of a recognition run. <see cref="Text"/> joins the lines in Windows' reading order.</summary>
public sealed record OcrOutcome(bool Success, string Text, IReadOnlyList<OcrLine> Lines, string LanguageTag, string? Error)
{
    public static OcrOutcome Failed(string error) => new(false, string.Empty, [], string.Empty, error);
}

/// <summary>A recognition language installed on this PC.</summary>
public sealed record OcrLanguage(string Tag, string DisplayName);

/// <summary>
/// Offline text recognition with the OCR engine built into Windows (<c>Windows.Media.Ocr</c>), called through raw
/// WinRT ABI (no projection assembly, no new dependency). Everything runs locally: no network, no cloud service.
/// Recognition languages are the Windows "Optical character recognition" language features the user has installed
/// (Settings → Time &amp; language → Language &amp; region). Interface IDs and vtable slots were read from
/// Windows.Foundation.UniversalApiContract.winmd (Microsoft.Windows.SDK.Contracts 10.0.26100.1742).
/// </summary>
public static unsafe class OcrService
{
    private static readonly Guid IID_IOcrEngineStatics = new("5BFFA85A-3384-3540-9940-699120D428A8");
    private static readonly Guid IID_ISoftwareBitmapFactory = new("C99FEB69-2D62-4D47-A6B3-4FDB6A07FDF8");
    private static readonly Guid IID_ILanguageFactory = new("9B0252AC-0C27-44F8-B792-9793FB66C63E");
    private static readonly Guid IID_IAsyncInfo = new("00000036-0000-0000-C000-000000000046");
    private static readonly Guid IID_IMemoryBuffer = new("FBC4DD2A-245B-11E4-AF98-689423260CF8");
    private static readonly Guid IID_IMemoryBufferByteAccess = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
    private static readonly Guid IID_IClosable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");

    // Slots (IInspectable methods occupy 0-5).
    private const int StaticsMaxImageDimension = 6, StaticsAvailableLanguages = 7, StaticsTryCreateFromLanguage = 9,
        StaticsTryCreateFromUserProfile = 10;
    private const int EngineRecognizeAsync = 6, EngineRecognizerLanguage = 7;
    private const int ResultLines = 6, LineWords = 6, LineText = 7, WordBoundingRect = 6;
    private const int VectorGetAt = 6, VectorSize = 7;
    private const int LanguageTag = 6, LanguageDisplayName = 7;
    private const int LanguageFactoryCreate = 6;
    private const int BitmapFactoryCreate = 6, BitmapLockBuffer = 15;
    private const int BufferGetPlaneDescription = 7, MemoryBufferCreateReference = 6, ByteAccessGetBuffer = 3;
    private const int AsyncInfoStatus = 7, AsyncInfoErrorCode = 8, AsyncInfoCancel = 9, AsyncOperationGetResults = 8;
    private const int ClosableClose = 6;
    private const int PixelFormatBgra8 = 87, AccessWrite = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct PlaneDescription
    {
        public int StartIndex, Width, Height, Stride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public float X, Y, Width, Height;
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern char* WindowsGetStringRawBuffer(nint hstring, out uint length);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint hstring);

    /// <summary>Recognition languages installed on this PC (empty if OCR is unavailable).</summary>
    public static IReadOnlyList<OcrLanguage> AvailableLanguages() => RunOnMta(() =>
    {
        var result = new List<OcrLanguage>();
        nint statics = Statics();
        nint vector = 0;
        try
        {
            Com.Check(Com.Call(statics, StaticsAvailableLanguages, (nint)(&vector)), "AvailableRecognizerLanguages");
            uint size;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(vector, VectorSize))(vector, &size), "Size");
            for (uint i = 0; i < size; i++)
            {
                nint lang;
                Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(vector, VectorGetAt))(vector, i, &lang), "GetAt");
                try
                {
                    result.Add(new OcrLanguage(GetString(lang, LanguageTag), GetString(lang, LanguageDisplayName)));
                }
                finally
                {
                    Com.Release(ref lang);
                }
            }
        }
        finally
        {
            Com.Release(ref vector);
            Com.Release(ref statics);
        }

        return (IReadOnlyList<OcrLanguage>)result;
    }, []);

    /// <summary>
    /// Recognises text in <paramref name="image"/>. <paramref name="languageTag"/> null = the user's Windows
    /// languages (first one that has an installed recognizer). Never throws; failures come back in
    /// <see cref="OcrOutcome.Error"/> with a message suitable for the user.
    /// </summary>
    public static OcrOutcome Recognize(PixelBuffer image, string? languageTag = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        return RunOnMta(() => RecognizeCore(image, languageTag, cancel),
            OcrOutcome.Failed("Text recognition is not available on this version of Windows."));
    }

    private static OcrOutcome RecognizeCore(PixelBuffer image, string? languageTag, CancellationToken cancel)
    {
        nint statics = Statics();
        nint engine = 0;
        try
        {
            if (languageTag is null)
            {
                Com.Check(Com.Call(statics, StaticsTryCreateFromUserProfile, (nint)(&engine)), "TryCreateFromUserProfileLanguages");
            }
            else
            {
                nint lang = CreateLanguage(languageTag);
                try
                {
                    Com.Check(Com.Call(statics, StaticsTryCreateFromLanguage, lang, (nint)(&engine)), "TryCreateFromLanguage");
                }
                finally
                {
                    Com.Release(ref lang);
                }
            }

            if (engine == 0)
            {
                return OcrOutcome.Failed(languageTag is null
                    ? "No text-recognition language is installed. In Windows Settings → Time & language → Language & region, " +
                      "open a language's options and add \"Optical character recognition\"."
                    : $"Text recognition for \"{languageTag}\" is not installed. Add it in Windows Settings → Time & language → " +
                      "Language & region → (language) → Language options → Optical character recognition.");
            }

            string engineLanguage = "";
            nint engineLang;
            if (Com.Call(engine, EngineRecognizerLanguage, (nint)(&engineLang)) >= 0 && engineLang != 0)
            {
                engineLanguage = GetString(engineLang, LanguageTag);
                Com.Release(ref engineLang);
            }

            uint maxDim;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(statics, StaticsMaxImageDimension))(statics, &maxDim), "MaxImageDimension");
            if (maxDim == 0)
            {
                maxDim = 10000;
            }

            // Wide images are scaled to fit; tall images (scrolling captures) are recognised in horizontal bands
            // so that small text is not shrunk below what the recognizer can read.
            var lines = new List<OcrLine>();
            double scale = image.Width > maxDim ? (double)maxDim / image.Width : 1.0;
            PixelBuffer source = scale < 1.0 ? ImageScaler.Scale(image, scale) : image;
            int bandHeight = (int)maxDim;
            const int overlap = 64;
            for (int top = 0; top < source.Height; top += bandHeight - overlap)
            {
                cancel.ThrowIfCancellationRequested();
                int h = Math.Min(bandHeight, source.Height - top);
                PixelBuffer band = top == 0 && h == source.Height ? source : source.Crop(new Core.Geometry.PixelRect(0, top, source.Width, h));
                foreach (OcrLine l in RecognizeBand(engine, band, cancel))
                {
                    // Lines that start in the overlap belong to the next band, except in the last band.
                    bool last = top + h >= source.Height;
                    if (!last && l.Y >= h - overlap)
                    {
                        continue;
                    }

                    if (top > 0 && l.Y + l.Height <= overlap)
                    {
                        continue;
                    }

                    lines.Add(new OcrLine(l.Text, l.X / scale, (l.Y + top) / scale, l.Width / scale, l.Height / scale));
                }

                if (top + h >= source.Height)
                {
                    break;
                }
            }

            string text = string.Join(Environment.NewLine, lines.Select(l => l.Text));
            return new OcrOutcome(true, text, lines, engineLanguage, lines.Count == 0 ? "No text was found." : null);
        }
        catch (OperationCanceledException)
        {
            return OcrOutcome.Failed("Text recognition was cancelled.");
        }
        catch (NativeCallException ex)
        {
            return OcrOutcome.Failed($"Text recognition failed: {ex.Message}");
        }
        finally
        {
            Com.Release(ref engine);
            Com.Release(ref statics);
        }
    }

    private static List<OcrLine> RecognizeBand(nint engine, PixelBuffer image, CancellationToken cancel)
    {
        nint bitmap = CreateSoftwareBitmap(image);
        nint operation = 0, result = 0, linesVector = 0;
        try
        {
            Com.Check(Com.Call(engine, EngineRecognizeAsync, bitmap, (nint)(&operation)), "RecognizeAsync");
            Wait(operation, cancel);
            Com.Check(Com.Call(operation, AsyncOperationGetResults, (nint)(&result)), "GetResults");
            Com.Check(Com.Call(result, ResultLines, (nint)(&linesVector)), "Lines");
            uint count;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(linesVector, VectorSize))(linesVector, &count), "Size");
            var lines = new List<OcrLine>((int)count);
            for (uint i = 0; i < count; i++)
            {
                nint line;
                Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(linesVector, VectorGetAt))(linesVector, i, &line), "GetAt");
                try
                {
                    string text = GetString(line, LineText);
                    (double x, double y, double w, double h) = LineBounds(line);
                    lines.Add(new OcrLine(text, x, y, w, h));
                }
                finally
                {
                    Com.Release(ref line);
                }
            }

            return lines;
        }
        finally
        {
            Com.Release(ref linesVector);
            Com.Release(ref result);
            Close(operation);
            Com.Release(ref operation);
            Close(bitmap);
            Com.Release(ref bitmap);
        }
    }

    private static (double X, double Y, double W, double H) LineBounds(nint line)
    {
        nint words = 0;
        try
        {
            Com.Check(Com.Call(line, LineWords, (nint)(&words)), "Words");
            uint count;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(words, VectorSize))(words, &count), "Size");
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
            for (uint i = 0; i < count; i++)
            {
                nint word;
                Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(words, VectorGetAt))(words, i, &word), "GetAt");
                WinRect rect;
                int hr = ((delegate* unmanaged[Stdcall]<nint, WinRect*, int>)Com.Slot(word, WordBoundingRect))(word, &rect);
                Com.Release(ref word);
                Com.Check(hr, "BoundingRect");
                l = Math.Min(l, rect.X);
                t = Math.Min(t, rect.Y);
                r = Math.Max(r, rect.X + rect.Width);
                b = Math.Max(b, rect.Y + rect.Height);
            }

            return count == 0 ? (0, 0, 0, 0) : (l, t, r - l, b - t);
        }
        finally
        {
            Com.Release(ref words);
        }
    }

    private static nint CreateSoftwareBitmap(PixelBuffer image)
    {
        nint factory = WinRt.GetActivationFactory("Windows.Graphics.Imaging.SoftwareBitmap", IID_ISoftwareBitmapFactory);
        nint bitmap = 0, buffer = 0, memory = 0, reference = 0, access = 0;
        try
        {
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int, int, nint*, int>)Com.Slot(factory, BitmapFactoryCreate))(factory, PixelFormatBgra8, image.Width, image.Height, &bitmap), "SoftwareBitmap.Create");
            Com.Check(Com.Call(bitmap, BitmapLockBuffer, AccessWrite, (nint)(&buffer)), "LockBuffer");
            PlaneDescription plane;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, PlaneDescription*, int>)Com.Slot(buffer, BufferGetPlaneDescription))(buffer, 0, &plane), "GetPlaneDescription");
            memory = Com.QueryInterface(buffer, IID_IMemoryBuffer);
            Com.Check(Com.Call(memory, MemoryBufferCreateReference, (nint)(&reference)), "CreateReference");
            access = Com.QueryInterface(reference, IID_IMemoryBufferByteAccess);
            byte* data;
            uint capacity;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, int>)Com.Slot(access, ByteAccessGetBuffer))(access, &data, &capacity), "GetBuffer");
            int rowBytes = image.Width * PixelBuffer.BytesPerPixel;
            if (plane.Stride < rowBytes || (long)plane.StartIndex + ((long)plane.Stride * (image.Height - 1)) + rowBytes > capacity)
            {
                throw new NativeCallException("SoftwareBitmap buffer is smaller than the image");
            }

            fixed (byte* src = image.Pixels)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    Buffer.MemoryCopy(src + ((long)y * rowBytes), data + plane.StartIndex + ((long)y * plane.Stride), rowBytes, rowBytes);
                    // Screenshots are opaque; make sure alpha is 255 so the recognizer sees the pixels.
                    byte* row = data + plane.StartIndex + ((long)y * plane.Stride);
                    for (int x = 3; x < rowBytes; x += 4)
                    {
                        row[x] = 255;
                    }
                }
            }

            nint result = bitmap;
            bitmap = 0;
            return result;
        }
        finally
        {
            Com.Release(ref access);
            Close(reference);
            Com.Release(ref reference);
            Com.Release(ref memory);
            Close(buffer);
            Com.Release(ref buffer);
            Com.Release(ref bitmap);
            Com.Release(ref factory);
        }
    }

    private static void Wait(nint operation, CancellationToken cancel)
    {
        nint info = Com.QueryInterface(operation, IID_IAsyncInfo);
        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (true)
            {
                int status;
                Com.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Slot(info, AsyncInfoStatus))(info, &status), "Status");
                if (status == 1)
                {
                    return; // Completed
                }

                if (status == 2)
                {
                    throw new OperationCanceledException();
                }

                if (status == 3)
                {
                    int error;
                    _ = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Slot(info, AsyncInfoErrorCode))(info, &error);
                    throw new NativeCallException("RecognizeAsync", error);
                }

                if (cancel.IsCancellationRequested || DateTime.UtcNow > deadline)
                {
                    _ = Com.Call(info, AsyncInfoCancel);
                    throw new OperationCanceledException();
                }

                Thread.Sleep(10);
            }
        }
        finally
        {
            Com.Release(ref info);
        }
    }

    private static nint Statics() =>
        WinRt.GetActivationFactory("Windows.Media.Ocr.OcrEngine", IID_IOcrEngineStatics);

    private static nint CreateLanguage(string tag)
    {
        nint factory = WinRt.GetActivationFactory("Windows.Globalization.Language", IID_ILanguageFactory);
        Com.Check(WindowsCreateString(tag, tag.Length, out nint hs), "WindowsCreateString");
        try
        {
            nint lang;
            Com.Check(Com.Call(factory, LanguageFactoryCreate, hs, (nint)(&lang)), "CreateLanguage");
            return lang;
        }
        finally
        {
            _ = WindowsDeleteString(hs);
            Com.Release(ref factory);
        }
    }

    private static string GetString(nint obj, int slot)
    {
        nint hs;
        Com.Check(Com.Call(obj, slot, (nint)(&hs)), "get string");
        try
        {
            char* p = WindowsGetStringRawBuffer(hs, out uint len);
            return p == null ? string.Empty : new string(p, 0, (int)len);
        }
        finally
        {
            _ = WindowsDeleteString(hs);
        }
    }

    private static void Close(nint obj)
    {
        if (obj == 0)
        {
            return;
        }

        nint closable = Com.TryQueryInterface(obj, IID_IClosable);
        if (closable != 0)
        {
            _ = Com.Call(closable, ClosableClose);
            Com.Release(ref closable);
        }
    }

    /// <summary>WinRT objects are used from a dedicated MTA thread so the WPF UI thread never blocks on them.</summary>
    private static T RunOnMta<T>(Func<T> work, T unavailable)
    {
        T result = unavailable;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            _ = WinRt.RoInitialize(1);
            try
            {
                result = work();
            }
            catch (NativeCallException)
            {
                result = unavailable;
            }
            catch (DllNotFoundException)
            {
                result = unavailable;
            }
            catch (EntryPointNotFoundException)
            {
                result = unavailable;
            }
            catch (OperationCanceledException ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
            Name = "SnapForge OCR",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }

        return result;
    }
}

/// <summary>Simple area-averaging downscale used only to fit OCR's maximum image dimension.</summary>
internal static class ImageScaler
{
    public static PixelBuffer Scale(PixelBuffer src, double scale)
    {
        int w = Math.Max(1, (int)Math.Round(src.Width * scale));
        int h = Math.Max(1, (int)Math.Round(src.Height * scale));
        var dst = new PixelBuffer(w, h);
        byte[] s = src.Pixels, d = dst.Pixels;
        for (int y = 0; y < h; y++)
        {
            int sy0 = (int)(y / scale), sy1 = Math.Max(sy0 + 1, Math.Min(src.Height, (int)((y + 1) / scale)));
            for (int x = 0; x < w; x++)
            {
                int sx0 = (int)(x / scale), sx1 = Math.Max(sx0 + 1, Math.Min(src.Width, (int)((x + 1) / scale)));
                long b = 0, g = 0, r = 0, n = 0;
                for (int yy = sy0; yy < sy1; yy++)
                {
                    int i = ((yy * src.Width) + sx0) * 4;
                    for (int xx = sx0; xx < sx1; xx++, i += 4)
                    {
                        b += s[i];
                        g += s[i + 1];
                        r += s[i + 2];
                        n++;
                    }
                }

                int o = ((y * w) + x) * 4;
                d[o] = (byte)(b / n);
                d[o + 1] = (byte)(g / n);
                d[o + 2] = (byte)(r / n);
                d[o + 3] = 255;
            }
        }

        return dst;
    }
}
