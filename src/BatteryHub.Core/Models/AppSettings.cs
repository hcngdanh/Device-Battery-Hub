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

public sealed class AppSettings
{
    public int LowBatteryThreshold { get; set; } = 20;
    public bool ShowOnlyConnected { get; set; } = true;
    public bool EnableLowBatteryNotification { get; set; } = true;
    public bool EnableFullBatteryNotification { get; set; } = true;
    public bool EnableChargingStateNotification { get; set; } = true;
    public bool ColorCodeBatteryLevel { get; set; } = true;
    public TaskbarLayout TaskbarLayout { get; set; } = TaskbarLayout.Percentage;
    public bool LaunchAtStartup { get; set; } = false;
    public bool ShowTaskbarOverlay { get; set; } = true;
    public int MaxTaskbarDevices { get; set; } = 4;

    public Dictionary<string, string> DeviceAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DeviceCategory> CategoryOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> HiddenDeviceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
