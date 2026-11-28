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
/// that ended without a drop deletes its files immediately. FA-009: a session whose drag is still in progress carries an
/// <see cref="ActiveMarker"/> file and is never removed by the sweep or the size cap (a marker older than the grace
/// period is a leftover of a crash and no longer protects); after a drop the session is protected from the size cap
/// for <see cref="MinimumAge"/> counted from the drop, not from its creation. SnapForge cannot know when every
/// receiving app has finished reading a file: for a permanent copy, users export the capture instead. Contents are flattened exports only (redactions burned in,
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

    /// <summary>Present while the drag that owns the session is in progress.</summary>
    public const string ActiveMarker = ".snapforge-drag-active";

    /// <summary>Written when a drag ends with a drop; its time is the drop time.</summary>
    public const string DroppedMarker = ".snapforge-drag-dropped";

    /// <summary>Marks a session's drag as finished with a drop (protection then runs from the drop time).</summary>
    public static void MarkDropped(string sessionDir, DateTime utcNow)
    {
        try
        {
            string dropped = Path.Combine(sessionDir, DroppedMarker);
            File.WriteAllBytes(dropped, []); // the file time is the drop time; empty so it adds nothing to sizes
            File.SetLastWriteTimeUtc(dropped, utcNow);
            File.Delete(Path.Combine(sessionDir, ActiveMarker));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The session stays protected by its active marker until that marker becomes stale.
        }
    }

    public static string DefaultRoot() =>
        Path.Combine(ProductInfo.DataRoot, "Temp", "Drag");

    /// <summary>Creates a new, empty, uniquely named folder for one drag operation.</summary>
    public string NewSession(DateTime utcNow)
    {
        string name = string.Create(CultureInfo.InvariantCulture, $"{utcNow:yyyyMMddHHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}");
        string dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        string marker = Path.Combine(dir, ActiveMarker);
        File.WriteAllText(marker, string.Empty);
        File.SetLastWriteTimeUtc(marker, utcNow);
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
            if (IsActive(dir, utcNow))
            {
                continue;
            }

            if (!TryParseCreated(Path.GetFileName(dir), out DateTime created) || utcNow - EffectiveTime(dir, created) >= maxAge)
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
            .Select(s => (s.Dir, Time: EffectiveTime(s.Dir, s.Created), s.Bytes, Active: IsActive(s.Dir, utcNow)))
            .OrderBy(s => s.Time)
            .ToList();
        long total = sessions.Sum(s => s.Bytes);
        foreach (var s in sessions)
        {
            if (total <= maxTotalBytes)
            {
                break;
            }

            if (s.Active || utcNow - s.Time < MinimumAge)
            {
                continue; // a drag in progress or a recent drop is never evicted
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

    /// <summary>
    /// Deletes every finished session folder now (Settings → "Delete drag copies now"; locked files and drags still in
    /// progress are skipped).
    /// </summary>
    public int DeleteAll(DateTime utcNow)
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }

        int removed = 0;
        foreach (string dir in Directory.EnumerateDirectories(Root))
        {
            if (!IsActive(dir, utcNow) && TryDelete(dir))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>True while the owning drag is in progress (an active marker younger than the grace period).</summary>
    internal static bool IsActive(string dir, DateTime utcNow)
    {
        string marker = Path.Combine(dir, ActiveMarker);
        return File.Exists(marker) && utcNow - File.GetLastWriteTimeUtc(marker) < GracePeriod;
    }

    /// <summary>Retention clock of a session: the drop time when known, otherwise the creation time.</summary>
    private static DateTime EffectiveTime(string dir, DateTime created)
    {
        string dropped = Path.Combine(dir, DroppedMarker);
        try
        {
            return File.Exists(dropped) ? new DateTime(Math.Max(created.Ticks, File.GetLastWriteTimeUtc(dropped).Ticks), DateTimeKind.Utc) : created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return created;
        }
    }

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
