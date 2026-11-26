using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text;

namespace SnapForge.App.Services;

/// <summary>
/// Identity of the running SnapForge, published by the first instance so a second launch can tell whether it is the
/// same build. Before milestone 3 a second launch always handed over to the running copy, so testers who started a
/// new build while an older one was still in the notification area silently got the old one.
/// </summary>
internal sealed record InstanceInfo(string Build, string ExePath)
{
    private const int Capacity = 4096;

    /// <summary>The build identity: assembly informational version (CI adds "+&lt;commit&gt;").</summary>
    public static string CurrentBuild { get; } =
        typeof(InstanceInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(InstanceInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>Short form for display: version plus the first 7 characters of the commit, if present.</summary>
    /// <summary>The full Git commit of this build ("local build" when built outside CI).</summary>
    public static string Commit
    {
        get
        {
            int plus = CurrentBuild.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? "local build (no commit recorded)" : CurrentBuild[(plus + 1)..];
        }
    }

    /// <summary>The Git branch this build came from, when CI recorded it.</summary>
    public static string? Branch { get; } =
        typeof(InstanceInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "SourceBranch")?.Value;

    public static string ShortBuild(string build)
    {
        int plus = build.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 || build.Length - plus - 1 < 7 ? build : build[..(plus + 8)];
    }

    public static InstanceInfo Current() => new(CurrentBuild, Environment.ProcessPath ?? string.Empty);

    /// <summary>Publishes this instance's identity; keep the returned object alive for the life of the process.</summary>
    public static MemoryMappedFile? Publish(string name, InstanceInfo info)
    {
        try
        {
            var mmf = MemoryMappedFile.CreateOrOpen(name, Capacity);
            using MemoryMappedViewStream view = mmf.CreateViewStream();
            byte[] bytes = Encode(info);
            view.Write(bytes, 0, bytes.Length);
            return mmf;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the running instance's identity; null if it published none (builds before milestone 3).</summary>
    public static InstanceInfo? Read(string name)
    {
        try
        {
            using MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using MemoryMappedViewStream view = mmf.CreateViewStream(0, Capacity, MemoryMappedFileAccess.Read);
            byte[] bytes = new byte[Capacity];
            int read = view.Read(bytes, 0, bytes.Length);
            return Decode(bytes.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    internal static byte[] Encode(InstanceInfo info)
    {
        byte[] text = Encoding.UTF8.GetBytes($"{info.Build}\n{info.ExePath}");
        int length = Math.Min(text.Length, Capacity - 4);
        byte[] result = new byte[length + 4];
        BitConverter.GetBytes(length).CopyTo(result, 0);
        Array.Copy(text, 0, result, 4, length);
        return result;
    }

    internal static InstanceInfo? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
        {
            return null;
        }

        int length = BitConverter.ToInt32(bytes[..4]);
        if (length <= 0 || length > bytes.Length - 4)
        {
            return null;
        }

        string[] parts = Encoding.UTF8.GetString(bytes.Slice(4, length)).Split('\n', 2);
        return parts.Length == 2 ? new InstanceInfo(parts[0], parts[1]) : null;
    }
}
