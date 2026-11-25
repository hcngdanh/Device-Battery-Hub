namespace BatteryHub.Core.Models;

public enum DeviceCategory
{
    Other = 0,
    Headphones = 1,
    Mouse = 2,
    Keyboard = 3,
    Gamepad = 4,
    Speaker = 5
}

public enum BatterySource
{
    WindowsPnP = 0,
    BleGatt = 1,
    UsbHid = 2
}

public sealed record BluetoothDeviceBattery
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public DeviceCategory Category { get; init; } = DeviceCategory.Other;
    public int BatteryPercent { get; init; }
    public bool IsConnected { get; init; }
    public bool IsCharging { get; init; }
    public bool IsHidden { get; init; }
    public BatterySource Source { get; init; }
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;

    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
}
