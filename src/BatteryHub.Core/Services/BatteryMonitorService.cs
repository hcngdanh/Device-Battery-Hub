using BatteryHub.Core.Models;
using BatteryHub.Core.Services.Providers;

namespace BatteryHub.Core.Services;

public enum NotificationEventType
{
    LowBattery,
    FullBattery,
    ChargingStarted,
    ChargingStopped
}

public sealed class BatteryMonitorService : IDisposable
{
    /// <summary>
    /// Polling period for PnP/BLE providers. A full scan takes ~200ms, so 1s is the fastest
    /// practical cadence; overlapping refreshes are skipped by <see cref="_refreshLock"/>.
    /// Device arrival/removal and HID battery pushes trigger refreshes immediately on top of this.
    /// </summary>
    public const int RefreshIntervalMs = 1000;

    private readonly IReadOnlyList<IBatteryProvider> _liveProviders;
    private readonly SettingsService _settingsService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly HashSet<string> _notifiedLowIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notifiedFullIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _previousChargingState = new(StringComparer.OrdinalIgnoreCase);

    private System.Threading.Timer? _timer;
    private IReadOnlyList<BluetoothDeviceBattery> _latestDevices = Array.Empty<BluetoothDeviceBattery>();

    public AppSettings Settings { get; private set; }
    public IReadOnlyList<BluetoothDeviceBattery> Devices => _latestDevices;

    public event Action<IReadOnlyList<BluetoothDeviceBattery>>? DevicesUpdated;
    public event Action<BluetoothDeviceBattery, NotificationEventType, string>? NotificationTriggered;

    public BatteryMonitorService(
        SettingsService settingsService,
        IBatteryProvider? liveProvider = null)
        : this(
            settingsService,
            liveProvider is not null
                ? [liveProvider]
                : CreateDefaultLiveProviders())
    {
    }

    public BatteryMonitorService(
        SettingsService settingsService,
        IEnumerable<IBatteryProvider> liveProviders)
    {
        _settingsService = settingsService;
        _liveProviders = liveProviders.ToList();
        Settings = _settingsService.Load();

        foreach (var provider in _liveProviders)
        {
            if (provider is AttackSharkHidBatteryProvider hidProvider)
                hidProvider.BatteryChanged += () => _ = RefreshAsync();
        }
    }

    private static IReadOnlyList<IBatteryProvider> CreateDefaultLiveProviders() =>
    [
        new WindowsPnPBatteryProvider(),
        new AttackSharkHidBatteryProvider()
    ];

    public void Start()
    {
        RestartTimer();
        _ = RefreshAsync();
    }

    private void RestartTimer()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => _ = RefreshAsync(), null, RefreshIntervalMs, RefreshIntervalMs);
    }

    public void UpdateSettings(Action<AppSettings> mutate, bool triggerRefresh = true)
    {
        mutate(Settings);
        _settingsService.Save(Settings);
        if (triggerRefresh)
            _ = RefreshAsync();
    }

    public async Task<IReadOnlyList<BluetoothDeviceBattery>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken))
            return _latestDevices;

        try
        {
            var lists = await Task.WhenAll(_liveProviders.Select(p => p.GetDevicesAsync(cancellationToken)));
            var raw = lists
                .SelectMany(x => x)
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var processed = ApplySettings(raw, Settings);

            _latestDevices = processed;
            CheckNotifications(processed);
            DevicesUpdated?.Invoke(processed);
            return processed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public static IReadOnlyList<BluetoothDeviceBattery> ApplySettings(
        IReadOnlyList<BluetoothDeviceBattery> raw,
        AppSettings settings)
    {
        return raw
            .Select(d =>
            {
                string alias = settings.DeviceAliases.TryGetValue(d.Id, out var a) ? a : string.Empty;
                var category = settings.CategoryOverrides.TryGetValue(d.Id, out var c) ? c : d.Category;
                bool hidden = settings.HiddenDeviceIds.Contains(d.Id);

                return d with
                {
                    DisplayName = alias,
                    Category = category,
                    IsHidden = hidden
                };
            })
            .ToList();
    }

    public IReadOnlyList<BluetoothDeviceBattery> GetTaskbarDevices()
    {
        return _latestDevices
            .Where(d => !d.IsHidden && (!Settings.ShowOnlyConnected || d.IsConnected))
            .Take(Settings.MaxTaskbarDevices)
            .ToList();
    }

    private void CheckNotifications(IReadOnlyList<BluetoothDeviceBattery> devices)
    {
        foreach (var d in devices)
        {
            if (!d.IsConnected || d.IsHidden)
                continue;

            // 1. Charging state transitions (plugged in / unplugged)
            if (_previousChargingState.TryGetValue(d.Id, out bool prevCharging))
            {
                if (prevCharging != d.IsCharging)
                {
                    _previousChargingState[d.Id] = d.IsCharging;
                    if (Settings.EnableChargingStateNotification)
                    {
                        var eventType = d.IsCharging ? NotificationEventType.ChargingStarted : NotificationEventType.ChargingStopped;
                        var msg = d.IsCharging
                            ? $"{d.EffectiveName} is now charging ({d.BatteryPercent}%)."
                            : $"{d.EffectiveName} unplugged from charger ({d.BatteryPercent}%).";
                        NotificationTriggered?.Invoke(d, eventType, msg);
                    }
                }
            }
            else
            {
                _previousChargingState[d.Id] = d.IsCharging;
            }

            // 2. Low Battery check
            if (Settings.EnableLowBatteryNotification)
            {
                if (d.BatteryPercent <= Settings.LowBatteryThreshold && !d.IsCharging)
                {
                    if (_notifiedLowIds.Add(d.Id))
                    {
                        var msg = $"{d.EffectiveName} battery is low ({d.BatteryPercent}%). Please charge soon.";
                        NotificationTriggered?.Invoke(d, NotificationEventType.LowBattery, msg);
                    }
                }
                else if (d.BatteryPercent > Settings.LowBatteryThreshold + 5)
                {
                    _notifiedLowIds.Remove(d.Id);
                }
            }

            // 3. Fully Charged check (100% or high while charging/completed)
            if (Settings.EnableFullBatteryNotification)
            {
                if (d.BatteryPercent >= 100)
                {
                    if (_notifiedFullIds.Add(d.Id))
                    {
                        var msg = $"{d.EffectiveName} is fully charged (100%).";
                        NotificationTriggered?.Invoke(d, NotificationEventType.FullBattery, msg);
                    }
                }
                else if (d.BatteryPercent <= 95)
                {
                    _notifiedFullIds.Remove(d.Id);
                }
            }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _refreshLock.Dispose();
        foreach (var provider in _liveProviders)
        {
            if (provider is IDisposable d)
                d.Dispose();
        }
    }
}
