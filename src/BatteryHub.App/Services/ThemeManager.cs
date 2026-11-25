using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BatteryHub.Core.Models;

namespace BatteryHub.App.Services;

/// <summary>
/// Applies the app theme (System / Light / Dark) by swapping the shared brushes in
/// Application.Resources, which every window consumes through DynamicResource.
/// The taskbar readout is not affected: it always follows the real taskbar colour.
/// </summary>
public static class ThemeManager
{
    private static bool? _appliedLight;
    private static System.Windows.Media.Color _appliedAccent;

    public static bool IsLight { get; private set; }

    public static event Action? Changed;

    public static void Apply(AppTheme theme)
    {
        bool light = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => WindowsThemeHelper.IsAppsLightMode()
        };
        var accent = WindowsThemeHelper.GetAccentColor(light);
        if (_appliedLight == light && _appliedAccent == accent)
            return;

        _appliedLight = light;
        _appliedAccent = accent;
        IsLight = light;

        var res = System.Windows.Application.Current.Resources;
        void Set(string key, uint argb)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            brush.Freeze();
            res[key] = brush;
        }

        if (light)
        {
            Set("BgBrush", 0xFFF3F3F3);
            Set("WindowBrush", 0xFFF3F3F3);
            Set("PopupBrush", 0xFFF9F9F9);
            Set("StrokeBrush", 0x1A000000);
            Set("CardBrush", 0xFFFBFBFB);
            Set("DividerBrush", 0x0F000000);
            Set("TextBrush", 0xFF1B1B1B);
            Set("SubTextBrush", 0xFF5F5F5F);
            Set("ControlBrush", 0xB3FFFFFF);
            Set("ControlHoverBrush", 0x0D000000);
            Set("ControlPressedBrush", 0x08000000);
            Set("ToggleOffBrush", 0xFF868686);
            Set("TrackBrush", 0x29000000);
            Set("TileBrush", 0x0A000000);
            Set("ThumbBrush", 0xFFFFFFFF);
            Set("PreviewBgBrush", 0xFFEEEEEE);
            Set("DangerBrush", 0xFFC42B1C);
        }
        else
        {
            Set("BgBrush", 0xFF2B2B2B);
            Set("WindowBrush", 0xFF202020);
            Set("PopupBrush", 0xFF2C2C2C);
            Set("StrokeBrush", 0x1FFFFFFF);
            Set("CardBrush", 0xFF2D2D2D);
            Set("DividerBrush", 0x14FFFFFF);
            Set("TextBrush", 0xFFFFFFFF);
            Set("SubTextBrush", 0xFFABABAB);
            Set("ControlBrush", 0x0FFFFFFF);
            Set("ControlHoverBrush", 0x18FFFFFF);
            Set("ControlPressedBrush", 0x0AFFFFFF);
            Set("ToggleOffBrush", 0xFFC5C5C5);
            Set("TrackBrush", 0x33FFFFFF);
            Set("TileBrush", 0x12FFFFFF);
            Set("ThumbBrush", 0xFF454545);
            Set("PreviewBgBrush", 0xFF1C1C1C);
            Set("DangerBrush", 0xFFFF99A4);
        }

        Set("SystemAccent", 0xFF000000u | ((uint)accent.R << 16) | ((uint)accent.G << 8) | accent.B);
        var onAccent = WindowsThemeHelper.GetOnAccentColor(accent);
        Set("OnAccentBrush", 0xFF000000u | ((uint)onAccent.R << 16) | ((uint)onAccent.G << 8) | onAccent.B);

        foreach (Window window in System.Windows.Application.Current.Windows)
            ApplyTitleBar(window);

        Changed?.Invoke();
    }

    /// <summary>Dark/light native title bar for standard-chrome windows (Windows 10 20H1+ / 11).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int dark = IsLight ? 0 : 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
