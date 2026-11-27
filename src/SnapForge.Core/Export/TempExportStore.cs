using System.Globalization;
using System.Security.Cryptography;

namespace SnapForge.Core.Export;

/// <summary>
/// Short-lived files created for drag-and-drop (a drop target needs real files: CF_HDROP). Each drag gets its own
/// random sub-folder named "&lt;UTC yyyyMMddHHmmss&gt;-&lt;random&gt;" under a per-user root.
/// <para>Retention (1.0): receiving apps may read a dropped file long after the drop — browsers upload lazily, mail
/// clients attach on send — so a drag's files are kept for <see cref="GracePeriod"/> (24 h), across exit and restart,
/// and are removed by <see cref="Sweep"/> afterwards. If the store grows beyond <see cref="MaxTotalBytes"/> (large
/// video drags), the oldest sessions are removed early, but never one younger than <see cref="MinimumAge"/>. A drag
/// that ended without a drop deletes its files immediately. Contents are flattened exports only (redactions burned in,
/// no metadata) — never editable or original data. Deleting a file does not overwrite it on disk.</para>
/// </summary>
public sealed class TempExportStore
{
    public TempExportStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>How long a drag's files are kept (targets may read them long after the drop).</summary>
    public static TimeSpan GracePeriod { get; } = TimeSpan.FromHours(24);

    /// <summary>Size cap for all drag sessions together; above it the oldest sessions go first.</summary>
    public static long MaxTotalBytes { get; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Sessions younger than this are never removed by the size cap (a drop may be in progress).</summary>
    public static TimeSpan MinimumAge { get; } = TimeSpan.FromMinutes(15);

    public string Root { get; }

    public static string DefaultRoot() =>
        Path.Combine(ProductInfo.DataRoot, "Temp", "Drag");

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

    /// <summary>
    /// Applies the retention policy: removes sessions older than <see cref="GracePeriod"/>, then, while the total size
    /// exceeds <paramref name="maxTotalBytes"/>, the oldest sessions older than <see cref="MinimumAge"/>.
    /// </summary>
    public int Enforce(DateTime utcNow, long maxTotalBytes)
    {
        int removed = Sweep(utcNow, GracePeriod);
        if (!Directory.Exists(Root))
        {
            return removed;
        }

        var sessions = Directory.EnumerateDirectories(Root)
            .Select(d => (Dir: d, Ok: TryParseCreated(Path.GetFileName(d), out DateTime c), Created: c, Bytes: SizeOf(d)))
            .Where(s => s.Ok)
            .OrderBy(s => s.Created)
            .ToList();
        long total = sessions.Sum(s => s.Bytes);
        foreach (var s in sessions)
        {
            if (total <= maxTotalBytes || utcNow - s.Created < MinimumAge)
            {
                break;
            }

            if (TryDelete(s.Dir))
            {
                total -= s.Bytes;
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Total size of all drag sessions (Settings shows it).</summary>
    public long TotalBytes() => Directory.Exists(Root) ? SizeOf(Root) : 0;

    /// <summary>Deletes every session folder now (Settings → "Delete drag copies now"; locked files are skipped).</summary>
    public int DeleteAll(DateTime utcNow) => Sweep(utcNow.AddYears(100), TimeSpan.Zero);

    private static long SizeOf(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

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
