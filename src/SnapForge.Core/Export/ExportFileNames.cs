using System.Globalization;
using System.Text;

namespace SnapForge.Core.Export;

/// <summary>Builds meaningful, unique, Windows-safe file names for exported (dragged) captures.</summary>
public static class ExportFileNames
{
    /// <summary>Longest stem we produce; keeps full paths well under MAX_PATH inside the temp folder.</summary>
    public const int MaxStemLength = 80;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// "&lt;title&gt; &lt;yyyy-MM-dd HHmmss&gt;.png". The timestamp is the capture's local creation time and is not
    /// repeated if the title already contains it. <paramref name="used"/> collects names already handed out (case-
    /// insensitive); duplicates get " (2)", " (3)" … appended.
    /// </summary>
    public static string For(string? title, DateTime createdLocal, ImageFileFormat format, ISet<string> used)
    {
        ArgumentNullException.ThrowIfNull(used);
        string stamp = createdLocal.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        string clean = Sanitize(title);
        string stem;
        if (clean.Length == 0)
        {
            stem = $"{ProductInfo.FilePrefix} {stamp}";
        }
        else
        {
            string dateOnly = createdLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            stem = clean.Contains(dateOnly, StringComparison.Ordinal) ? clean : $"{Truncate(clean, MaxStemLength - stamp.Length - 1)} {stamp}";
        }

        stem = Truncate(stem, MaxStemLength);
        if (Reserved.Contains(stem) || Reserved.Contains(stem.Split('.')[0]))
        {
            stem = "_" + stem;
        }

        string ext = ExportRules.Extension(format);
        string name = stem + ext;
        for (int n = 2; used.Contains(name); n++)
        {
            name = $"{stem} ({n}){ext}";
        }

        used.Add(name);
        return name;
    }

    /// <summary>Removes characters Windows forbids in file names, control characters and trailing dots/spaces.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsControl(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                sb.Append(c == ':' ? '.' : ' ');
            }
            else
            {
                sb.Append(c);
            }
        }

        // Collapse runs of whitespace and trim characters Windows strips silently.
        string collapsed = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Trim().TrimEnd('.', ' ');
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
        {
            return s;
        }

        // Do not split a surrogate pair.
        int cut = char.IsLowSurrogate(s[max]) ? max - 1 : max;
        return s[..cut].TrimEnd('.', ' ');
    }
}
