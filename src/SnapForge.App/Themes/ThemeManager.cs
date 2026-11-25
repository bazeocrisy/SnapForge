using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.Core.Settings;

namespace SnapForge.App.Themes;

/// <summary>Switches between the light and dark palettes at run time (all styles use DynamicResource).</summary>
internal static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static event EventHandler? ThemeChanged;

    private static ThemePreference _preference = ThemePreference.System;
    private static bool _watchingContrast;

    /// <summary>True while a Windows high-contrast theme is active (it overrides SnapForge's light/dark choice).</summary>
    public static bool IsHighContrast { get; private set; }

    public static void Apply(ThemePreference preference)
    {
        _preference = preference;
        if (!_watchingContrast)
        {
            _watchingContrast = true;
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast))
                {
                    Application.Current?.Dispatcher.BeginInvoke(() => Apply(_preference));
                }
            };
        }

        IsHighContrast = SystemParameters.HighContrast;
        bool dark = IsHighContrast
            ? Luminance(SystemColors.WindowColor) < 0.5
            : preference switch
            {
                ThemePreference.Light => false,
                ThemePreference.Dark => true,
                _ => SystemUsesDarkTheme(),
            };
        IsDark = dark;
        if (Application.Current is not { } app)
        {
            return;
        }

        ResourceDictionary palette = IsHighContrast
            ? HighContrastPalette()
            : new ResourceDictionary { Source = new Uri($"pack://application:,,,/SnapForge;component/Themes/Palette.{(dark ? "Dark" : "Light")}.xaml") };
        if (app.Resources.MergedDictionaries.Count > 0)
        {
            app.Resources.MergedDictionaries[0] = palette;
        }
        else
        {
            app.Resources.MergedDictionaries.Add(palette);
        }

        foreach (Window w in app.Windows)
        {
            WindowChrome.UseDarkTitleBar(w, dark);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Palette built from the user's Windows high-contrast colours (Window, WindowText, Highlight, HighlightText,
    /// GrayText, ButtonFace), so every SnapForge surface follows the chosen contrast theme.
    /// </summary>
    internal static ResourceDictionary HighContrastPalette()
    {
        static SolidColorBrush B(System.Windows.Media.Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        return new ResourceDictionary
        {
            ["BackgroundBrush"] = B(SystemColors.WindowColor),
            ["SurfaceBrush"] = B(SystemColors.WindowColor),
            ["SurfaceRaisedBrush"] = B(SystemColors.ControlColor),
            ["HoverBrush"] = B(SystemColors.ControlColor),
            ["PressedBrush"] = B(SystemColors.ControlColor),
            ["BorderBrush"] = B(SystemColors.WindowTextColor),
            ["TextBrush"] = B(SystemColors.WindowTextColor),
            ["MutedTextBrush"] = B(SystemColors.WindowTextColor),
            ["AccentBrush"] = B(SystemColors.HighlightColor),
            ["AccentHoverBrush"] = B(SystemColors.HighlightColor),
            ["AccentSubtleBrush"] = B(SystemColors.ControlColor),
            ["AccentForegroundBrush"] = B(SystemColors.HighlightTextColor),
            ["CanvasBackdropBrush"] = B(SystemColors.WindowColor),
            ["WarningBrush"] = B(SystemColors.HighlightColor),
            ["WarningBackgroundBrush"] = B(SystemColors.WindowColor),
        };
    }

    private static double Luminance(System.Windows.Media.Color c) => ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255.0;

    /// <summary>Windows "app mode" setting (HKCU\...\Themes\Personalize\AppsUseLightTheme = 0 means dark).</summary>
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
#pragma warning disable CA1031 // Missing/blocked registry: fall back to light like Windows does.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
