using BatteryHub.Core.Models;

namespace BatteryHub.Core.Services;

public enum NotificationEventType
{
    LowBattery,
    CriticalBattery,
    FullBattery,
    ChargingStarted,
    ChargingStopped,
    DeviceConnected,
    DeviceDisconnected,
    ChargeReminder
}

/// <summary>
/// A notification decided by <see cref="NotificationEngine"/>. Text is produced by the UI layer
/// (localized); <see cref="Devices"/> holds one device, or several for <see cref="NotificationEventType.ChargeReminder"/>.
/// </summary>
public sealed record BatteryNotification(NotificationEventType Type, IReadOnlyList<BluetoothDeviceBattery> Devices)
{
    public BluetoothDeviceBattery Device => Devices[0];
}

/// <summary>
/// Turns successive device snapshots into notifications. Stateful (remembers what was already announced)
/// and time-driven, so it is fed the current local time instead of reading the clock itself.
/// Not thread-safe: call from one thread at a time (the monitor's refresh lock guarantees this).
/// </summary>
public sealed class NotificationEngine
{
    private const int Hysteresis = 5;

    private readonly HashSet<string> _notifiedLow = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notifiedFull = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastCritical = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _previousCharging = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// How long an inferred disconnect must last before it is announced. Shorter silences are a
    /// sleeping device (mouse left idle), not one that was switched off.
    /// </summary>
    public static readonly TimeSpan InferredDisconnectGrace = TimeSpan.FromMinutes(5);

    /// <summary>Last connection state the user was told about (or first seen).</summary>
    private readonly Dictionary<string, bool> _previousConnected = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>When an inferred device went silent and the disconnect hasn't been announced yet.</summary>
    private readonly Dictionary<string, DateTimeOffset> _silentSince = new(StringComparer.OrdinalIgnoreCase);
    private DateOnly? _lastReminderDate;

    private void EvaluateConnection(BluetoothDeviceBattery d, AppSettings settings, DateTimeOffset now, List<BatteryNotification> result)
    {
        // The first sighting only records state, so startup isn't noisy.
        if (!_previousConnected.TryGetValue(d.Id, out bool announced))
        {
            _previousConnected[d.Id] = d.IsConnected;
            return;
        }

        if (d.IsConnected == announced)
        {
            // Woke up before the grace period ran out: nothing was announced, so nothing to undo.
            _silentSince.Remove(d.Id);
            return;
        }

        if (d.ConnectionInferred && !d.IsConnected)
        {
            if (!_silentSince.TryGetValue(d.Id, out var since))
            {
                _silentSince[d.Id] = now;
                return;
            }
            if (now - since < InferredDisconnectGrace)
                return;
        }

        _silentSince.Remove(d.Id);
        _previousConnected[d.Id] = d.IsConnected;
        if (settings.EnableConnectionNotification)
            result.Add(Single(d.IsConnected ? NotificationEventType.DeviceConnected : NotificationEventType.DeviceDisconnected, d));
    }

    public IReadOnlyList<BatteryNotification> Evaluate(
        IReadOnlyList<BluetoothDeviceBattery> devices,
        AppSettings settings,
        DateTimeOffset nowLocal)
    {
        var result = new List<BatteryNotification>();

        foreach (var d in devices)
        {
            if (d.IsHidden)
                continue;

            EvaluateConnection(d, settings, nowLocal, result);

            if (!d.IsConnected)
                continue;

            EvaluateCharging(d, settings, result);
            bool critical = EvaluateCritical(d, settings, nowLocal, result);
            EvaluateLow(d, settings, suppress: critical, result);
            EvaluateFull(d, settings, result);
        }

        EvaluateReminder(devices, settings, nowLocal, result);
        return result;
    }

    private void EvaluateCharging(BluetoothDeviceBattery d, AppSettings settings, List<BatteryNotification> result)
    {
        if (_previousCharging.TryGetValue(d.Id, out bool wasCharging) && wasCharging != d.IsCharging)
        {
            if (settings.EnableChargingStateNotification)
                result.Add(Single(d.IsCharging ? NotificationEventType.ChargingStarted : NotificationEventType.ChargingStopped, d));
        }
        _previousCharging[d.Id] = d.IsCharging;
    }

    /// <returns>true when a critical notification was raised for this device in this pass.</returns>
    private bool EvaluateCritical(BluetoothDeviceBattery d, AppSettings settings, DateTimeOffset now, List<BatteryNotification> result)
    {
        if (!settings.EnableCriticalBatteryNotification)
        {
            _lastCritical.Remove(d.Id);
            return false;
        }

        if (d.IsCharging || d.BatteryPercent > settings.CriticalBatteryThreshold + Hysteresis)
        {
            _lastCritical.Remove(d.Id);
            return false;
        }

        if (d.BatteryPercent > settings.CriticalBatteryThreshold)
            return false;

        var repeat = TimeSpan.FromMinutes(settings.CriticalRepeatMinutes);
        if (_lastCritical.TryGetValue(d.Id, out var last) && now - last < repeat)
            return false;

        _lastCritical[d.Id] = now;
        result.Add(Single(NotificationEventType.CriticalBattery, d));
        return true;
    }

    private void EvaluateLow(BluetoothDeviceBattery d, AppSettings settings, bool suppress, List<BatteryNotification> result)
    {
        if (!settings.EnableLowBatteryNotification)
            return;

        if (d.BatteryPercent <= settings.LowBatteryThreshold && !d.IsCharging)
        {
            // Add even when suppressed so the low alert doesn't follow right after a critical one.
            if (_notifiedLow.Add(d.Id) && !suppress)
                result.Add(Single(NotificationEventType.LowBattery, d));
        }
        else if (d.BatteryPercent > settings.LowBatteryThreshold + Hysteresis)
        {
            _notifiedLow.Remove(d.Id);
        }
    }

    private void EvaluateFull(BluetoothDeviceBattery d, AppSettings settings, List<BatteryNotification> result)
    {
        if (!settings.EnableFullBatteryNotification)
            return;

        int target = settings.FullChargeMode == FullChargeMode.Threshold ? settings.FullChargeThreshold : 100;
        // Below 100% only a device that is actually charging counts as "reached the target".
        bool reached = d.BatteryPercent >= target && (target >= 100 || d.IsCharging);

        if (reached)
        {
            if (_notifiedFull.Add(d.Id))
                result.Add(Single(NotificationEventType.FullBattery, d));
        }
        else if (d.BatteryPercent <= target - Hysteresis)
        {
            _notifiedFull.Remove(d.Id);
        }
    }

    private void EvaluateReminder(IReadOnlyList<BluetoothDeviceBattery> devices, AppSettings settings, DateTimeOffset now, List<BatteryNotification> result)
    {
        if (!settings.EnableChargeReminder)
            return;

        var today = DateOnly.FromDateTime(now.DateTime);
        if (_lastReminderDate == today || now.TimeOfDay < settings.ChargeReminderTime)
            return;

        _lastReminderDate = today;
        var needCharge = devices
            .Where(d => !d.IsHidden && d.IsConnected && !d.IsCharging && d.BatteryPercent <= settings.ChargeReminderThreshold)
            .ToList();

        if (needCharge.Count > 0)
            result.Add(new BatteryNotification(NotificationEventType.ChargeReminder, needCharge));
    }

    private static BatteryNotification Single(NotificationEventType type, BluetoothDeviceBattery d) => new(type, [d]);
}
