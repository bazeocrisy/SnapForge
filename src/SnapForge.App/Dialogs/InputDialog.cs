using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapForge.App.Interop;
using SnapForge.App.Themes;

namespace SnapForge.App.Dialogs;

/// <summary>Small themed prompt for a single line of text (rename, tags, folder, preset name).</summary>
internal sealed class InputDialog : Window
{
    private readonly TextBox _box;

    private InputDialog(string title, string prompt, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont");

        _box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 16) };
        AutomationProperties_SetName(_box, prompt);
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_box);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
    }

    public static string? Ask(Window? owner, string title, string prompt, string initial = "")
    {
        var dialog = new InputDialog(title, prompt, initial) { Owner = owner is { IsVisible: true } ? owner : null };
        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog._box.Text : null;
    }

    private static void AutomationProperties_SetName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);
}

/// <summary>Countdown shown during a capture delay (Esc cancels). Not included in the capture: it closes first.</summary>
internal sealed class CountdownWindow : Window
{
    private readonly TextBlock _number;
    private readonly CancellationTokenSource _cts;

    private CountdownWindow(CancellationTokenSource cts)
    {
        _cts = cts;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        Width = 150;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _number = new TextBlock { FontSize = 64, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        var hint = new TextBlock { Text = "Esc to cancel", Foreground = System.Windows.Media.Brushes.White, Opacity = 0.75, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(_number);
        stack.Children.Add(hint);
        Content = new Border
        {
            CornerRadius = new CornerRadius(75),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x1E, 0x1F, 0x22)),
            Child = stack,
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _cts.Cancel();
            }
        };
    }

    /// <summary>Counts down; returns false if the user cancelled.</summary>
    public static async Task<bool> RunAsync(int seconds)
    {
        using var cts = new CancellationTokenSource();
        var w = new CountdownWindow(cts);
        w.Show();
        w.Activate();
        try
        {
            for (int i = seconds; i > 0; i--)
            {
                w._number.Text = i.ToString(System.Globalization.CultureInfo.CurrentCulture);
                await Task.Delay(1000, w._cts.Token).ConfigureAwait(true);
            }

            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        finally
        {
            w.Close();
        }
    }
}
