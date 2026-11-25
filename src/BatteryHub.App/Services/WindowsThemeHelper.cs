using System.Windows.Media;
using Microsoft.Win32;

namespace BatteryHub.App.Services;

public static class WindowsThemeHelper
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Returns true when the Windows taskbar is using Light mode.
    /// </summary>
    public static bool IsTaskbarLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            if (key?.GetValue("SystemUsesLightTheme") is int val)
                return val == 1;
        }
        catch
        {
            // Default to dark mode on Windows 11.
        }
        return false;
    }

    public static bool IsAppsLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 1;
        }
        catch
        {
            // Default to dark mode.
        }
        return false;
    }

    /// <summary>
    /// Windows accent color tuned like Windows 11 controls: Light2 shade in dark mode, Dark1 in light mode.
    /// </summary>
    public static System.Windows.Media.Color GetAccentColor(bool lightTheme)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", writable: false);
            if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
            {
                int index = lightTheme ? 4 : 1; // palette entries are RGBA: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, ...
                var color = System.Windows.Media.Color.FromRgb(palette[index * 4], palette[index * 4 + 1], palette[index * 4 + 2]);
                return EnsureContrast(color, lightTheme);
            }
        }
        catch
        {
            // Fall back to the default Windows 11 blue.
        }

        return lightTheme
            ? System.Windows.Media.Color.FromRgb(0x00, 0x5F, 0xB8)
            : System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF);
    }

    /// <summary>
    /// Low-saturation accents (e.g. graphite/steel) can be nearly invisible against the flyout background.
    /// Blend toward white (dark theme) or black (light theme) until the accent stands out.
    /// </summary>
    private static System.Windows.Media.Color EnsureContrast(System.Windows.Media.Color c, bool lightTheme)
    {
        byte target = lightTheme ? (byte)0 : (byte)255;
        for (int step = 0; step < 20; step++)
        {
            double lum = RelativeLuminance(c);
            if (lightTheme ? lum <= 0.18 : lum >= 0.42)
                break;
            c = System.Windows.Media.Color.FromRgb(Mix(c.R, target), Mix(c.G, target), Mix(c.B, target));
        }
        return c;

        static byte Mix(byte v, byte t) => (byte)Math.Round(v + (t - v) * 0.12);
    }

    /// <summary>Black or white, whichever reads better on top of <paramref name="background"/>.</summary>
    public static System.Windows.Media.Color GetOnAccentColor(System.Windows.Media.Color background) =>
        RelativeLuminance(background) > 0.35 ? Colors.Black : Colors.White;

    private static double RelativeLuminance(System.Windows.Media.Color c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
}
