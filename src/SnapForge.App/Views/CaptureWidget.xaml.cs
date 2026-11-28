using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.App.Themes;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Settings;

namespace SnapForge.App.Views;

/// <summary>
/// Entry points QuickDock and the capture panel share with the rest of the app, kept behind small indirections so
/// they can be re-pointed without touching the two surfaces.
/// </summary>
internal static class DockActions
{
    /// <summary>
    /// QuickDock "Record" and the capture panel's Video "Open Studio…" action. Defaults to starting a recording
    /// directly; the integrator points it at <c>Video.RecordingController.OpenStudio()</c> once that exists.
    /// </summary>
    internal static Action OpenStudio { get; set; } = Video.RecordingController.OpenStudio;

    /// <summary>
    /// "Open QuickDock with keyboard" (FA-004): turns QuickDock on if needed and opens it in keyboard mode. Esc
    /// returns focus to <paramref name="previousForeground"/>, or, when 0, to the window that was in the foreground.
    /// </summary>
    internal static void OpenQuickDockWithKeyboard(nint previousForeground = 0)
    {
        if (AppServices.Current is not { } services)
        {
            return;
        }

        if (App.Widget is null)
        {
            services.UpdateSettings(s => s with { Widget = s.Widget with { Enabled = true } });
            App.ApplyWidget(services);
        }

        App.Widget?.EnterKeyboardMode(previousForeground);
    }
}

/// <summary>
/// QuickDock, the persistent desktop dock (class name kept from the milestone 3 "capture widget"). Docked to the
/// top/left/right edge of a monitor (default: top centre of the primary), collapsed to a small pill until hovered
/// or clicked, never activated by the mouse (it does not steal focus from the app being captured), optionally always
/// on top, remembers its monitor/edge/offset, and is hidden during captures along with SnapForge's other windows.
/// Keyboard mode (<see cref="EnterKeyboardMode"/>) makes it activatable with focusable buttons; Esc restores the
/// window that was in the foreground before.
/// </summary>
public partial class CaptureWidget : Window
{
    public static readonly DependencyProperty TabCornerProperty = DependencyProperty.Register(nameof(TabCorner), typeof(CornerRadius), typeof(CaptureWidget), new PropertyMetadata(new CornerRadius(0, 0, 11, 11)));
    public static readonly DependencyProperty TabIconVisibilityProperty = DependencyProperty.Register(nameof(TabIconVisibility), typeof(Visibility), typeof(CaptureWidget), new PropertyMetadata(Visibility.Visible));
    public static readonly DependencyProperty PanelCornerProperty = DependencyProperty.Register(nameof(PanelCorner), typeof(CornerRadius), typeof(CaptureWidget), new PropertyMetadata(new CornerRadius(0, 0, 12, 12)));
    public static readonly DependencyProperty TabOrientationProperty = DependencyProperty.Register(nameof(TabOrientation), typeof(Orientation), typeof(CaptureWidget), new PropertyMetadata(Orientation.Horizontal));
    public static readonly DependencyProperty ChevronGeometryProperty = DependencyProperty.Register(nameof(ChevronGeometry), typeof(Geometry), typeof(CaptureWidget), new PropertyMetadata(null));
    public static readonly DependencyProperty TabBorderThicknessProperty = DependencyProperty.Register(nameof(TabBorderThickness), typeof(Thickness), typeof(CaptureWidget), new PropertyMetadata(new Thickness(1, 0, 1, 1)));

    /// <summary>Collapsed pill size in DIPs (along the edge × away from it): small, so it never covers a title bar.</summary>
    internal const double TabAlong = 40;
    internal const double TabAcross = 22;
    internal const double AutoHideAcross = 4;
    internal static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(700);

    private readonly AppServices _services;
    private readonly DispatcherTimer _expandTimer;
    private readonly DispatcherTimer _collapseTimer;
    private bool _dragging;
    private bool _menuOpen;
    private bool _keyboardMode;
    private nint _previousForeground;

