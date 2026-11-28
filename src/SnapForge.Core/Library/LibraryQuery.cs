using System.Globalization;

namespace SnapForge.Core.Library;

public enum LibraryView
{
    All,
    Recent,
    Favorites,
    Tag,
    Folder,
    Trash,

    /// <summary>Screen recordings only.</summary>
    Videos,
}

public enum LibrarySort
{
    NewestFirst,
    OldestFirst,
    TitleAscending,
    LargestFirst,
    RecentlyModified,
}

public sealed record LibraryQuery
{
    public LibraryView View { get; init; } = LibraryView.All;

    /// <summary>Tag name for <see cref="LibraryView.Tag"/> or folder name for <see cref="LibraryView.Folder"/>.</summary>
    public string? Key { get; init; }

    public string Search { get; init; } = string.Empty;

    public LibrarySort Sort { get; init; } = LibrarySort.NewestFirst;

    /// <summary>How far back the Recent view looks.</summary>
    public static TimeSpan RecentWindow { get; } = TimeSpan.FromDays(7);

    public IEnumerable<CaptureRecord> Apply(IEnumerable<CaptureRecord> records, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(records);
        IEnumerable<CaptureRecord> q = View switch
        {
            LibraryView.Trash => records.Where(r => r.IsDeleted),
            LibraryView.Favorites => records.Where(r => !r.IsDeleted && r.IsFavorite),
            LibraryView.Recent => records.Where(r => !r.IsDeleted && nowUtc - r.RecentActivityUtc <= RecentWindow),
            LibraryView.Tag => records.Where(r => !r.IsDeleted && r.Meta.Tags.Contains(Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)),
            LibraryView.Folder => records.Where(r => !r.IsDeleted && string.Equals(r.Meta.Folder, Key ?? string.Empty, StringComparison.OrdinalIgnoreCase)),
            LibraryView.Videos => records.Where(r => !r.IsDeleted && r.IsVideo),
            _ => records.Where(r => !r.IsDeleted),
        };

        string[] terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length > 0)
        {
            q = q.Where(r => terms.All(t => Matches(r, t)));
        }

        Func<CaptureRecord, CaptureRecord, int> tiebreak = (a, b) => a.Id.CompareTo(b.Id);
        return (View == LibraryView.Recent && Sort == LibrarySort.NewestFirst ? LibrarySort.RecentlyModified : Sort) switch
        {
            LibrarySort.OldestFirst => q.OrderBy(r => r.CreatedUtc).ThenBy(r => r.Id),
            LibrarySort.TitleAscending => q.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Id),
            LibrarySort.LargestFirst => q.OrderByDescending(r => (long)r.Width * r.Height).ThenBy(r => r.Id),
            LibrarySort.RecentlyModified => q.OrderByDescending(r => r.RecentActivityUtc).ThenBy(r => r.Id),
            _ => q.OrderByDescending(r => r.CreatedUtc).ThenBy(r => r.Id),
        };
    }

    private static bool Matches(CaptureRecord r, string term)
    {
        const StringComparison ci = StringComparison.CurrentCultureIgnoreCase;
        return r.Title.Contains(term, ci)
            || r.Meta.Tags.Any(t => t.Contains(term, ci))
            || r.Meta.Folder.Contains(term, ci)
            || r.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)
            || $"{r.Width}x{r.Height}".Contains(term, StringComparison.OrdinalIgnoreCase)
            || (r.IsVideo && (term.Equals("video", ci) || term.Equals("recording", ci)));
    }
}

/// <summary>Date grouping for the library grid (Today, Yesterday, Earlier this week, This month, then by month).</summary>
public static class DateGrouping
{
    public static string GroupFor(DateTime localTime, DateTime localNow)
    {
        DateTime day = localTime.Date, today = localNow.Date;
        if (day == today)
        {
            return "Today";
        }

        if (day == today.AddDays(-1))
        {
            return "Yesterday";
        }

        int offset = ((int)today.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        if (day >= today.AddDays(-offset) && day < today)
        {
            return "Earlier this week";
        }

        if (day.Year == today.Year && day.Month == today.Month && day < today)
        {
            return "Earlier this month";
        }

        return localTime.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>Groups records (already sorted) preserving order; group order follows first appearance.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<CaptureRecord> Items)> Group(IEnumerable<CaptureRecord> sorted, DateTime localNow, Func<CaptureRecord, DateTime>? keyUtc = null)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        keyUtc ??= r => r.CreatedUtc;
        var groups = new List<(string, List<CaptureRecord>)>();
        foreach (CaptureRecord r in sorted)
        {
            string g = GroupFor(keyUtc(r).ToLocalTime(), localNow);
            if (groups.Count == 0 || groups[^1].Item1 != g)
            {
                int existing = groups.FindIndex(x => x.Item1 == g);
                if (existing >= 0)
                {
                    groups[existing].Item2.Add(r);
                    continue;
                }

                groups.Add((g, []));
            }

            groups[^1].Item2.Add(r);
        }

        return groups.Select(g => (g.Item1, (IReadOnlyList<CaptureRecord>)g.Item2)).ToList();
    }
}
