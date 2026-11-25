using System.Windows;
using Microsoft.Win32;
using SnapForge.App.Interop;
using SnapForge.Core.Settings;

namespace SnapForge.App.Themes;

/// <summary>Switches between the light and dark palettes at run time (all styles use DynamicResource).</summary>
internal static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static event EventHandler? ThemeChanged;

    public static void Apply(ThemePreference preference)
    {
        bool dark = preference switch
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

        var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/SnapForge;component/Themes/Palette.{(dark ? "Dark" : "Light")}.xaml") };
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
