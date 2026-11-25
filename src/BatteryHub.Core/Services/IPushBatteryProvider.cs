namespace BatteryHub.Core.Services;

/// <summary>
/// A provider that learns about battery changes on its own (e.g. HID input reports) and
/// raises <see cref="BatteryChanged"/> so the monitor can refresh immediately instead of
/// waiting for the next poll.
/// </summary>
public interface IPushBatteryProvider : IBatteryProvider
{
    event Action? BatteryChanged;
}
