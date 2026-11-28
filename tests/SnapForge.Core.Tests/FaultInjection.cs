using System.IO.Compression;
using System.Text;
using SnapForge.Core.Imaging;
using SnapForge.Core.Library;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>Thrown by the fault hook to simulate a crash at a durable boundary.</summary>
public sealed class SimulatedCrashException : Exception
{
    public SimulatedCrashException()
    {
    }

    public SimulatedCrashException(string message)
        : base(message)
    {
    }

    public SimulatedCrashException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Drives <see cref="DurabilityFaults"/>: lists the checkpoints of an operation and crashes at the n-th.</summary>
internal static class Crash
{
    /// <summary>Runs <paramref name="operation"/> and returns every checkpoint it passed, in order.</summary>
    public static List<string> Checkpoints(Action operation)
    {
        var points = new List<string>();
        DurabilityFaults.Hook.Value = points.Add;
        try
        {
            operation();
        }
        finally
        {
            DurabilityFaults.Hook.Value = null;
        }

        return points;
    }

    /// <summary>Runs <paramref name="operation"/>, crashing at checkpoint <paramref name="n"/>; returns its name (null if not reached).</summary>
    public static string? At(int n, Action operation)
    {
        int count = 0;
        string? hit = null;
        DurabilityFaults.Hook.Value = point =>
        {
            if (count++ == n)
            {
                hit = point;
                throw new SimulatedCrashException($"Simulated crash at {point}");
            }
        };
        try
        {
            operation();
        }
        catch (SimulatedCrashException)
        {
        }
        finally
        {
            DurabilityFaults.Hook.Value = null;
        }

        return hit;
    }
}

/// <summary>Byte-level scans of library files for secret content (raw bytes and decompressed SFPX pixel data).</summary>
internal static class SecretScan
{
    public static byte[] PixelPattern()
    {
        var bytes = new List<byte>();
        foreach (ArgbColor c in LegacyFixtures.SecretPattern)
        {
            bytes.AddRange([c.B, c.G, c.R, c.A]);
        }

        return [.. bytes];
    }

    public static readonly byte[] SecretText = Encoding.UTF8.GetBytes("hunter2-secret");

    /// <summary>Files under <paramref name="folder"/> that contain the secret pixels or the hidden secret text.</summary>
    public static List<string> FilesContainingSecret(string folder)
    {
        byte[] pixels = PixelPattern();
        var hits = new List<string>();
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            byte[] data = File.ReadAllBytes(file);
            if (data.AsSpan().IndexOf(pixels) >= 0 || data.AsSpan().IndexOf(SecretText) >= 0 || (Decompressed(data) is { } raw && raw.AsSpan().IndexOf(pixels) >= 0))
            {
                hits.Add(file);
            }
        }

        return hits;
    }

    private static byte[]? Decompressed(byte[] data)
    {
        if (data.Length <= PixelCodec.HeaderSize || !data.AsSpan(0, 4).SequenceEqual("SFPX"u8))
        {
            return null;
        }

        try
        {
            using var z = new ZLibStream(new MemoryStream(data, PixelCodec.HeaderSize, data.Length - PixelCodec.HeaderSize), CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}

/// <summary>Assertions that a capture folder is coherent: one committed generation, all hashes valid, no leftovers.</summary>
internal static class Coherence
{
    public static void AssertCoherent(CaptureLibrary lib, Guid id)
    {
        CaptureRecord r = lib.Find(id) ?? throw new Xunit.Sdk.XunitException($"Capture {id} is not listed.");
        Assert.Null(r.Damage);
        CaptureContent content = lib.Load(id); // verifies base and annotations hashes
        Assert.Equal(content.Annotations.Count(a => a is not Core.Annotations.RedactionMaskAnnotation), r.AnnotationCount);
        CaptureManifest m = lib.ReadCaptureManifest(id);
        string folder = Path.Combine(lib.CapturesDirectory, id.ToString("D"));
        if (m.Thumb is { Sha256: not null } thumb)
        {
            CaptureFormat.ReadVerified(folder, thumb);
        }

        HashSet<string> referenced = m.ReferencedFiles();
        foreach (string file in Directory.GetFiles(folder))
        {
            Assert.True(referenced.Contains(Path.GetFileName(file)), $"Unreferenced file left in the capture folder: {Path.GetFileName(file)}");
        }
    }
}
