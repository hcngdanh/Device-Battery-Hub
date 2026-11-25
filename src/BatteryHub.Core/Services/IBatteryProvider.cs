using BatteryHub.Core.Models;

namespace BatteryHub.Core.Services;

public interface IBatteryProvider
{
    Task<IReadOnlyList<BluetoothDeviceBattery>> GetDevicesAsync(CancellationToken cancellationToken = default);
}
