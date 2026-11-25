using System.IO;
using System.Media;
using System.Windows.Media;
using BatteryHub.App.Controls;
using BatteryHub.App.Interop;
using BatteryHub.App.Localization;
using BatteryHub.App.Views;
using BatteryHub.Core.Models;
using BatteryHub.Core.Services;

namespace BatteryHub.App.Services;

/// <summary>
/// Turns engine notifications into localized toasts and decides how (or whether) to show them:
/// Do not disturb, game mode (compact / off) and sound.
/// </summary>
public sealed class NotificationPresenter
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool> _isGameMode;
    private ToastWindow? _toast;

    public NotificationPresenter(Func<AppSettings> settings, Func<bool> isGameMode)
    {
        _settings = settings;
        _isGameMode = isGameMode;
    }

    public void Show(BatteryNotification n)
    {
        var d = n.Device;
        string name = d.EffectiveName;
        var (titleKey, bodyKey, colorKind) = n.Type switch
        {
            NotificationEventType.LowBattery => ("N_LowTitle", "N_LowBody", ToastColor.Warning),
            NotificationEventType.CriticalBattery => ("N_CriticalTitle", "N_CriticalBody", ToastColor.Danger),
            NotificationEventType.FullBattery => ("N_FullTitle", "N_FullBody", ToastColor.Good),
            NotificationEventType.ChargingStarted => ("N_ChargingTitle", "N_ChargingBody", ToastColor.Good),
            NotificationEventType.ChargingStopped => ("N_UnpluggedTitle", "N_UnpluggedBody", ToastColor.Neutral),
            NotificationEventType.DeviceConnected => ("N_ConnectedTitle", "N_ConnectedBody", ToastColor.Accent),
            NotificationEventType.DeviceDisconnected => ("N_DisconnectedTitle", "N_DisconnectedBody", ToastColor.Neutral),
            _ => ("N_ReminderTitle", "N_ReminderBody", ToastColor.Warning)
        };

        string title = Loc.T(titleKey);
        string body = n.Type == NotificationEventType.ChargeReminder
            ? Loc.T(bodyKey, string.Join(", ", n.Devices.Select(x => $"{x.EffectiveName} {x.BatteryPercent}%")))
            : Loc.T(bodyKey, name, d.BatteryPercent);
        string compact = n.Type == NotificationEventType.ChargeReminder
            ? $"{title}: {string.Join(", ", n.Devices.Select(x => $"{x.EffectiveName} {x.BatteryPercent}%"))}"
            : $"{name} · {d.BatteryPercent}% · {title}";

        Geometry? icon = n.Type == NotificationEventType.ChargeReminder ? null : DeviceIconPaths.GetGeometry(d.Category);
        string? glyph = n.Type == NotificationEventType.ChargeReminder ? "" : null;
        var duration = n.Type == NotificationEventType.CriticalBattery ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(6);

        Present(title, body, compact, icon, glyph, colorKind, duration, playSound: true);
    }

    public void ShowMessage(string title, string body, string glyph, Action? onClick = null) =>
        Present(title, body, title, null, glyph, ToastColor.Accent, TimeSpan.FromSeconds(8), playSound: false, onClick);

    public static void PlaySound(NotificationSound sound)
    {
        string file = sound switch
        {
            NotificationSound.Notify => "Windows Notify Messaging.wav",
            NotificationSound.Chimes => "chimes.wav",
            NotificationSound.Ding => "Windows Ding.wav",
            NotificationSound.Alarm => "Alarm01.wav",
            _ => "Windows Notify System Generic.wav"
        };

        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", file);
        try
        {
            if (File.Exists(path))
                new SoundPlayer(path).Play();
            else
                SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Audio device unavailable.
        }
    }

    private void Present(string title, string body, string compactText, Geometry? icon, string? glyph,
        ToastColor colorKind, TimeSpan duration, bool playSound, Action? onClick = null)
    {
        var s = _settings();
        if (s.RespectFocusAssist && ShellState.IsDoNotDisturbOn())
            return;

        bool game = _isGameMode();
        if (game && s.GameModeNotifications == GameModeNotificationStyle.Off)
            return;

        _toast ??= new ToastWindow();
        if (game)
            _toast.ShowCompact(compactText, icon, glyph, ResolveColor(colorKind, light: false), TimeSpan.FromSeconds(3.5));
        else
            _toast.ShowFull(title, body, icon, glyph, ResolveColor(colorKind, ThemeManager.IsLight), duration, onClick);

        if (playSound && s.EnableSound)
            PlaySound(s.Sound);
    }

    private enum ToastColor { Neutral, Accent, Good, Warning, Danger }

    private static System.Windows.Media.Brush ResolveColor(ToastColor kind, bool light)
    {
        var color = kind switch
        {
            ToastColor.Good => light ? System.Windows.Media.Color.FromRgb(22, 138, 70) : System.Windows.Media.Color.FromRgb(82, 196, 125),
            ToastColor.Warning => light ? System.Windows.Media.Color.FromRgb(180, 110, 10) : System.Windows.Media.Color.FromRgb(242, 180, 65),
            ToastColor.Danger => light ? System.Windows.Media.Color.FromRgb(210, 45, 30) : System.Windows.Media.Color.FromRgb(255, 90, 80),
            ToastColor.Accent => WindowsThemeHelper.GetAccentColor(light),
            _ => light ? System.Windows.Media.Color.FromRgb(26, 26, 26) : System.Windows.Media.Color.FromRgb(236, 236, 236)
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
