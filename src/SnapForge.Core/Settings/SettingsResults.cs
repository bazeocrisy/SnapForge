namespace SnapForge.Core.Settings;

/// <summary>How <see cref="AppSettings.LoadWithStatus"/> obtained the settings (FA-019).</summary>
public enum SettingsLoadStatus
{
    /// <summary>The file was read normally.</summary>
    Loaded,

    /// <summary>No settings file yet: defaults.</summary>
    Missing,

    /// <summary>The file is damaged: defaults; a copy was kept (<see cref="SettingsLoadResult.DamagedCopyPath"/>).</summary>
    Damaged,

    /// <summary>The file was written by a newer SnapForge: defaults, read-only (the file must not be overwritten).</summary>
    NewerVersion,

    /// <summary>The file exists but could not be read (locked, no permission): defaults, read-only.</summary>
    Unreadable,
}

/// <summary>Result of <see cref="AppSettings.LoadWithStatus"/>.</summary>
/// <param name="Settings">Settings to use for this session.</param>
/// <param name="Status">How they were obtained.</param>
/// <param name="Message">A user-facing explanation when the status is not Loaded/Missing.</param>
/// <param name="DamagedCopyPath">Where the damaged file was copied (Damaged only; null if the copy failed).</param>
public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status, string? Message, string? DamagedCopyPath)
{
    /// <summary>True when the app must not write the settings file this session (newer or unreadable file).</summary>
    public bool IsReadOnly => Status is SettingsLoadStatus.NewerVersion or SettingsLoadStatus.Unreadable;
}

/// <summary>Result of <see cref="AppSettings.TrySave"/>.</summary>
/// <param name="Succeeded">True if the file was written.</param>
/// <param name="Error">A user-facing reason when it was not.</param>
/// <param name="Exception">The underlying exception, if any.</param>
/// <param name="IsReadOnly">True when saving was refused because the file belongs to a newer SnapForge.</param>
public sealed record SettingsSaveResult(bool Succeeded, string? Error, Exception? Exception, bool IsReadOnly = false);

/// <summary>Thrown by <see cref="AppSettings.Save"/> when the settings could not be written (FA-019).</summary>
public sealed class SettingsSaveException : IOException
{
    public SettingsSaveException()
    {
    }

    public SettingsSaveException(string message)
        : base(message)
    {
    }

    public SettingsSaveException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
