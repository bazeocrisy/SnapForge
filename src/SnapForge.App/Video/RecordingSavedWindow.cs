using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.Core;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Video;

/// <summary>
/// RC3: the compact confirmation after Stop — "Recording saved", how long and how big it is, and three actions:
/// Play, Open in Editor and Show in folder. If the recording could not be finished, it says so in plain English and
/// still offers Show in folder for any file that was kept for recovery. It appears above the notification area, does
/// not take focus from the app you were recording, stays until closed (or 45 s without the pointer over it) and is
/// excluded from screen capture.
/// </summary>
internal sealed class RecordingSavedWindow : Window
{
    private static readonly TimeSpan AutoClose = TimeSpan.FromSeconds(45);
    private static RecordingSavedWindow? s_current;
    private readonly DispatcherTimer _close;

    /// <param name="id">Library id of the saved recording, or null if it was saved outside the Library (or not saved).</param>
    /// <param name="file">The saved MP4 (or the file kept for recovery), if there is one.</param>
    /// <param name="ok">False when the recording could not be finished normally.</param>
    public RecordingSavedWindow(string title, string details, Guid? id, string? file, bool ok, bool excludeFromCapture = true)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Title = title;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        SetResourceReference(ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(this, title);
        Id = id;
        File = file;

        var heading = new TextBlock { Text = (ok ? "✓  " : "!  ") + title, FontSize = 15, FontWeight = FontWeights.SemiBold };
        heading.Foreground = ok ? new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)) : new SolidColorBrush(Color.FromRgb(0xD9, 0x82, 0x1E));
        var text = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, FontSize = 12, Margin = new Thickness(0, 4, 0, 10) };
        AutomationProperties.SetName(text, "Details");
        var close = new Button { Content = "✕", Padding = new Thickness(6, 0, 6, 0), Focusable = false, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Close" };
        close.SetResourceReference(StyleProperty, "ToolbarButton");
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => Close();
        var top = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(heading);

        var actions = new WrapPanel();
        bool canPlay = ok && (id is not null || (file is not null && System.IO.File.Exists(file)));
        if (canPlay)
        {
            Button play = Action("_Play", "Play the recording", accent: true, () =>
            {
                if (id is { } video)
                {
                    App.OpenInEditor(new HandoffRequest(Handoff.PlayVideo, video));
                }
                else if (file is not null)
                {
                    OpenWithWindows(file);
                }
            });
            actions.Children.Add(play);
        }

        if (ok && id is { } editable)
        {
            actions.Children.Add(Action("Open in _Editor", "Open the recording in SnapForge Editor (trim, export, GIF)", accent: false,
                () => App.OpenInEditor(new HandoffRequest(Handoff.EditVideo, editable))));
        }

        if (file is not null && System.IO.File.Exists(file))
        {
            actions.Children.Add(Action("Show in _folder", "Show the MP4 file in File Explorer", accent: false, () => ShowInFolder(file)));
        }

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(text);
        stack.Children.Add(actions);
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 10, 12), Child = stack, MinWidth = 300 };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = border;

        _close = new DispatcherTimer(AutoClose, DispatcherPriority.Normal, (_, _) =>
        {
            if (!IsMouseOver)
            {
                Close();
            }
        }, Dispatcher);
        SourceInitialized += (_, _) =>
        {
            CaptureExclusion.MakeNonActivating(this);
            Excluded = excludeFromCapture && CaptureExclusion.Apply(this);
        };
        Loaded += (_, _) =>
        {
            UpdateLayout();
            Core.Capture.ScreenLayout layout = PhysicalPlacement.Layout();
            Core.Capture.MonitorInfo primary = layout.Monitors.FirstOrDefault(m => m.IsPrimary) ?? layout.Monitors[0];
            if (PhysicalPlacement.GetBounds(this) is { } b)
            {
                PhysicalPlacement.SetBounds(this, WindowFit.BottomRight(primary.WorkArea, b.Width, b.Height, 16), resize: false);
            }

            _close.Start();
        };
        Closed += (_, _) =>
        {
            _close.Stop();
            if (ReferenceEquals(s_current, this))
            {
                s_current = null;
            }
        };
    }

    /// <summary>The Library id shown (tests).</summary>
    public Guid? Id { get; }

    /// <summary>The file offered by Show in folder (tests).</summary>
    public string? File { get; }

    /// <summary>True if Windows confirmed the window is excluded from screen capture.</summary>
    public bool Excluded { get; private set; }

    /// <summary>The confirmation on screen, if any (self-tests).</summary>
    internal static RecordingSavedWindow? Current => s_current;

    /// <summary>Shows the confirmation (replacing an earlier one).</summary>
    public static RecordingSavedWindow ShowNew(string title, string details, Guid? id, string? file, bool ok)
    {
        s_current?.Close();
        var window = new RecordingSavedWindow(title, details, id, file, ok);
        s_current = window;
        window.Show();
        return window;
    }

    /// <summary>Opens File Explorer with the file selected.</summary>
    internal static void ShowInFolder(string file)
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", file }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            MessageBox.Show("File Explorer could not be opened: " + ex.Message + "\n\nThe file is here:\n" + file, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenWithWindows(string file)
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            MessageBox.Show("The recording could not be opened: " + ex.Message, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Button Action(string text, string name, bool accent, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0), Focusable = false };
        b.SetResourceReference(StyleProperty, accent ? "AccentButton" : "ToolbarButton");
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) =>
        {
            click();
            Close();
        };
        return b;
    }
}
