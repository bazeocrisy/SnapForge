using System.Windows;
using System.Windows.Controls;
using SnapForge.App.Dialogs;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.Core.Settings;
using CaptureMode = SnapForge.Core.Capture.CaptureMode;

namespace SnapForge.App.Views;

internal sealed record PresetViewModel(CapturePreset Preset)
{
    public string Name => Preset.Name;

    public string Summary => string.Join(" · ", new[]
    {
        Preset.Options.Mode switch { CaptureMode.Window => "Window", CaptureMode.SingleScreen => "Display", CaptureMode.AllScreens => "Full screen", _ => "Region" },
        Preset.Options.DelaySeconds > 0 ? $"{Preset.Options.DelaySeconds} s delay" : null,
        Preset.Options.CaptureCursor ? "cursor" : null,
        Preset.Options.SaveToLibrary ? "Library" : "no history",
        Preset.Options.CopyToClipboard ? "clipboard" : null,
        Preset.Options.PreviewInEditor ? "Editor" : null,
        Preset.Hotkey is { } h ? h.ToString() : null,
    }.Where(s => s is not null));
}

/// <summary>The compact capture controller: big Capture button, selection mode, options, delay and presets.</summary>
public partial class CaptureWindow : Window
{
    private readonly AppServices _services;
    private bool _loading;

    internal CaptureWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        ThemeManager.ThemeChanged += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        services.SettingsChanged += (_, _) => Dispatcher.BeginInvoke(LoadFromSettings);
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        CaptureOptions o = _services.Settings.Capture;
        foreach (RadioButton rb in ModePanel.Children.OfType<RadioButton>())
        {
            rb.IsChecked = (string)rb.Tag == o.Mode.ToString();
        }

        PreviewBox.IsChecked = o.PreviewInEditor;
        CopyBox.IsChecked = o.CopyToClipboard;
        SaveBox.IsChecked = o.SaveToLibrary;
        CursorBox.IsChecked = o.CaptureCursor;
        foreach (RadioButton rb in DelayPanel.Children.OfType<RadioButton>())
        {
            rb.IsChecked = (string)rb.Tag == o.DelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!DelayPanel.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
        {
            ((RadioButton)DelayPanel.Children[0]).IsChecked = true;
        }

        SaveHint.Text = o.SaveToLibrary
            ? "Kept in your local Capture Library automatically."
            : "Capture without history: nothing is written to disk unless you export it.";
        HotkeyRegistration? reg = App.HotkeyResults.FirstOrDefault(r => r.Action == "Capture");
        HotkeyText.Text = _services.Settings.CaptureHotkey is not { } hk
            ? "No global hotkey (set one in Settings)"
            : reg is { Succeeded: false } ? $"Hotkey {hk} is not available: {reg.Error}" : $"Global hotkey: {hk}";
        PresetList.ItemsSource = _services.Settings.Presets.Select(p => new PresetViewModel(p)).ToList();
        _loading = false;
    }

    private CaptureOptions CurrentOptions() => _services.Settings.Capture;

    private void Update(Func<CaptureOptions, CaptureOptions> change) =>
        _services.UpdateSettings(s => s with { Capture = change(s.Capture).Normalized() });

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (!_loading && sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out CaptureMode mode))
        {
            Update(o => o with { Mode = mode });
        }
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Update(o => o with
        {
            PreviewInEditor = PreviewBox.IsChecked == true,
            CopyToClipboard = CopyBox.IsChecked == true,
            SaveToLibrary = SaveBox.IsChecked == true,
            CaptureCursor = CursorBox.IsChecked == true,
        });
    }

    private void OnDelayChecked(object sender, RoutedEventArgs e)
    {
        if (!_loading && sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out int seconds))
        {
            Update(o => o with { DelaySeconds = seconds });
        }
    }

    private void OnCapture(object sender, RoutedEventArgs e) => _ = App.CaptureWithOptionsAsync(CurrentOptions());

    private static CapturePreset? PresetFrom(object sender) =>
        ((sender as FrameworkElement)?.DataContext as PresetViewModel)?.Preset;

    private void OnRunPreset(object sender, RoutedEventArgs e)
    {
        if (PresetFrom(sender) is { } p)
        {
            _ = App.CaptureWithOptionsAsync(p.Options);
        }
    }

    private void OnApplyPreset(object sender, RoutedEventArgs e)
    {
        if (PresetFrom(sender) is { } p)
        {
            Update(_ => p.Options);
        }
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        string? name = InputDialog.Ask(this, "Save preset", "Preset name:", "My preset");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var preset = new CapturePreset { Name = name.Trim(), Options = CurrentOptions() };
            _services.UpdateSettings(s => s with { Presets = [.. s.Presets, preset] });
        }
    }

    private void OnRenamePreset(object sender, RoutedEventArgs e)
    {
        if (PresetFrom(sender) is not { } p)
        {
            return;
        }

        string? name = InputDialog.Ask(this, "Rename preset", "Preset name:", p.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _services.UpdateSettings(s => s with { Presets = [.. s.Presets.Select(x => x.Id == p.Id ? x with { Name = name.Trim() } : x)] });
        }
    }

    private void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        if (PresetFrom(sender) is { } p &&
            MessageBox.Show(this, $"Delete the preset \"{p.Name}\"?", "SnapForge", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _services.UpdateSettings(s => s with { Presets = [.. s.Presets.Where(x => x.Id != p.Id)] });
        }
    }

    private void OnPresetHotkey(object sender, RoutedEventArgs e)
    {
        if (PresetFrom(sender) is not { } p)
        {
            return;
        }

        string? text = InputDialog.Ask(this, "Preset hotkey", $"Hotkey for \"{p.Name}\" (e.g. Ctrl+Shift+F9; leave empty for none):", p.Hotkey?.ToString() ?? string.Empty);
        if (text is null)
        {
            return;
        }

        try
        {
            HotkeyGesture? gesture = HotkeyGesture.Parse(text);
            if (gesture is { IsValid: false })
            {
                MessageBox.Show(this, gesture.Problem, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _services.UpdateSettings(s => s with { Presets = [.. s.Presets.Select(x => x.Id == p.Id ? x with { Hotkey = gesture } : x)] });
            HotkeyRegistration? result = App.HotkeyResults.FirstOrDefault(r => r.Action == $"Preset: {p.Name}");
            if (result is { Succeeded: false })
            {
                MessageBox.Show(this, $"{gesture} could not be registered: {result.Error}", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOpenEditor(object sender, RoutedEventArgs e)
    {
        App.MainShell?.BringToFront();
        App.MainShell?.ShowEditorWorkspace();
    }

    private void OnOpenLibrary(object sender, RoutedEventArgs e)
    {
        App.MainShell?.BringToFront();
        App.MainShell?.ShowLibraryWorkspace();
    }

    private void OnSettings(object sender, RoutedEventArgs e) => App.ShowSettings(this);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing only hides the controller; it stays available from the notification area.
        base.OnClosing(e);
        if (Application.Current.MainWindow is { } main && main.IsLoaded && !e.Cancel && App.HasNotificationIcon)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
