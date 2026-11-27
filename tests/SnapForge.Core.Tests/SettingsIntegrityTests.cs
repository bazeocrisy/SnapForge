using SnapForge.Core.Annotations;
using SnapForge.Core.Settings;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Tests;

/// <summary>FA-019: newer settings files are never overwritten, damaged ones are kept, and save failures are reported.</summary>
public class SettingsIntegrityTests
{
    private const string NewerFile = """{ "Version": 7, "Theme": "Neon", "SomethingNew": { "x": 1 } }""";

    [Fact]
    public void FA019_newer_settings_load_as_read_only_defaults_and_the_file_is_never_overwritten()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("settings.json");
        File.WriteAllText(path, NewerFile);

        SettingsLoadResult r = AppSettings.LoadWithStatus(path);
        Assert.Equal(SettingsLoadStatus.NewerVersion, r.Status);
        Assert.True(r.IsReadOnly);
        Assert.Contains("newer version", r.Message, StringComparison.Ordinal);
        Assert.Equal(new AppSettings().Theme, r.Settings.Theme);
        Assert.Equal(NewerFile, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir.Path)); // not renamed, no ".damaged" copy

        // Even a caller that ignores the read-only flag cannot overwrite the newer file.
        SettingsSaveResult save = (r.Settings with { Theme = ThemePreference.Dark }).TrySave(path);
        Assert.False(save.Succeeded);
        Assert.True(save.IsReadOnly);
        Assert.Throws<SettingsSaveException>(() => r.Settings.Save(path));
        Assert.Equal(NewerFile, File.ReadAllText(path));
        Assert.Equal(new AppSettings().Theme, AppSettings.Load(path).Theme);
    }

    [Fact]
    public void FA019_damaged_settings_are_copied_to_unique_names_and_earlier_copies_are_kept()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("settings.json");
        File.WriteAllText(path, "{ broken 1");
        SettingsLoadResult first = AppSettings.LoadWithStatus(path);
        Assert.Equal(SettingsLoadStatus.Damaged, first.Status);
        Assert.False(first.IsReadOnly);
        Assert.NotNull(first.DamagedCopyPath);
        Assert.StartsWith("settings.damaged-", Path.GetFileName(first.DamagedCopyPath), StringComparison.Ordinal);

        File.WriteAllText(path, "{ broken 2");
        SettingsLoadResult second = AppSettings.LoadWithStatus(path);
        Assert.NotEqual(first.DamagedCopyPath, second.DamagedCopyPath);
        Assert.Equal("{ broken 1", File.ReadAllText(first.DamagedCopyPath!));
        Assert.Equal("{ broken 2", File.ReadAllText(second.DamagedCopyPath!));

        // Saving defaults afterwards replaces the damaged original only; the copies stay.
        Assert.True(second.Settings.TrySave(path).Succeeded);
        Assert.Equal(SettingsLoadStatus.Loaded, AppSettings.LoadWithStatus(path).Status);
        Assert.Equal(2, Directory.GetFiles(dir.Path, "settings.damaged-*.json").Length);
    }

    [Fact]
    public void FA019_legacy_v1_settings_fixture_loads_unchanged()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("settings.json");
        File.Copy(Path.Combine(LegacyFixtures.FixtureRoot, "settings.json"), path);
        SettingsLoadResult r = AppSettings.LoadWithStatus(path);
        Assert.Equal(SettingsLoadStatus.Loaded, r.Status);
        Assert.False(r.IsReadOnly);
        Assert.Equal(@"D:\Shots", r.Settings.LibraryPath);
        Assert.Equal(ThemePreference.Dark, r.Settings.Theme);
        Assert.Equal("Ctrl+Shift+S", r.Settings.CaptureHotkey!.ToString());
        Assert.Equal(AnnotationTool.Highlight, r.Settings.LastTool);
        Assert.Equal(240, r.Settings.LibraryThumbnailSize);
        Assert.True(r.Settings.TrySave(path).Succeeded);
    }

    [Fact]
    public void FA019_disk_full_during_save_is_reported_and_keeps_the_previous_file()
    {
        using var dir = new TestDirectory();
        string path = dir.Sub("settings.json");
        new AppSettings { Theme = ThemePreference.Light }.Save(path);
        byte[] before = File.ReadAllBytes(path);
        AtomicFile.WriteDecorator.Value = s => new FailingStream(s, 16, () => new IOException("There is not enough space on the disk."));
        try
        {
            SettingsSaveResult r = new AppSettings { Theme = ThemePreference.Dark }.TrySave(path);
            Assert.False(r.Succeeded);
            Assert.Contains("not enough space", r.Error, StringComparison.Ordinal);
            Assert.IsType<IOException>(r.Exception);
            SettingsSaveException ex = Assert.Throws<SettingsSaveException>(() => new AppSettings { Theme = ThemePreference.Dark }.Save(path));
            Assert.IsType<IOException>(ex.InnerException);
        }
        finally
        {
            AtomicFile.WriteDecorator.Value = null;
        }

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void FA019_unwritable_location_is_reported()
    {
        using var dir = new TestDirectory();
        File.WriteAllText(dir.Sub("not-a-folder"), "x");
        string path = Path.Combine(dir.Sub("not-a-folder"), "settings.json"); // its "folder" is a file
        SettingsSaveResult r = new AppSettings().TrySave(path);
        Assert.False(r.Succeeded);
        Assert.NotNull(r.Error);
        Assert.Throws<SettingsSaveException>(() => new AppSettings().Save(path));
    }

    [Fact]
    public void FA019_read_only_directory_is_reported_where_the_platform_enforces_it()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // directory read-only attributes are not enforced for files on Windows; covered by the test above
        }

        using var dir = new TestDirectory();
        string ro = dir.Sub("ro");
        Directory.CreateDirectory(ro);
        File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            string probe = Path.Combine(ro, "probe");
            try
            {
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return; // running as root: permissions are not enforced, nothing to observe here
            }
            catch (UnauthorizedAccessException)
            {
            }

            SettingsSaveResult r = new AppSettings().TrySave(Path.Combine(ro, "settings.json"));
            Assert.False(r.Succeeded);
            Assert.IsType<UnauthorizedAccessException>(r.Exception);
        }
        finally
        {
            File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
