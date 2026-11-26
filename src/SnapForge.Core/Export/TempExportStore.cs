using System.Globalization;
using System.Security.Cryptography;

namespace SnapForge.Core.Export;

/// <summary>
/// Short-lived files created for drag-and-drop (a drop target needs real files: CF_HDROP). Each drag gets its own
/// random sub-folder named "&lt;UTC yyyyMMddHHmmss&gt;-&lt;random&gt;" under a per-user root. Folders are removed by
/// <see cref="Sweep"/> once older than a grace period (drop targets may still be copying), on exit, and at the next
/// start (after a crash). Contents are flattened exports only — never editable or original data.
/// </summary>
public sealed class TempExportStore
{
    public TempExportStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>How long a drag's files are kept after the drag ends (targets may copy asynchronously).</summary>
    public static TimeSpan GracePeriod { get; } = TimeSpan.FromMinutes(2);

    public string Root { get; }

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "Temp", "Drag");

    /// <summary>Creates a new, empty, uniquely named folder for one drag operation.</summary>
    public string NewSession(DateTime utcNow)
    {
        string name = string.Create(CultureInfo.InvariantCulture, $"{utcNow:yyyyMMddHHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}");
        string dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Deletes session folders created more than <paramref name="maxAge"/> before <paramref name="utcNow"/>. Returns how many were removed.</summary>
    public int Sweep(DateTime utcNow, TimeSpan maxAge)
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }

        int removed = 0;
        foreach (string dir in Directory.EnumerateDirectories(Root))
        {
            if (!TryParseCreated(Path.GetFileName(dir), out DateTime created) || utcNow - created >= maxAge)
            {
                if (TryDelete(dir))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    /// <summary>Deletes every session folder (used on exit; files still locked by a target are skipped).</summary>
    public int DeleteAll(DateTime utcNow) => Sweep(utcNow.AddYears(100), TimeSpan.Zero);

    /// <summary>Deletes one session folder now (cancelled drag).</summary>
    public static bool DeleteSession(string sessionDir) => TryDelete(sessionDir);

    internal static bool TryParseCreated(string name, out DateTime createdUtc)
    {
        createdUtc = default;
        int dash = name.IndexOf('-', StringComparison.Ordinal);
        return dash == 14 && DateTime.TryParseExact(name[..dash], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out createdUtc);
    }

    private static bool TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // still in use by a drop target; a later sweep retries
        }
    }
}
