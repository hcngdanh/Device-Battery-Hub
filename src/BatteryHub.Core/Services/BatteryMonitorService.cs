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

    // Guards _settingsSnapshot / _latestRaw / _latestDevices, which are shared between the
    // refresh thread (timer / thread pool) and the UI thread (UpdateSettings).
    private readonly object _stateLock = new();

    private System.Threading.Timer? _timer;
    private AppSettings _settingsSnapshot;
    private IReadOnlyList<BluetoothDeviceBattery> _latestRaw = Array.Empty<BluetoothDeviceBattery>();
    private IReadOnlyList<BluetoothDeviceBattery> _latestDevices = Array.Empty<BluetoothDeviceBattery>();
    private bool _hasPublished;
    private volatile bool _disposed;

    /// <summary>
    /// Live settings for the UI thread. Change them only through <see cref="UpdateSettings"/>;
    /// background refreshes work on an immutable snapshot so they never see a collection mid-edit.
    /// </summary>
    public AppSettings Settings { get; }

    public IReadOnlyList<BluetoothDeviceBattery> Devices
    {
        get { lock (_stateLock) return _latestDevices; }
    }

    /// <summary>
    /// Raised when the visible device list changes (battery, connection, charging, name, category,
    /// hidden flag) or settings are updated. Identical consecutive scans do not raise it.
    /// May be raised on a background thread.
    /// </summary>
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
        _settingsSnapshot = Settings.Clone();

        foreach (var provider in _liveProviders.OfType<IPushBatteryProvider>())
            provider.BatteryChanged += () => _ = RefreshAsync();
    }

    private static IReadOnlyList<IBatteryProvider> CreateDefaultLiveProviders() =>
    [
        new WindowsPnPBatteryProvider(),
        new AttackSharkHidBatteryProvider()
    ];

    public void Start()
    {
        _timer ??= new System.Threading.Timer(_ => _ = RefreshAsync(), null, RefreshIntervalMs, RefreshIntervalMs);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Mutates and persists settings (call on the UI thread), then immediately re-applies them to the
    /// last scan so the change shows up without waiting for the next poll.
    /// </summary>
    public void UpdateSettings(Action<AppSettings> mutate)
    {
        mutate(Settings);
        _settingsService.Save(Settings);

        var snapshot = Settings.Clone();
        IReadOnlyList<BluetoothDeviceBattery> processed;
        lock (_stateLock)
        {
            _settingsSnapshot = snapshot;
            processed = SortForDisplay(ApplySettings(_latestRaw, snapshot));
            _latestDevices = processed;
            _hasPublished = true;
        }

        DevicesUpdated?.Invoke(processed);
    }

    public async Task<IReadOnlyList<BluetoothDeviceBattery>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !await _refreshLock.WaitAsync(0, cancellationToken))
            return Devices;

        try
        {
            var lists = await Task.WhenAll(_liveProviders.Select(p => p.GetDevicesAsync(cancellationToken)));
            var raw = lists
                .SelectMany(x => x)
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            AppSettings settings;
            IReadOnlyList<BluetoothDeviceBattery> processed;
            bool changed;
            lock (_stateLock)
            {
                settings = _settingsSnapshot;
                processed = SortForDisplay(ApplySettings(raw, settings));
                changed = !_hasPublished || !HasSameContent(_latestDevices, processed);
                _latestRaw = raw;
                _latestDevices = processed;
                _hasPublished = true;
            }

            CheckNotifications(processed, settings);
            if (changed)
                DevicesUpdated?.Invoke(processed);
            return processed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Equal lists ignoring <see cref="BluetoothDeviceBattery.LastUpdated"/>, which changes on every scan.</summary>
    public static bool HasSameContent(IReadOnlyList<BluetoothDeviceBattery> a, IReadOnlyList<BluetoothDeviceBattery> b)
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] with { LastUpdated = default } != b[i] with { LastUpdated = default })
                return false;
        }
        return true;
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

    /// <summary>
    /// Display order across all providers: connected devices first, then by category and name,
    /// so a device read over HID isn't stuck below disconnected Bluetooth devices.
    /// </summary>
    public static IReadOnlyList<BluetoothDeviceBattery> SortForDisplay(IEnumerable<BluetoothDeviceBattery> devices) =>
        devices
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Category)
            .ThenBy(d => d.EffectiveName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public IReadOnlyList<BluetoothDeviceBattery> GetTaskbarDevices()
    {
        return Devices
            .Where(d => !d.IsHidden && (!Settings.ShowOnlyConnected || d.IsConnected))
            .Take(Settings.MaxTaskbarDevices)
            .ToList();
    }

    private void CheckNotifications(IReadOnlyList<BluetoothDeviceBattery> devices, AppSettings settings)
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
                    if (settings.EnableChargingStateNotification)
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
            if (settings.EnableLowBatteryNotification)
            {
                if (d.BatteryPercent <= settings.LowBatteryThreshold && !d.IsCharging)
                {
                    if (_notifiedLowIds.Add(d.Id))
                    {
                        var msg = $"{d.EffectiveName} battery is low ({d.BatteryPercent}%). Please charge soon.";
                        NotificationTriggered?.Invoke(d, NotificationEventType.LowBattery, msg);
                    }
                }
                else if (d.BatteryPercent > settings.LowBatteryThreshold + 5)
                {
                    _notifiedLowIds.Remove(d.Id);
                }
            }

            // 3. Fully Charged check (100% or high while charging/completed)
            if (settings.EnableFullBatteryNotification)
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
        _disposed = true;
        _timer?.Dispose();
        _refreshLock.Dispose();
        foreach (var provider in _liveProviders)
        {
            if (provider is IDisposable d)
                d.Dispose();
        }
    }
}
