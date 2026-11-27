using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.Core.Library;
using SnapForge.Core.Settings;

namespace SnapForge.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private bool _loading;

    internal SettingsWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        Load();
    }

    private void Load()
    {
        _loading = true;
        AppSettings s = _services.Settings;
        ThemeSystem.IsChecked = s.Theme == ThemePreference.System;
        ThemeLight.IsChecked = s.Theme == ThemePreference.Light;
        ThemeDark.IsChecked = s.Theme == ThemePreference.Dark;
        CloseToTrayBox.IsChecked = s.CloseToNotificationArea;
        ShowCaptureWindowBox.IsChecked = s.ShowCaptureWindowOnStart;
        PanelTopmostBox.IsChecked = s.CapturePanelTopmost;
        PanelHotkeyBox.Text = s.ShowPanelHotkey?.ToString() ?? "(none)";
        CaptureHotkeyBox.Text = s.CaptureHotkey?.ToString() ?? "(none)";
        TrashDaysCheck.IsChecked = s.Retention.EmptyTrashAfterDays is not null;
        TrashDaysBox.Text = (s.Retention.EmptyTrashAfterDays ?? 30).ToString(CultureInfo.CurrentCulture);
        KeepDaysCheck.IsChecked = s.Retention.MoveToTrashAfterDays is not null;
        KeepDaysBox.Text = (s.Retention.MoveToTrashAfterDays ?? 365).ToString(CultureInfo.CurrentCulture);
        LocationBox.Text = _services.Library.Root;
        StorageUsage u = _services.Library.GetUsage();
        UsageText.Text = $"{u.CaptureCount} captures ({MainWindow.FormatBytes(u.TotalBytes - u.TrashBytes)}) · Trash: {u.TrashCount} ({MainWindow.FormatBytes(u.TrashBytes)})";
        int unsanitized = _services.Library.Records.Count(r => r.HasReversibleRedactions);
        PrivacyText.Text = (unsanitized == 0
            ? "No library capture keeps original pixels under redactions."
            : $"{unsanitized} library capture(s) still keep original pixels under their redactions (so the redactions can be undone). Exports are unaffected.")
            + "\nSnapForge has no network features: captures are never uploaded or synchronized by SnapForge.";
        UpdateHotkeyStatus();
        LoadGateBSettings(s);
        BuildRecordingSection();
        _loading = false;
    }

    private void LoadGateBSettings(AppSettings s)
    {
        WidgetEnabledBox.IsChecked = s.Widget.Enabled;
        WidgetTopmostBox.IsChecked = s.Widget.Topmost;
        WidgetAutoHideBox.IsChecked = s.Widget.AutoHide;
        WidgetHover.IsChecked = s.Widget.ExpandMode == WidgetExpandMode.Hover;
        WidgetClick.IsChecked = s.Widget.ExpandMode == WidgetExpandMode.Click;
        DragPng.IsChecked = s.DragDrop.Format == Core.Export.ImageFileFormat.Png;
        DragJpeg.IsChecked = s.DragDrop.Format == Core.Export.ImageFileFormat.Jpeg;
        JpegQualityBox.Text = s.DragDrop.JpegQuality.ToString(CultureInfo.CurrentCulture);
        JpegQualityBox.IsEnabled = s.DragDrop.Format == Core.Export.ImageFileFormat.Jpeg;
        bool registered = SafeStartupEnabled();
        StartupBox.IsChecked = registered;
        StartupStatus.Text = registered && !StartupPointsHere()
            ? "The startup entry points to a different copy of SnapForge.exe. Turn this off and on again to use this copy."
            : "Per-user setting (no administrator rights). Turn it off here or in Windows Settings → Apps → Startup.";
        if (Core.ProductInfo.Profile is not null)
        {
            StartupBox.IsEnabled = false;
            StartupStatus.Text = "Not available in an isolated test profile (it would start a test copy with Windows).";
        }
    }

    private static bool SafeStartupEnabled()
    {
        try
        {
            return StartupRegistration.IsEnabled();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool StartupPointsHere()
    {
        try
        {
            return StartupRegistration.PointsAt(StartupRegistration.CurrentExecutable());
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        bool enabled = StartupBox.IsChecked == true;
        try
        {
            StartupRegistration.Set(enabled, StartupRegistration.CurrentExecutable());
            _services.UpdateSettings(s => s with { LaunchAtStartup = enabled });
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            MessageBox.Show(this, $"The startup setting could not be changed:\n\n{ex.Message}", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _loading = true;
        LoadGateBSettings(_services.Settings);
        _loading = false;
    }

    private void OnWidgetChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.UpdateSettings(s => s with
        {
            Widget = s.Widget with
            {
                Enabled = WidgetEnabledBox.IsChecked == true,
                Topmost = WidgetTopmostBox.IsChecked == true,
                AutoHide = WidgetAutoHideBox.IsChecked == true,
                ExpandMode = WidgetClick.IsChecked == true ? WidgetExpandMode.Click : WidgetExpandMode.Hover,
            },
        });
    }

    private void OnWidgetReset(object sender, RoutedEventArgs e) =>
        _services.UpdateSettings(s => s with { Widget = s.Widget with { Placement = null, Enabled = true } });

    private void OnDragFormatChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        int quality = int.TryParse(JpegQualityBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int q) ? q : _services.Settings.DragDrop.JpegQuality;
        var format = DragJpeg.IsChecked == true ? Core.Export.ImageFileFormat.Jpeg : Core.Export.ImageFileFormat.Png;
        _services.UpdateSettings(s => s with { DragDrop = new DragDropSettings { Format = format, JpegQuality = quality }.Normalized() });
        _loading = true;
        LoadGateBSettings(_services.Settings);
        _loading = false;
    }

    private void UpdateHotkeyStatus()
    {
        var lines = new List<string>();
        foreach (HotkeyRegistration r in App.HotkeyResults)
        {
            lines.Add(r.Succeeded ? $"✓ {r.Gesture} — {r.Action}" : $"✗ {r.Gesture} — {r.Action}: {r.Error}");
        }

        foreach (IReadOnlyList<string> conflict in HotkeyGesture.FindConflicts(_services.Settings.HotkeyAssignments()))
        {
            lines.Add($"✗ Same hotkey used for: {string.Join(", ", conflict)}");
        }

        HotkeyStatus.Text = lines.Count == 0 ? "No global hotkeys are registered." : string.Join("\n", lines);
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!_loading && sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out ThemePreference theme))
        {
            _services.UpdateSettings(s => s with { Theme = theme });
            ThemeManager.Apply(theme);
        }
    }

    private void OnBehaviourChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _services.UpdateSettings(s => s with { CloseToNotificationArea = CloseToTrayBox.IsChecked == true, ShowCaptureWindowOnStart = ShowCaptureWindowBox.IsChecked == true, CapturePanelTopmost = PanelTopmostBox.IsChecked == true });
        }
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            OnClearHotkey(sender, e);
            return;
        }

        HotkeyModifiers mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Windows;
        var gesture = new HotkeyGesture(mods, key == Key.Snapshot ? "PrintScreen" : key.ToString());
        if (!gesture.IsValid)
        {
            HotkeyStatus.Text = $"{gesture}: {gesture.Problem}";
            return;
        }

        if (IsPanelHotkey(sender))
        {
            _services.UpdateSettings(s => s with { ShowPanelHotkey = gesture });
            PanelHotkeyBox.Text = gesture.ToString();
        }
        else
        {
            _services.UpdateSettings(s => s with { CaptureHotkey = gesture });
            CaptureHotkeyBox.Text = gesture.ToString();
        }

        UpdateHotkeyStatus();
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if (IsPanelHotkey(sender))
        {
            _services.UpdateSettings(s => s with { ShowPanelHotkey = null });
            PanelHotkeyBox.Text = "(none)";
        }
        else
        {
            _services.UpdateSettings(s => s with { CaptureHotkey = null });
            CaptureHotkeyBox.Text = "(none)";
        }

        UpdateHotkeyStatus();
    }

    private static bool IsPanelHotkey(object sender) => sender is FrameworkElement { Tag: "Panel" };

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_services.Library.Root}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, ex.Message, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnChangeLocation(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose an empty folder for the Capture Library" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string target = dialog.FolderName;
        if (MessageBox.Show(this,
                $"Move all captures to:\n{target}\n\nThe captures are copied and verified, then the old copies are overwritten and removed. Avoid cloud-synced folders (OneDrive, Dropbox) unless you want your captures uploaded by that service.",
                "Move Capture Library", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            IsEnabled = false;
            if (App.MainShell is { } main)
            {
                await main.FlushAllAsync().ConfigureAwait(true);
            }

            await _services.MoveLibraryAsync(target).ConfigureAwait(true);
            MessageBox.Show(this, $"The Capture Library is now in:\n{_services.Library.Root}", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            MessageBox.Show(this, $"The library was not moved:\n\n{ex.Message}", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
            Load();
        }
    }

    private void OnRetentionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        int? Parse(System.Windows.Controls.TextBox box, bool enabled) =>
            enabled && int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int d) && d >= 1 ? d : enabled ? 30 : null;

        var policy = new RetentionPolicy
        {
            EmptyTrashAfterDays = Parse(TrashDaysBox, TrashDaysCheck.IsChecked == true),
            MoveToTrashAfterDays = Parse(KeepDaysBox, KeepDaysCheck.IsChecked == true),
        };
        _services.UpdateSettings(s => s with { Retention = policy });
    }

    private async void OnApplyRetention(object sender, RoutedEventArgs e)
    {
        (int trashed, int deleted) = await _services.Writer.RunAsync(() => _services.Library.ApplyRetention(_services.Settings.Retention, DateTime.UtcNow)).ConfigureAwait(true);
        _services.Tray.Prune(id => _services.Library.Find(id) is { IsDeleted: false });
        MessageBox.Show(this, $"Moved {trashed} capture(s) to the Trash and permanently deleted {deleted}.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Information);
        Load();
    }

    private async void OnEmptyTrash(object sender, RoutedEventArgs e)
    {
        int count = _services.Library.Records.Count(r => r.IsDeleted);
        if (count == 0 || MessageBox.Show(this, $"Permanently delete {count} capture(s) in the Trash?", "SnapForge", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        App.MainShell?.OnCapturesRemoved([.. _services.Library.Records.Where(r => r.IsDeleted).Select(r => r.Id)]);
        await _services.Writer.RunAsync(() => _services.Library.EmptyTrash()).ConfigureAwait(true);
        Load();
    }

    private async void OnBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Back up Capture Library", Filter = "ZIP archive (*.zip)|*.zip", FileName = $"SnapForge Library {DateTime.Now:yyyy-MM-dd}.zip" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (App.MainShell is { } main)
            {
                await main.FlushAllAsync().ConfigureAwait(true);
            }

            int n = await _services.Writer.RunAsync(() => _services.Library.Backup(dialog.FileName)).ConfigureAwait(true);
            MessageBox.Show(this, $"Backed up {n} capture(s) to:\n{dialog.FileName}", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The backup failed:\n\n{ex.Message}", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Restore from backup", Filter = "ZIP archive (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            int n = await _services.Writer.RunAsync(() => _services.Library.RestoreBackup(dialog.FileName)).ConfigureAwait(true);
            MessageBox.Show(this, n == 0 ? "Nothing to restore: every capture in the backup is already in the Library." : $"Restored {n} capture(s).", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The backup could not be restored:\n\n{ex.Message}", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Load();
    }

    private async void OnSanitizeAll(object sender, RoutedEventArgs e)
    {
        Guid[] ids = [.. _services.Library.Records.Where(r => r.HasReversibleRedactions && r.Damage is null).Select(r => r.Id)];
        if (ids.Length == 0)
        {
            MessageBox.Show(this, "No capture needs sanitizing.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Permanently burn in the redactions of {ids.Length} capture(s)? Original pixels and hidden annotations are removed from the Library copies. This cannot be undone.",
                "Sanitize", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes || App.MainShell is not { } main)
        {
            return;
        }

        await main.SanitizeRecordsAsync(ids).ConfigureAwait(true);
        Load();
    }

    private void OnPrivacyNotice(object sender, RoutedEventArgs e) => App.ShowPrivacyNotice(this);

    private void OnDeleteDragCopies(object sender, RoutedEventArgs e)
    {
        long before = CaptureDragDrop.Store.TotalBytes();
        int removed = CaptureDragDrop.Store.DeleteAll(DateTime.UtcNow);
        long after = CaptureDragDrop.Store.TotalBytes();
        MessageBox.Show(this, $"Removed {removed} drag-and-drop folder(s), {(before - after) / 1024} KB." +
            (after > 0 ? $"\n{after / 1024} KB are still in use by another app and will be removed later." : string.Empty) +
            "\n\nFiles are deleted normally (not overwritten); apps you dropped them into keep their own copies.",
            "Drag-and-drop copies", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnResetPreferences(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reset every SnapForge preference to its default?\n\nYour captures, recordings and the Library location are kept.",
                "Reset all preferences", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        string? library = _services.Settings.LibraryPath;
        _services.UpdateSettings(_ => new AppSettings { LibraryPath = library, PrivacyNoticeShown = true });
        App.RegisterHotkeys(notifyOnFailure: true);
        Load();
        MessageBox.Show(this, "Preferences were reset to their defaults.", "Reset all preferences", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
