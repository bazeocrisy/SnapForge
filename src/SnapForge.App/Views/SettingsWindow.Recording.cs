using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapForge.App.Video.Interop;
using SnapForge.Core.Settings;

namespace SnapForge.App.Views;

/// <summary>Settings → Recording (Milestone V1): microphone, hotkeys, after-recording and disk-space options.</summary>
public partial class SettingsWindow
{
    private void BuildRecordingSection()
    {
        VideoSettings v = _services.Settings.Video;
        RecordingSection.Children.Clear();

        // Microphone: "Windows default" follows the default device; listing devices does not open them.
        var mic = new ComboBox { Margin = new Thickness(0, 2, 0, 6), MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(mic, "Microphone device");
        mic.Items.Add(new ComboBoxItem { Content = "Windows default microphone", Tag = null });
        IReadOnlyList<CoreAudio.Endpoint> endpoints;
        try
        {
            endpoints = CoreAudio.ListEndpoints(CoreAudio.eCapture);
        }
        catch (NativeCallException)
        {
            endpoints = [];
        }

        foreach (CoreAudio.Endpoint e in endpoints)
        {
            mic.Items.Add(new ComboBoxItem { Content = e.Name, Tag = e.Id });
        }

        if (v.MicrophoneDeviceId is { } chosen && endpoints.All(e => e.Id != chosen))
        {
            mic.Items.Add(new ComboBoxItem { Content = "Previously chosen microphone (not connected)", Tag = chosen });
        }

        mic.SelectedItem = mic.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == v.MicrophoneDeviceId) ?? mic.Items[0];
        mic.SelectionChanged += (_, _) =>
        {
            if (mic.SelectedItem is ComboBoxItem item)
            {
                _services.UpdateSettings(s => s with { Video = s.Video with { MicrophoneDeviceId = item.Tag as string } });
            }
        };
        RecordingSection.Children.Add(new TextBlock { Text = "Microphone (used when Microphone is on):", FontSize = 12 });
        RecordingSection.Children.Add(mic);

        AddHotkeyRow("Record / pause / resume:", v.RecordHotkey, g => _services.UpdateSettings(s => s with { Video = s.Video with { RecordHotkey = g } }));
        AddHotkeyRow("Stop recording:", v.StopHotkey, g => _services.UpdateSettings(s => s with { Video = s.Video with { StopHotkey = g } }));

        var keep = new CheckBox { Content = "Save recordings to the Library", IsChecked = v.SaveToLibrary, Margin = new Thickness(0, 6, 0, 2), ToolTip = "Off = recordings are saved only where you choose when you stop (no history is kept)." };
        keep.Click += (_, _) => _services.UpdateSettings(s => s with { Video = s.Video with { SaveToLibrary = keep.IsChecked == true } });
        RecordingSection.Children.Add(keep);
        AddHotkeyRow("Cancel recording:", v.CancelHotkey, g => _services.UpdateSettings(s => s with { Video = s.Video with { CancelHotkey = g } }));

        var open = new CheckBox { Content = "Open the recording when it is saved", IsChecked = v.OpenAfterRecording, Margin = new Thickness(0, 6, 0, 2) };
        open.Click += (_, _) => _services.UpdateSettings(s => s with { Video = s.Video with { OpenAfterRecording = open.IsChecked == true } });
        RecordingSection.Children.Add(open);

        var space = new DockPanel { Margin = new Thickness(0, 4, 0, 2) };
        var minBox = new TextBox { Width = 70, Text = v.MinimumFreeSpaceMegabytes.ToString(CultureInfo.CurrentCulture) };
        System.Windows.Automation.AutomationProperties.SetName(minBox, "Stop recording below this free space, in megabytes");
        minBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(minBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int mb))
            {
                _services.UpdateSettings(s => s with { Video = (s.Video with { MinimumFreeSpaceMegabytes = mb }).Normalized() });
            }

            minBox.Text = _services.Settings.Video.MinimumFreeSpaceMegabytes.ToString(CultureInfo.CurrentCulture);
        };
        space.Children.Add(new TextBlock { Text = "Stop and save when free disk space falls below (MB): ", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        space.Children.Add(minBox);
        RecordingSection.Children.Add(space);

        RecordingSection.Children.Add(new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Text = "Recordings are MP4 files (H.264 video, AAC sound) kept in your Library on this PC; SnapForge never uploads them. " +
                   "Videos cannot be redacted or sanitized — anything visible or audible while recording is in the file. " +
                   "Windows shows its own capture border while recording, and its microphone indicator when the microphone is on.",
        });
    }

    private void AddHotkeyRow(string label, HotkeyGesture? current, Action<HotkeyGesture?> save)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var box = new TextBox { Width = 170, IsReadOnly = true, Text = current?.ToString() ?? "(none)", ToolTip = "Click, then press the keys. Backspace clears." };
        System.Windows.Automation.AutomationProperties.SetName(box, label.TrimEnd(':'));
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.Tab)
            {
                e.Handled = key != Key.Tab;
                return;
            }

            if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
            {
                save(null);
                box.Text = "(none)";
                UpdateHotkeyStatus();
                return;
            }

            HotkeyModifiers mods = HotkeyModifiers.None;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                mods |= HotkeyModifiers.Control;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                mods |= HotkeyModifiers.Alt;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                mods |= HotkeyModifiers.Shift;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
            {
                mods |= HotkeyModifiers.Windows;
            }

            var gesture = new HotkeyGesture(mods, key == Key.Snapshot ? "PrintScreen" : key.ToString());
            if (!gesture.IsValid)
            {
                HotkeyStatus.Text = $"{gesture}: {gesture.Problem}";
                return;
            }

            save(gesture);
            box.Text = gesture.ToString();
            UpdateHotkeyStatus();
        };
        row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        row.Children.Add(box);
        RecordingSection.Children.Add(row);
    }
}
