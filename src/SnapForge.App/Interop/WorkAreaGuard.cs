using System.Windows;
using Microsoft.Win32;
using SnapForge.Core.Capture;
using SnapForge.Core.Geometry;

namespace SnapForge.App.Interop;

/// <summary>
/// FA-003 / SF-007: every SnapForge window with a normal title bar is kept inside the usable area (work area) of its
/// monitor — when it is first shown, when it is restored from minimized/maximized, and whenever the display layout,
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
            if (sender is Window w && Applies(w))
            {
                Fit(w);
                w.StateChanged -= OnStateChanged;
                w.StateChanged += OnStateChanged;
            }
        }));
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged; // taskbar moved/resized changes the work area
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

        // WPF enforces MinWidth/MinHeight (in DIPs); lower them when the screen is smaller, so the frame can fit.
        double scale = monitor.DpiScale > 0 ? monitor.DpiScale : 1.0;
        double maxW = Math.Floor(fitted.Width / scale), maxH = Math.Floor(fitted.Height / scale);
        if (w.MinWidth > maxW)
        {
            w.MinWidth = maxW;
        }

        if (w.MinHeight > maxH)
        {
            w.MinHeight = maxH;
        }

        PhysicalPlacement.SetBounds(w, fitted, resize: fitted.Width != bounds.Width || fitted.Height != bounds.Height);
    }

    private static void OnStateChanged(object? sender, EventArgs e)
    {
        if (sender is Window { WindowState: WindowState.Normal } w)
        {
            w.Dispatcher.BeginInvoke(() => Fit(w));
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
