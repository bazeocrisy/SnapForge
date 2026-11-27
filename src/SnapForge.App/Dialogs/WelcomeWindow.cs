using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SnapForge.App.Interop;
using SnapForge.App.Themes;
using SnapForge.Core;

namespace SnapForge.App.Dialogs;

/// <summary>
/// First-launch welcome and offline "Getting started" help (Help → Getting started). Explains how to capture, the
/// desktop widget, where captures are saved, recording, redaction and where to find help. Needs no account and no
/// internet connection; every instruction is built in.
/// </summary>
internal sealed class WelcomeWindow : Window
{
    private WelcomeWindow(string libraryRoot, string captureHotkey, string recordHotkey, Action? captureNow)
    {
        Title = $"Welcome to {ProductInfo.Name}";
        Width = 640;
        Height = 640;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)FindResource("UiFont");

        var doc = new StackPanel { Margin = new Thickness(28, 20, 28, 12) };
        var heading = new TextBlock { Text = $"Welcome to {ProductInfo.Name}", FontSize = 24, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        doc.Children.Add(heading);
        doc.Children.Add(new TextBlock { Text = "Capture. Create. Share. — free, open source, and everything stays on this PC.", Margin = new Thickness(0, 4, 0, 12), Opacity = 0.85, TextWrapping = TextWrapping.Wrap });

        Section(doc, "Take a screenshot",
            $"Press {captureHotkey} anywhere, or click Capture in the capture panel or the QuickDock. Drag to select an area (a magnifier shows exact pixels; arrow keys nudge by one pixel), click a window, or click a display. Esc cancels.");
        Section(doc, "The QuickDock",
            "A small dark pill with the SnapForge mark at the top centre of your screen opens Capture, Record, Library, Editor, Settings and presets without switching windows. Drag it to the top, left or right edge of any display. Lost it? View → Show QuickDock (also in the notification-area menu).");
        Section(doc, "Where captures are saved",
            $"Every capture is saved automatically to your local Capture Library:\n{libraryRoot}\nNothing is uploaded. Turn off \"Save to Library\" in the capture panel to capture without keeping a copy. Change the location in Settings → Capture Library. Back up your Library regularly (Settings → Capture Library → Back up).");
        Section(doc, "Record your screen",
            $"Click Record (or press {recordHotkey}) to open SnapForge Studio: record the screen, the screen with your camera, or the camera only. Camera and microphone are off until you turn them on. Press the hotkey again to pause/resume; Shift+F10 stops and saves an MP4. Windows shows a border around what is recorded and its own camera/microphone indicators.");
        Section(doc, "Redaction",
            "The Redact tool replaces pixels with solid black. Images you export, copy or drag out contain only the black box. Your editable Library copy keeps the original pixels so you can undo — use File → Sanitize to remove them for good. Blur and pixelate are not redaction. Videos cannot be redacted.");
        Section(doc, "Closing windows and exiting",
            "Closing a window keeps SnapForge running in the notification area (next to the clock) so hotkeys keep working. To quit completely use File → Exit SnapForge or right-click the notification-area icon → Exit SnapForge.");
        Section(doc, "Help",
            "Help → Keyboard shortcuts (F1), Help → Getting started (this page), and Help → Where are my captures? are available offline. The user guide is also on the SnapForge website.");

        var scroll = new ScrollViewer { Content = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(28, 8, 28, 20) };
        if (captureNow is not null)
        {
            var capture = new Button { Content = "_Take a capture now", MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
            capture.Click += (_, _) =>
            {
                _capturing = true;
                Close();
                captureNow();
            };
            buttons.Children.Add(capture);
        }

        var close = new Button { Content = "_Get started", IsDefault = true, IsCancel = true, MinWidth = 110 };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(scroll);
        Content = root;
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitleBar(this, ThemeManager.IsDark);
        Loaded += (_, _) => close.Focus();
    }

    private bool _capturing;

    private static void Section(Panel parent, string title, string body)
    {
        var h = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) };
        AutomationProperties.SetHeadingLevel(h, AutomationHeadingLevel.Level2);
        parent.Children.Add(h);
        parent.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
    }

    /// <summary>Shows the welcome / getting-started window (non-modal).</summary>
    public static void ShowFor(Window? owner, string libraryRoot, string captureHotkey, string recordHotkey, Action? captureNow, Action? afterClose = null)
    {
        var w = new WelcomeWindow(libraryRoot, captureHotkey, recordHotkey, captureNow);
        if (afterClose is not null)
        {
            // First launch: the welcome is the only window; when it closes (other than "Take a capture now")
            // the primary window opens, so there are never two windows competing at start-up.
            w.Closed += (_, _) =>
            {
                if (!w._capturing)
                {
                    afterClose();
                }
            };
        }

        if (owner is { IsVisible: true })
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        w.Show();
        w.Activate();
    }
}
