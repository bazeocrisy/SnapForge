using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapForge.App.Interop;
using SnapForge.App.Themes;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Ocr;

/// <summary>
/// Shows text recognised in an image (offline, Windows OCR) for review, lets the user pick another installed
/// recognition language, edit the text and copy it. Recognised text is never stored or sent anywhere by SnapForge:
/// it exists only in this window until the user copies it.
/// </summary>
#pragma warning disable CA1001 // The token source is cancelled and disposed when the window closes (Closed handler).
internal sealed class TextRecognitionWindow : Window
#pragma warning restore CA1001
{
    private readonly PixelBuffer _image;
    private readonly TextBox _text;
    private readonly ComboBox _language;
    private readonly TextBlock _status;
    private readonly Button _copy;
    private CancellationTokenSource? _cts;
    private bool _ready;

    private TextRecognitionWindow(PixelBuffer image, string title)
    {
        _image = image;
        Title = $"Text from \"{title}\" — SnapForge";
        Width = 640;
        Height = 520;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont");

        _language = new ComboBox { MinWidth = 220, Margin = new Thickness(8, 0, 0, 0), DisplayMemberPath = nameof(OcrLanguage.DisplayName) };
        AutomationProperties.SetName(_language, "Recognition language");
        _language.SelectionChanged += (_, _) =>
        {
            if (_ready && _language.SelectedItem is OcrLanguage)
            {
                _ = RunAsync();
            }
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var label = new TextBlock { Text = "Language:", VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(label);
        top.Children.Add(_language);

        _text = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 14,
        };
        AutomationProperties.SetName(_text, "Recognised text (you can edit it before copying)");

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _copy = new Button { Content = "_Copy text", IsDefault = true, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _copy.Click += (_, _) => CopyText();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_copy);
        buttons.Children.Add(close);

        var note = new TextBlock
        {
            Text = "Recognised on this PC with Windows' built-in text recognition. Nothing is uploaded or saved. Check the text: recognition is not always exact.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            FontSize = 12,
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        DockPanel.SetDock(note, Dock.Bottom);
        root.Children.Add(note);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        root.Children.Add(_text);
        Content = root;

        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        Loaded += async (_, _) =>
        {
            _status.Text = "Recognising text…";
            IReadOnlyList<OcrLanguage> languages = await Task.Run(OcrService.AvailableLanguages).ConfigureAwait(true);
            if (languages.Count == 0)
            {
                _status.Text = "No text-recognition language is installed on this PC. In Windows Settings → Time & language → " +
                               "Language & region, open your language's options and add \"Optical character recognition\".";
                _language.IsEnabled = false;
                return;
            }

            _language.ItemsSource = languages;
            _language.SelectedIndex = 0;
            _ready = true;
            await RunAsync().ConfigureAwait(true);
        };
        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        };
    }

    /// <summary>Opens the window for <paramref name="image"/> (already flattened: redactions burned in).</summary>
    public static void Show(Window? owner, PixelBuffer image, string title)
    {
        var w = new TextRecognitionWindow(image, title) { Owner = owner is { IsVisible: true } ? owner : null };
        if (w.Owner is null)
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        w.Show();
    }

    private async Task RunAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        string? tag = (_language.SelectedItem as OcrLanguage)?.Tag;
        _status.Text = "Recognising text…";
        _copy.IsEnabled = false;
        OcrOutcome outcome;
        try
        {
            outcome = await Task.Run(() => OcrService.Recognize(_image, tag, token), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        _text.Text = outcome.Text;
        _copy.IsEnabled = outcome.Success && outcome.Text.Length > 0;
        _status.Text = !outcome.Success
            ? outcome.Error
            : outcome.Lines.Count == 0
                ? "No text was found in this image."
                : $"{outcome.Lines.Count} line{(outcome.Lines.Count == 1 ? "" : "s")} recognised ({outcome.LanguageTag}).";
    }

    private void CopyText()
    {
        try
        {
            Clipboard.SetText(_text.Text);
            _status.Text = "Text copied to the clipboard.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _status.Text = $"The text could not be copied: {ex.Message}";
        }
    }
}
