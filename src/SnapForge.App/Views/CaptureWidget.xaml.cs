using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.App.Services;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;
using SnapForge.Core.Settings;

namespace SnapForge.App.Views;

/// <summary>
/// The persistent desktop capture widget. Docked to the top/left/right edge of a monitor (default: top centre of
/// the primary), collapsed to a small tab until hovered or clicked, never activated (it does not steal focus from
/// the app being captured), optionally always on top, remembers its monitor/edge/offset, and is hidden during
/// captures along with SnapForge's other windows.
/// </summary>
public partial class CaptureWidget : Window
{
    public static readonly DependencyProperty TabCornerProperty = DependencyProperty.Register(nameof(TabCorner), typeof(CornerRadius), typeof(CaptureWidget), new PropertyMetadata(new CornerRadius(0, 0, 6, 6)));
    public static readonly DependencyProperty TabIconVisibilityProperty = DependencyProperty.Register(nameof(TabIconVisibility), typeof(Visibility), typeof(CaptureWidget), new PropertyMetadata(Visibility.Visible));
    public static readonly DependencyProperty PanelCornerProperty = DependencyProperty.Register(nameof(PanelCorner), typeof(CornerRadius), typeof(CaptureWidget), new PropertyMetadata(new CornerRadius(0, 0, 10, 10)));

    /// <summary>Collapsed tab size in DIPs (along the edge × away from it).</summary>
    internal const double TabAlong = 120;
    internal const double TabAcross = 20;
    internal const double AutoHideAcross = 4;
    internal static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(700);

    private readonly AppServices _services;
    private readonly DispatcherTimer _expandTimer;
    private readonly DispatcherTimer _collapseTimer;
    private bool _dragging;
    private bool _menuOpen;

    internal CaptureWidget(AppServices services)
    {
        _services = services;
        InitializeComponent();
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
            if (!IsMouseOver && !_dragging && !_menuOpen)
            {
                Collapse();
            }
        };

        SourceInitialized += (_, _) => MakeNonActivating();
        MouseEnter += OnMouseEnterWidget;
        MouseLeave += (_, _) => _collapseTimer.Start();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        ApplySettings();
    }

    public CornerRadius TabCorner { get => (CornerRadius)GetValue(TabCornerProperty); set => SetValue(TabCornerProperty, value); }

    public CornerRadius PanelCorner { get => (CornerRadius)GetValue(PanelCornerProperty); set => SetValue(PanelCornerProperty, value); }

    public Visibility TabIconVisibility { get => (Visibility)GetValue(TabIconVisibilityProperty); set => SetValue(TabIconVisibilityProperty, value); }

    /// <summary>
    /// Opens the widget for a few seconds so the user can see where it is (first start, or "Show capture widget"),
    /// then collapses it unless the pointer is on it.
    /// </summary>
    internal void Peek(TimeSpan duration)
    {
        Expand();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsMouseOver && !_dragging && !_menuOpen)
            {
                Collapse();
            }
        };
        timer.Start();
    }

    internal bool IsExpanded => Panel.Visibility == Visibility.Visible;

    internal DockEdge Edge => _services.Settings.Widget.Placement?.Edge ?? DockEdge.Top;

    /// <summary>Applies the current widget settings (topmost, edge orientation, size, position).</summary>
    internal void ApplySettings()
    {
        WidgetSettings s = _services.Settings.Widget;
        Topmost = s.Topmost;
        TabIconVisibility = s.AutoHide ? Visibility.Collapsed : Visibility.Visible;
        bool vertical = Edge != DockEdge.Top;
        PanelStack.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        CaptureLabel.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        (TabCorner, PanelCorner) = Edge switch
        {
            DockEdge.Left => (new CornerRadius(0, 6, 6, 0), new CornerRadius(0, 10, 10, 0)),
            DockEdge.Right => (new CornerRadius(6, 0, 0, 6), new CornerRadius(10, 0, 0, 10)),
            _ => (new CornerRadius(0, 0, 6, 6), new CornerRadius(0, 0, 10, 10)),
        };
        TabButton.ToolTip = s.ExpandMode == WidgetExpandMode.Hover
            ? "SnapForge — point here to open the capture widget"
            : "SnapForge — click to open the capture widget";
        Reposition();
    }

    /// <summary>Opens the widget (Capture / Editor / Library / Presets).</summary>
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

    /// <summary>Back to the small tab.</summary>
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

    private void MakeNonActivating()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(RefuseMouseActivation);
        nint ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)((long)ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
        Reposition();
    }

    /// <summary>WM_MOUSEACTIVATE → MA_NOACTIVATE: clicks work but never activate the widget.</summary>
    private static nint RefuseMouseActivation(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return MA_NOACTIVATE;
        }

        return 0;
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

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        Collapse();
        _ = App.CaptureWithOptionsAsync(_services.Settings.Capture);
    }

    private void OnEditor(object sender, RoutedEventArgs e)
    {
        Collapse();
        App.MainShell?.BringToFront();
        App.MainShell?.ShowEditorWorkspace();
    }

    private void OnLibrary(object sender, RoutedEventArgs e)
    {
        Collapse();
        App.MainShell?.BringToFront();
        App.MainShell?.ShowLibraryWorkspace();
    }

    private void OnPresets(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = PresetsButton,
            Placement = Edge switch
            {
                DockEdge.Left => System.Windows.Controls.Primitives.PlacementMode.Right,
                DockEdge.Right => System.Windows.Controls.Primitives.PlacementMode.Left,
                _ => System.Windows.Controls.Primitives.PlacementMode.Bottom,
            },
        };
        foreach (CapturePreset preset in _services.Settings.Presets)
        {
            CapturePreset p = preset;
            var item = new MenuItem { Header = p.Name, InputGestureText = p.Hotkey is { } h ? CaptureWindow.FriendlyGesture(h) : string.Empty };
            item.Click += (_, _) =>
            {
                Collapse();
                _ = App.CaptureWithOptionsAsync(p.Options);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        AddItem(menu, "Capture _panel…", App.ShowCaptureWindow);
        AddItem(menu, "Widget _settings…", () => App.ShowSettings(null));
        AddItem(menu, "_Hide widget", () => _services.UpdateSettings(s => s with { Widget = s.Widget with { Enabled = false } }));
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _collapseTimer.Start();
        };
        PresetsButton.ContextMenu = menu;
        menu.IsOpen = true;

        static void AddItem(ContextMenu m, string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            m.Items.Add(item);
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