    internal CaptureWidget(AppServices services)
    {
        _services = services;
        InitializeComponent();
        ApplyDockColours();
        _expandTimer = new DispatcherTimer { Interval = HoverDelay };
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            if (IsMouseOver)
            {
                Expand();
            }
        };
        _collapseTimer = new DispatcherTimer { Interval = CollapseDelay };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (CanAutoCollapse)
            {
                Collapse();
            }
        };

        SourceInitialized += (_, _) => MakeNonActivating();
        MouseEnter += OnMouseEnterWidget;
        MouseLeave += (_, _) => _collapseTimer.Start();
        PreviewKeyDown += OnDockKeyDown;
        Deactivated += (_, _) =>
        {
            // Keyboard mode ends when the user switches to another window (Alt+Tab, a click elsewhere).
            if (_keyboardMode && !_menuOpen)
            {
                ExitKeyboardMode(restoreForeground: false);
            }
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            ThemeManager.ThemeChanged -= OnThemeChanged;
        };
        ApplySettings();
    }

    public CornerRadius TabCorner { get => (CornerRadius)GetValue(TabCornerProperty); set => SetValue(TabCornerProperty, value); }

    public CornerRadius PanelCorner { get => (CornerRadius)GetValue(PanelCornerProperty); set => SetValue(PanelCornerProperty, value); }

    public Visibility TabIconVisibility { get => (Visibility)GetValue(TabIconVisibilityProperty); set => SetValue(TabIconVisibilityProperty, value); }

    /// <summary>Mark and chevron side by side at the top edge, stacked at the left/right edges.</summary>
    public Orientation TabOrientation { get => (Orientation)GetValue(TabOrientationProperty); set => SetValue(TabOrientationProperty, value); }

    /// <summary>The chevron points away from the docked edge (down at the top, right at the left edge, left at the right edge).</summary>
    public Geometry? ChevronGeometry { get => (Geometry?)GetValue(ChevronGeometryProperty); set => SetValue(ChevronGeometryProperty, value); }

    /// <summary>No border on the side that touches the screen edge.</summary>
    public Thickness TabBorderThickness { get => (Thickness)GetValue(TabBorderThicknessProperty); set => SetValue(TabBorderThicknessProperty, value); }

    /// <summary>
    /// Test hook: called with the button's automation name before a dock action runs; returning false skips the
    /// action (the dock still collapses and leaves keyboard mode as it would).
    /// </summary>
    internal Func<string, bool>? ActionFilter { get; set; }

    /// <summary>The dock buttons in visual order (keyboard mode focuses and cycles through these).</summary>
    internal IReadOnlyList<Button> DockButtons => [CaptureButton, RecordButton, TextButton, LibraryButton, EditorButton, SettingsButton, PresetsButton];

    /// <summary>True while QuickDock is open for keyboard use (activatable, buttons focusable).</summary>
    internal bool IsKeyboardMode => _keyboardMode;

    /// <summary>The window keyboard mode will return focus to.</summary>
    internal nint PreviousForeground => _previousForeground;

    private bool CanAutoCollapse => !IsMouseOver && !_dragging && !_menuOpen && !_keyboardMode;

    /// <summary>
    /// Opens the dock for a few seconds so the user can see where it is (first start, or "Show QuickDock"),
    /// then collapses it unless the pointer is on it.
    /// </summary>
    internal void Peek(TimeSpan duration)
    {
        Expand();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (CanAutoCollapse)
            {
                Collapse();
            }
        };
        timer.Start();
    }

    internal bool IsExpanded => Panel.Visibility == Visibility.Visible;

    internal DockEdge Edge => _services.Settings.Widget.Placement?.Edge ?? DockEdge.Top;

    /// <summary>Applies the current dock settings (topmost, edge orientation, size, position).</summary>
    internal void ApplySettings()
    {
        WidgetSettings s = _services.Settings.Widget;
        Topmost = s.Topmost;
        TabIconVisibility = s.AutoHide ? Visibility.Collapsed : Visibility.Visible;
        bool vertical = Edge != DockEdge.Top;
        PanelStack.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        TabOrientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        GripHandle.Width = vertical ? 36 : 16;
        GripHandle.Height = vertical ? 16 : 36;
        ((System.Windows.Shapes.Path)GripHandle.Child).LayoutTransform = vertical ? new RotateTransform(90) : Transform.Identity;
        string chevron;
        (TabCorner, PanelCorner, chevron, TabBorderThickness) = Edge switch
        {
            DockEdge.Left => (new CornerRadius(0, 11, 11, 0), new CornerRadius(0, 12, 12, 0), "IconChevronRight", new Thickness(0, 1, 1, 1)),
            DockEdge.Right => (new CornerRadius(11, 0, 0, 11), new CornerRadius(12, 0, 0, 12), "IconChevronLeft", new Thickness(1, 1, 0, 1)),
            _ => (new CornerRadius(0, 0, 11, 11), new CornerRadius(0, 0, 12, 12), "IconChevronDown", new Thickness(1, 0, 1, 1)),
        };
        ChevronGeometry = (Geometry)FindResource(chevron);
        TabButton.ToolTip = s.ExpandMode == WidgetExpandMode.Hover
            ? "SnapForge QuickDock — point here to open it"
            : "SnapForge QuickDock — click to open it";
        Reposition();
    }

    /// <summary>Opens the dock (Capture / Record / Text / Library / Editor / Settings / Presets).</summary>
    internal void Expand()
    {
        _expandTimer.Stop();
        if (IsExpanded)
        {
            return;
        }

        TabButton.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
        Reposition();
    }

    /// <summary>Back to the small pill.</summary>
    internal void Collapse()
    {
        _expandTimer.Stop();
        _collapseTimer.Stop();
        Panel.Visibility = Visibility.Collapsed;
        TabButton.Visibility = Visibility.Visible;
        Reposition();
    }

    /// <summary>Sizes and places the window for the current state on its monitor (physical pixels).</summary>
    internal void Reposition()
    {
        ScreenLayout layout;
        try
        {
            layout = PhysicalPlacement.Layout();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }

        WidgetSettings s = _services.Settings.Widget;
        MonitorInfo monitor = WidgetDock.ResolveMonitor(layout, s.Placement);
        double scale = monitor.DpiScale <= 0 ? 1 : monitor.DpiScale;
        (double along, double across) = DipSize(s);
        bool vertical = Edge != DockEdge.Top;
        Width = vertical ? across : along;
        Height = vertical ? along : across;
        PixelRect rect = WidgetDock.Place(layout, s.Placement, (int)Math.Ceiling(along * scale), (int)Math.Ceiling(across * scale));
        if (new WindowInteropHelper(this).Handle != 0)
        {
            PhysicalPlacement.SetBounds(this, rect, resize: true);
        }
    }

    private (double Along, double Across) DipSize(WidgetSettings s)
    {
        if (!IsExpanded)
        {
            return (TabAlong, s.AutoHide ? AutoHideAcross : TabAcross);
        }

        Panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size desired = Panel.DesiredSize;
        return Edge == DockEdge.Top ? (Math.Ceiling(desired.Width), Math.Ceiling(desired.Height)) : (Math.Ceiling(desired.Height), Math.Ceiling(desired.Width));
    }

    // ------------------------------------------------------------------ colours

    /// <summary>
    /// Dock colours as window resources: SnapForge charcoal-navy with the orange aperture mark in the light and dark
    /// themes (the same brand colours as the app icon), the Windows high-contrast colours in high contrast. The
    /// Capture button uses the theme accent (AccentBrush), so it follows every palette as well.
    /// </summary>
    internal void ApplyDockColours()
    {
        static SolidColorBrush B(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        bool hc = ThemeManager.IsHighContrast;
        Resources["DockSurfaceBrush"] = hc ? B(SystemColors.WindowColor) : B(Color.FromRgb(0x0F, 0x17, 0x2A));
        Resources["DockBorderBrush"] = hc ? B(SystemColors.WindowTextColor) : B(Color.FromRgb(0x2B, 0x36, 0x4C));
        Resources["DockHoverBrush"] = hc ? B(SystemColors.ControlColor) : B(Color.FromRgb(0x1E, 0x29, 0x3F));
        Resources["DockTextBrush"] = hc ? B(SystemColors.WindowTextColor) : B(Color.FromRgb(0xE8, 0xEC, 0xF3));
        Resources["DockMutedBrush"] = hc ? B(SystemColors.WindowTextColor) : B(Color.FromRgb(0x9A, 0xA5, 0xB8));
        Resources["DockMarkBrush"] = hc ? B(SystemColors.HighlightColor) : B(Color.FromRgb(0xF9, 0x73, 0x16));
        Resources["DockFocusBrush"] = hc ? B(SystemColors.HighlightColor) : B(Color.FromRgb(0xFF, 0xFF, 0xFF));
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyDockColours();

    // ------------------------------------------------------------------ non-activating window

    private void MakeNonActivating()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(RefuseMouseActivation);
        SetNoActivate(true);
        Reposition();
    }

    private void SetNoActivate(bool on)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }

        long ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE) | NativeMethods.WS_EX_TOOLWINDOW;
        ex = on ? ex | NativeMethods.WS_EX_NOACTIVATE : ex & ~NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);
    }

    /// <summary>WM_MOUSEACTIVATE → MA_NOACTIVATE: clicks work but never activate the dock (except in keyboard mode).</summary>
    private nint RefuseMouseActivation(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;
        if (msg == WM_MOUSEACTIVATE && !_keyboardMode)
        {
            handled = true;
            return MA_NOACTIVATE;
        }

        return 0;
    }

    // ------------------------------------------------------------------ keyboard mode (FA-004)

    /// <summary>
    /// Opens QuickDock for keyboard use: remembers the foreground window, makes the dock activatable, expands it,
    /// makes its buttons focusable and focuses Capture. Tab / arrow keys move, Enter (or Space) runs the focused
    /// button, Esc closes the dock and returns focus to the remembered window.
    /// </summary>
    internal void EnterKeyboardMode(nint previousForeground = 0)
    {
        nint own = new WindowInteropHelper(this).Handle;
        nint previous = previousForeground != 0 ? previousForeground : NativeMethods.GetForegroundWindow();
        if (previous != own && previous != 0)
        {
            _previousForeground = previous;
        }

        _keyboardMode = true;
        foreach (Button b in DockButtons)
        {
            b.Focusable = true;
        }

        SetNoActivate(false);
        Expand();
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        if (own != 0 && NativeMethods.GetForegroundWindow() != own)
        {
            _ = SetForegroundWindow(own);
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_keyboardMode)
            {
                CaptureButton.Focus();
                Keyboard.Focus(CaptureButton);
            }
        }, DispatcherPriority.Input);
    }

    /// <summary>Leaves keyboard mode: non-activating again, buttons unfocusable, collapsed; optionally refocuses the previous window.</summary>
    internal void ExitKeyboardMode(bool restoreForeground)
    {
        if (!_keyboardMode)
        {
            return;
        }

        _keyboardMode = false;
        foreach (Button b in DockButtons)
        {
            b.Focusable = false;
        }

        Keyboard.ClearFocus();
        SetNoActivate(true);
        Collapse();
        nint previous = _previousForeground;
        _previousForeground = 0;
        if (restoreForeground && previous != 0 && IsWindow(previous))
        {
            _ = SetForegroundWindow(previous);
        }
    }

    private void OnDockKeyDown(object sender, KeyEventArgs e)
    {
        if (!_keyboardMode || _menuOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                ExitKeyboardMode(restoreForeground: true);
                break;
            case Key.Enter or Key.Space when Keyboard.FocusedElement is Button { IsEnabled: true } button:
                e.Handled = true;
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                break;
            case Key.Home:
                e.Handled = true;
                Keyboard.Focus(DockButtons[0]);
                break;
            case Key.End:
                e.Handled = true;
                Keyboard.Focus(DockButtons[^1]);
                break;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hWnd);

    // ------------------------------------------------------------------ actions

    /// <summary>
    /// Runs a dock action: leaves keyboard mode first (returning focus to the app the user was in, so e.g. Active
    /// window captures that app) or collapses the dock, then runs <paramref name="action"/>.
    /// </summary>
    private void Run(Button source, Action action)
    {
        if (_keyboardMode)
        {
            ExitKeyboardMode(restoreForeground: true);
        }
        else
        {
            Collapse();
        }

        if (ActionFilter is { } filter && !filter(AutomationProperties.GetName(source)))
        {
            return;
        }

        action();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Reposition);

    private void OnMouseEnterWidget(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (!IsExpanded && _services.Settings.Widget.ExpandMode == WidgetExpandMode.Hover)
        {
            _expandTimer.Start();
        }
    }

    private void OnTabClick(object sender, RoutedEventArgs e) => Expand();

    private void OnCapture(object sender, RoutedEventArgs e) =>
        Run(CaptureButton, () => _ = App.CaptureWithOptionsAsync(_services.Settings.Capture));

    private void OnRecord(object sender, RoutedEventArgs e) => Run(RecordButton, () =>
    {
        if (Video.RecordingController.IsActive)
        {
            Video.RecordingController.ToggleFromHotkey(); // Stop: stops and saves the running recording
        }
        else
        {
            DockActions.OpenStudio();
        }
    });

    /// <summary>The Record button becomes Stop while a recording runs.</summary>
    internal void UpdateRecordButton()
    {
        bool active = Video.RecordingController.IsActive;
        RecordButton.Content = active ? "Stop" : "Record";
        RecordButton.Tag = FindResource(active ? "IconStop" : "IconRecordDot");
        RecordButton.ToolTip = active ? "Stop and save the recording" : "Record the screen (MP4)";
        RecordButton.SetValue(AutomationProperties.NameProperty, active ? "Stop recording" : "Record");
        RecordButton.SetValue(AutomationProperties.HelpTextProperty, active ? "Stop and save the running recording" : "Open the recording studio to record the screen (MP4)");
    }

    /// <summary>Text: select a region and open offline text recognition on it (nothing is saved to the Library).</summary>
    private void OnText(object sender, RoutedEventArgs e) => Run(TextButton, () => _ = ExtractTextAsync(_services.Settings.Capture));

    private static async Task ExtractTextAsync(CaptureOptions current)
    {
        CaptureResult? result = await CaptureController.CaptureAsync(current with { Mode = Core.Capture.CaptureMode.Region, DelaySeconds = 0, CaptureCursor = false }).ConfigureAwait(true);
        if (result is not null)
        {
            Ocr.TextRecognitionWindow.Show(null, result.Image, "screen region");
        }
    }

    private void OnEditor(object sender, RoutedEventArgs e) => Run(EditorButton, () => App.OpenInEditor(new HandoffRequest(Handoff.ShowEditor)));

    private void OnLibrary(object sender, RoutedEventArgs e) => Run(LibraryButton, () => App.OpenInEditor(new HandoffRequest(Handoff.ShowLibrary)));

    private void OnSettings(object sender, RoutedEventArgs e) => Run(SettingsButton, () => App.ShowSettings(null));

    private void OnPresets(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = PresetsButton,
            Placement = Edge switch
            {
                DockEdge.Left => PlacementMode.Right,
                DockEdge.Right => PlacementMode.Left,
                _ => PlacementMode.Bottom,
            },
        };
        foreach (CapturePreset preset in _services.Settings.Presets)
        {
            CapturePreset p = preset;
            var item = new MenuItem { Header = p.Name, InputGestureText = p.Hotkey is { } h ? CaptureWindow.FriendlyGesture(h) : string.Empty };
            item.Click += (_, _) => Run(PresetsButton, () => _ = App.CaptureWithOptionsAsync(p.Options));
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        AddItem("Capture _panel…", App.ShowCaptureWindow);
        AddItem("_Hide QuickDock", () => _services.UpdateSettings(s => s with { Widget = s.Widget with { Enabled = false } }));
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_keyboardMode)
            {
                Keyboard.Focus(PresetsButton); // Esc in the menu returns to the dock, still in keyboard mode
            }
            else
            {
                _collapseTimer.Start();
            }
        };
        PresetsButton.ContextMenu = menu;
        menu.IsOpen = true;

        void AddItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => Run(PresetsButton, action);
            menu.Items.Add(item);
        }
    }

    // ------------------------------------------------------------------ dragging to a new dock position

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        GripHandle.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || PhysicalPlacement.GetBounds(this) is not { } b)
        {
            return;
        }

        (int x, int y) = PhysicalPlacement.Cursor();
        PhysicalPlacement.SetBounds(this, new PixelRect(x - (b.Width / 2), y - (b.Height / 2), b.Width, b.Height), resize: false);
    }

    private void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        GripHandle.ReleaseMouseCapture();
        (int x, int y) = PhysicalPlacement.Cursor();
        DockAt(x, y);
        e.Handled = true;
    }

    /// <summary>Docks to the edge nearest to a physical point and remembers it.</summary>
    internal void DockAt(int x, int y)
    {
        WidgetPlacement placement = WidgetDock.FromPoint(PhysicalPlacement.Layout(), x, y);
        _services.UpdateSettings(s => s with { Widget = s.Widget with { Placement = placement } });
        ApplySettings();
    }
}
