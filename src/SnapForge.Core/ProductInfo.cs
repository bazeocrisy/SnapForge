using System.Text.RegularExpressions;

namespace SnapForge.Core;

/// <summary>
/// Single source of truth for user-visible product identity, so a rename only touches this file,
/// the project metadata and the assets.
/// </summary>
public static partial class ProductInfo
{
    public const string Name = "SnapForge";
    public const string FilePrefix = "SnapForge";

    /// <summary>Environment variable that selects an isolated data profile (same rules as <c>--profile</c>).</summary>
    public const string ProfileVariable = "SNAPFORGE_PROFILE";

    /// <summary>
    /// The isolated data profile in use, or null for the normal profile. A profile has its own settings, Library,
    /// logs, temporary files and single-instance key, so test builds and CI never touch the user's real data.
    /// </summary>
    public static string? Profile { get; private set; }

    /// <summary>
    /// Root of all per-user mutable data: <c>%LOCALAPPDATA%\SnapForge</c>, or
    /// <c>%LOCALAPPDATA%\SnapForge-Profiles\&lt;profile&gt;</c> for an isolated profile.
    /// </summary>
    public static string DataRoot
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Profile is null ? Path.Combine(local, Name) : Path.Combine(local, Name + "-Profiles", Profile);
        }
    }

    /// <summary>Selects an isolated profile (call once at start-up, before any data is opened). Null = normal.</summary>
    /// <exception cref="ArgumentException">The name is not 1–32 letters, digits, '-' or '_'.</exception>
    public static void UseProfile(string? name)
    {
        if (name is not null && !IsValidProfileName(name))
        {
            throw new ArgumentException("A profile name must be 1–32 letters, digits, '-' or '_'.", nameof(name));
        }

        Profile = string.IsNullOrEmpty(name) ? null : name;
    }

    public static bool IsValidProfileName(string name) => ProfileName().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileName();
}
