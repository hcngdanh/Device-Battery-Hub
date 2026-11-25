namespace BatteryHub.Core.Models;

/// <summary>
/// What appears next to the (always visible) device-type icon on the taskbar.
/// </summary>
public enum TaskbarLayout
{
    Percentage = 0,
    Battery = 1,
    BatteryAndPercentage = 2
}

public enum AppLanguage
{
    English = 0,
    Vietnamese = 1
}

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum NotificationSound
{
    Default = 0,
    Notify = 1,
    Chimes = 2,
    Ding = 3,
    Alarm = 4
}

public enum FullChargeMode
{
    /// <summary>Notify at 100%.</summary>
    Full = 0,
    /// <summary>Notify when a charging device reaches <see cref="AppSettings.FullChargeThreshold"/>.</summary>
    Threshold = 1
}

public enum GameModeNotificationStyle
{
    /// <summary>Small, non-intrusive pill instead of the full popup.</summary>
    Compact = 0,
    /// <summary>No notifications while game mode is active.</summary>
    Off = 1
}

public enum OverlayCorner
{
    TopLeft = 0,
    TopRight = 1,
    BottomLeft = 2,
    BottomRight = 3
}

public enum TaskbarPosition
{
    /// <summary>Just left of the system tray (default).</summary>
    NearTray = 0,
    /// <summary>Far left edge of the taskbar.</summary>
    Left = 1
}

public sealed class AppSettings
{
    public static readonly int[] CriticalRepeatChoices = [5, 10, 15, 30, 60];
    public static readonly int[] HistoryRetentionChoices = [7, 30, 90];

    // ---- General ----
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool LaunchAtStartup { get; set; } = false;
    public bool AutoCheckUpdates { get; set; } = true;

    // ---- Taskbar ----
    public bool ShowTaskbarOverlay { get; set; } = true;
    public TaskbarLayout TaskbarLayout { get; set; } = TaskbarLayout.Percentage;
    public TaskbarPosition TaskbarPosition { get; set; } = TaskbarPosition.NearTray;
    public bool ColorCodeBatteryLevel { get; set; } = true;
    public bool ShowOnlyConnected { get; set; } = true;
    public int MaxTaskbarDevices { get; set; } = 4;

    // ---- Notifications ----
    public bool EnableLowBatteryNotification { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;

    public bool EnableCriticalBatteryNotification { get; set; } = true;
    public int CriticalBatteryThreshold { get; set; } = 10;
    public int CriticalRepeatMinutes { get; set; } = 10;

    public bool EnableFullBatteryNotification { get; set; } = true;
    public FullChargeMode FullChargeMode { get; set; } = FullChargeMode.Full;
    public int FullChargeThreshold { get; set; } = 80;

    public bool EnableChargingStateNotification { get; set; } = true;
    public bool EnableConnectionNotification { get; set; } = false;

    public bool EnableChargeReminder { get; set; } = false;
    public TimeSpan ChargeReminderTime { get; set; } = new(21, 0, 0);
    public int ChargeReminderThreshold { get; set; } = 30;

    public bool EnableSound { get; set; } = true;
    public NotificationSound Sound { get; set; } = NotificationSound.Default;
    public bool RespectFocusAssist { get; set; } = true;

    // ---- Game mode ----
    public bool GameModeAutoEnable { get; set; } = true;
    public GameModeNotificationStyle GameModeNotifications { get; set; } = GameModeNotificationStyle.Compact;
    public bool GameOverlayEnabled { get; set; } = true;
    public string GameOverlayHotkey { get; set; } = "Ctrl+Shift+B";
    public OverlayCorner GameOverlayCorner { get; set; } = OverlayCorner.TopRight;
    public double GameOverlayOpacity { get; set; } = 0.85;
    /// <summary>Devices shown in the game overlay; empty = all connected devices.</summary>
    public HashSet<string> GameOverlayDeviceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---- Battery history ----
    public bool HistoryEnabled { get; set; } = false;
    public int HistoryRetentionDays { get; set; } = 30;

    // ---- Per-device ----
    public Dictionary<string, string> DeviceAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DeviceCategory> CategoryOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> HiddenDeviceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Overwrites every setting with a deep copy of <paramref name="source"/> (import / restore defaults).</summary>
    public void CopyFrom(AppSettings source)
    {
        var copy = source.Clone();
        foreach (var prop in typeof(AppSettings).GetProperties())
        {
            if (prop.CanRead && prop.CanWrite)
                prop.SetValue(this, prop.GetValue(copy));
        }
    }

    /// <summary>Deep copy, so a background refresh can read settings while the UI edits the original.</summary>
    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.DeviceAliases = new Dictionary<string, string>(DeviceAliases, StringComparer.OrdinalIgnoreCase);
        copy.CategoryOverrides = new Dictionary<string, DeviceCategory>(CategoryOverrides, StringComparer.OrdinalIgnoreCase);
        copy.HiddenDeviceIds = new HashSet<string>(HiddenDeviceIds, StringComparer.OrdinalIgnoreCase);
        copy.GameOverlayDeviceIds = new HashSet<string>(GameOverlayDeviceIds, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}
