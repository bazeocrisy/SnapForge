using Microsoft.Win32;

namespace SnapForge.App.Services;

/// <summary>
/// "Launch with Windows" for the current user only: a value under HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// pointing at this SnapForge.exe with --background (start in the notification area). No administrator rights.
/// A portable copy that is moved afterwards leaves a stale entry; turning the option off and on again fixes it.
/// </summary>
internal static class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string BackgroundArgument = "--background";

    /// <summary>Registry value name (tests use their own name so they never touch the real entry).</summary>
    internal static string ValueName { get; set; } = "SnapForge";

    public static string CommandFor(string exePath) => $"\"{exePath}\" {BackgroundArgument}";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>True if the entry exists and points at <paramref name="exePath"/>.</summary>
    public static bool PointsAt(string exePath)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && string.Equals(value, CommandFor(exePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled, string exePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Path of the running executable (the single-file SnapForge.exe).</summary>
    public static string CurrentExecutable() => Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unknown.");
}
