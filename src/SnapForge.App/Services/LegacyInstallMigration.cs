using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SnapForge.App.Services;

/// <summary>
/// FA-002 / FA-010: migration from the per-user installation used up to 1.0.0-rc.1
/// (<c>%LOCALAPPDATA%\Programs\SnapForge</c>, registered under HKCU) to the system-wide Program Files installation.
/// Runs inside SnapForge as the signed-in user — never from the elevated installer — so it only ever touches that
/// user's own old copy, never other profiles. It removes the old PROGRAM files with the old copy's own uninstaller in
/// silent mode, which by design keeps captures, recordings and settings (the rc.1 uninstaller only deletes data after
/// two interactive confirmations, never when silent). The "launch when I sign in" entry is re-pointed at the new copy.
/// </summary>
internal static class LegacyInstallMigration
{
    /// <summary>Inno Setup uninstall key of SnapForge (same AppId in all versions).</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6E0C3B1A-58A2-4E39-9F3B-0D6F4C1C7A51}_is1";

    public sealed record LegacyInstall(string Location, string Uninstaller, string? Version);

    /// <summary>
    /// The current user's old per-user installation, if any and only if it is safely inside
    /// <c>%LOCALAPPDATA%\Programs</c> with its uninstaller in that folder and it is not this running copy.
    /// </summary>
    public static LegacyInstall? Find(string? currentExe)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        if (key?.GetValue("InstallLocation") is not string location || location.Length == 0)
        {
            return null;
        }

        return Validate(location, key.GetValue("DisplayVersion") as string, currentExe,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
    }

    /// <summary>Path checks, separated for unit tests.</summary>
    internal static LegacyInstall? Validate(string location, string? version, string? currentExe, string perUserProgramsRoot)
    {
        string full;
        try
        {
            full = Path.GetFullPath(location).TrimEnd('\\');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        string root = Path.GetFullPath(perUserProgramsRoot).TrimEnd('\\') + "\\";
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.Contains("..", StringComparison.Ordinal))
        {
            return null; // only the per-user Programs folder of this user; never arbitrary paths from the registry
        }

        if (currentExe is not null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(currentExe))?.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))
        {
            return null; // this IS the old copy running
        }

        string uninstaller = Path.Combine(full, "unins000.exe");
        return File.Exists(uninstaller) ? new LegacyInstall(full, uninstaller, version) : null;
    }

    /// <summary>
    /// Removes the old program files silently (data kept) and re-points the start-up entry. Returns an error message,
    /// or null on success.
    /// </summary>
    public static string? Remove(LegacyInstall old, string currentExe)
    {
        bool startupPointedAtOld = StartupRegistration.IsEnabled() && !StartupRegistration.PointsAt(currentExe) && StartupPointsInto(old.Location);
        try
        {
            using var p = Process.Start(new ProcessStartInfo(old.Uninstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = false });
            if (p is null)
            {
                return "The old uninstaller could not be started.";
            }

            if (!p.WaitForExit(TimeSpan.FromMinutes(3)))
            {
                return "The old uninstaller did not finish within 3 minutes.";
            }

            if (p.ExitCode != 0)
            {
                return $"The old uninstaller reported error {p.ExitCode}.";
            }

            // Inno Setup's uninstaller hands over to a temporary copy of itself and exits at once; wait until the old
            // program files are really gone.
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (File.Exists(Path.Combine(old.Location, "SnapForge.exe")) || File.Exists(old.Uninstaller))
            {
                if (DateTime.UtcNow > deadline)
                {
                    return "The old copy was not removed within 3 minutes.";
                }

                Thread.Sleep(250);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return ex.Message;
        }

        if (startupPointedAtOld)
        {
            StartupRegistration.Set(true, currentExe);
        }

        return null;
    }

    private static bool StartupPointsInto(string folder)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath);
        return key?.GetValue(StartupRegistration.ValueName) is string value && value.Contains(folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Marker so the question is asked once per user (answering No is respected).</summary>
    public static string DeclinedMarker => Path.Combine(Core.ProductInfo.DataRoot, "legacy-install-migration-declined");
}
