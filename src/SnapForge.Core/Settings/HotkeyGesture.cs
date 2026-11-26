using System.Text;

namespace SnapForge.Core.Settings;

/// <summary>Win32 MOD_* values, so they can be passed to RegisterHotKey unchanged.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// A global keyboard shortcut such as "Ctrl+Shift+PrintScreen". <see cref="Key"/> is a WPF <c>Key</c> name
/// (the app converts it to a virtual-key code). Core only validates and compares gestures; registration happens in
/// the app, where conflicts with other programs are detected by RegisterHotKey failing.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    private static readonly HashSet<string> StandaloneKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "PrintScreen", "Snapshot", "Pause", "Scroll",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
    };

    private static readonly HashSet<string> ModifierKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "LeftCtrl", "RightCtrl", "LeftShift", "RightShift", "LeftAlt", "RightAlt", "LWin", "RWin", "System", "None",
    };

    /// <summary>Canonical key name: "Snapshot" and "PrintScreen" are the same physical key.</summary>
    public string NormalizedKey => Key.Equals("Snapshot", StringComparison.OrdinalIgnoreCase) ? "PrintScreen" : Key;

    /// <summary>Why this gesture cannot be used, or null if it is acceptable.</summary>
    public string? Problem =>
        string.IsNullOrWhiteSpace(Key) || ModifierKeyNames.Contains(Key) ? "Press a key together with Ctrl, Alt, Shift or Win."
        : Modifiers == HotkeyModifiers.None && !StandaloneKeys.Contains(Key) ? "Use Ctrl, Alt, Shift or Win with this key (only Print Screen, Pause and function keys can be used alone)."
        : Modifiers == HotkeyModifiers.Shift && !StandaloneKeys.Contains(Key) ? "Shift alone would block typing that character; add Ctrl or Alt."
        : null;

    public bool IsValid => Problem is null;

    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        HotkeyModifiers mods = HotkeyModifiers.None;
        string? key = null;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": mods |= HotkeyModifiers.Control; break;
                case "ALT": mods |= HotkeyModifiers.Alt; break;
                case "SHIFT": mods |= HotkeyModifiers.Shift; break;
                case "WIN" or "WINDOWS": mods |= HotkeyModifiers.Windows; break;
                default:
                    if (key is not null)
                    {
                        throw new FormatException($"'{text}' has more than one non-modifier key.");
                    }

                    key = raw.Equals("PrtSc", StringComparison.OrdinalIgnoreCase) || raw.Equals("Print Screen", StringComparison.OrdinalIgnoreCase) ? "PrintScreen" : raw;
                    break;
            }
        }

        return key is null ? throw new FormatException($"'{text}' has no key.") : new HotkeyGesture(mods, key);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) sb.Append("Win+");
        return sb.Append(NormalizedKey).ToString();
    }

    public bool SameKeysAs(HotkeyGesture? other) =>
        other is not null && other.Modifiers == Modifiers && string.Equals(other.NormalizedKey, NormalizedKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>Groups of action names that are assigned the same gesture (conflicts inside SnapForge itself).</summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindConflicts(IEnumerable<(string Action, HotkeyGesture? Gesture)> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        return [.. assignments
            .Where(a => a.Gesture is not null)
            .GroupBy(a => a.Gesture!.ToString(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<string>)g.Select(a => a.Action).ToList())];
    }
}
