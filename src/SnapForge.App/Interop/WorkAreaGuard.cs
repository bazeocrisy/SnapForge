using System.Windows;
using Microsoft.Win32;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Interop;

/// <summary>
/// FA-003 / SF-007: every SnapForge window with a normal title bar is kept inside the usable area (work area) of its
/// monitor — when it is first shown, when it grows, when it is restored from minimized/maximized, and whenever the display layout,
/// resolution, scaling or taskbar changes (including a monitor being unplugged). Works in physical pixels so mixed
/// DPI and Windows scaling are exact. Borderless overlays, toolbars and the QuickDock manage their own placement and
/// are not touched.
/// </summary>
internal static class WorkAreaGuard
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window w)
            {
                Attach(w);
            }
        }));

        // The Loaded class handler is not raised for every window in every hosting situation (CI run 90), so a cheap
        // scan of the application's windows also attaches to each newly shown window.
        var scan = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        scan.Tick += (_, _) =>
        {
            if (Application.Current is { } app && app.Dispatcher.CheckAccess())
            {
                foreach (Window w in app.Windows)
                {
                    if (w.IsVisible)
                    {
                        Attach(w);
                    }
                }
            }
        };
        scan.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged; // taskbar moved/resized changes the work area
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Attached = new();

    /// <summary>Fits <paramref name="w"/> once and keeps it fitted on restore (idempotent).</summary>
    internal static void Attach(Window w)
    {
        if (!Applies(w) || Attached.TryGetValue(w, out _))
        {
            return;
        }

        Attached.Add(w, new object());
        w.StateChanged += OnStateChanged;

        // QA-RC5-02: a window that grows after it was placed (the Record panel's "More options", a dialog whose content
        // gets longer) is fitted again, so its lower part and buttons never end up under the taskbar or off the screen.
        w.SizeChanged += (_, e) =>
        {
            if (e.HeightChanged && e.NewSize.Height > e.PreviousSize.Height || e.WidthChanged && e.NewSize.Width > e.PreviousSize.Width)
            {
                w.Dispatcher.BeginInvoke(() => Fit(w), System.Windows.Threading.DispatcherPriority.Background);
            }
        };
        FitSoon(w);
    }

    /// <summary>Windows with a standard caption that the user moves and resizes.</summary>
    internal static bool Applies(Window w) => w.WindowStyle != WindowStyle.None && !w.AllowsTransparency;

    /// <summary>Moves/resizes <paramref name="w"/> into its monitor's work area if any part of it is outside.</summary>
    internal static void Fit(Window w)
    {
        if (w.WindowState != WindowState.Normal || PhysicalPlacement.GetBounds(w) is not { } bounds)
        {
            return;
        }

        ScreenLayout layout;
        try
        {
            layout = PhysicalPlacement.Layout();
        }
        catch (InvalidOperationException)
        {
            return; // no monitors reported (e.g. during a display change); try again on the next event
        }

        MonitorInfo monitor = PhysicalPlacement.MonitorFor(layout, bounds);
        PixelRect fitted = WindowFit.Fit(bounds, monitor.WorkArea);
        if (fitted == bounds)
        {
            return;
        }

        // Drive WPF's own properties (in DIPs of the window's DPI) so WPF does not re-apply the old size or position
        // later; MinWidth/MinHeight are lowered when the screen is smaller than them.
        double scale = System.Windows.Media.VisualTreeHelper.GetDpi(w).DpiScaleX;
        if (scale <= 0)
        {
            scale = 1.0;
        }

        double width = fitted.Width / scale, height = fitted.Height / scale;
        if (w.MinWidth > width)
        {
            w.MinWidth = Math.Floor(width);
        }

        if (w.MinHeight > height)
        {
            w.MinHeight = Math.Floor(height);
        }

        if (fitted.Width != bounds.Width)
        {
            w.Width = width;
        }

        if (fitted.Height != bounds.Height)
        {
            w.Height = height;
        }

        w.Left = fitted.X / scale;
        w.Top = fitted.Y / scale;

        // Mixed-DPI setups can leave WPF's DIP conversion off by a few pixels: finish in physical pixels.
        if (PhysicalPlacement.GetBounds(w) is { } now && now != fitted)
        {
            PhysicalPlacement.SetBounds(w, fitted, resize: now.Width != fitted.Width || now.Height != fitted.Height);
        }
    }

    /// <summary>Fits now and again once WPF has finished its own placement (restore animations, size-to-content).</summary>
    private static void FitSoon(Window w)
    {
        w.Dispatcher.BeginInvoke(() => Fit(w), System.Windows.Threading.DispatcherPriority.Loaded);
        w.Dispatcher.BeginInvoke(() => Fit(w), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static void OnStateChanged(object? sender, EventArgs e)
    {
        if (sender is Window { WindowState: WindowState.Normal } w)
        {
            FitSoon(w);
        }
    }

    private static void OnDisplayChanged(object? sender, EventArgs e) => FitAllSoon();

    private static void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
        {
            FitAllSoon();
        }
    }

    private static void FitAllSoon() =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w.IsVisible && Applies(w))
                {
                    Fit(w);
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
}
