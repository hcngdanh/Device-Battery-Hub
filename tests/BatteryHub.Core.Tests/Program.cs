using BatteryHub.Core.Models;
using BatteryHub.Core.Services;
using BatteryHub.Core.Services.Providers;

int passed = 0;
int failed = 0;

void Assert(bool condition, string message)
{
    if (condition)
    {
        passed++;
    }
    else
    {
        failed++;
        Console.Error.WriteLine($"FAIL: {message}");
    }
}

// 1. DeviceClassifier tests (Class of Device + Name)
Assert(DeviceClassifier.Classify("Unknown Device", 0x002580) == DeviceCategory.Mouse, "CoD 0x002580 should classify as Mouse");
Assert(DeviceClassifier.Classify("Unknown Device", 0x002540) == DeviceCategory.Keyboard, "CoD 0x002540 should classify as Keyboard");
Assert(DeviceClassifier.Classify("Unknown Device", 0x240418) == DeviceCategory.Headphones, "CoD 0x240418 should classify as Headphones");
Assert(DeviceClassifier.Classify("Logitech MX Master 3S") == DeviceCategory.Mouse, "MX Master 3S should classify as Mouse");
Assert(DeviceClassifier.Classify("Keychron K2 Pro") == DeviceCategory.Keyboard, "Keychron K2 Pro should classify as Keyboard");
Assert(DeviceClassifier.Classify("BT5.1-KB", 0x002580) == DeviceCategory.Keyboard, "BT5.1-KB should classify as Keyboard even with pointing CoD");
Assert(DeviceClassifier.Classify("Attack Shark X11") == DeviceCategory.Mouse, "Attack Shark X11 should classify as Mouse");
Assert(DeviceClassifier.Classify("Sony WH-1000XM5") == DeviceCategory.Headphones, "Sony WH-1000XM5 should classify as Headphones");

// Attack Shark X11 2.4GHz HID Report 0x03 parsing tests
Assert(
    AttackSharkHidBatteryProvider.TryParseBatteryReport([0x03, 0xA9, 0x40, 0x01, 76], out int pct1, out bool chg1) &&
    pct1 == 76 && !chg1,
    "Valid discharging Attack Shark X11 packet should parse 76%");
Assert(
    AttackSharkHidBatteryProvider.TryParseBatteryReport([0x03, 0xA9, 0x41, 0x03, 95], out int pct2, out bool chg2) &&
    pct2 == 95 && chg2,
    "Valid charging Attack Shark X11 packet should parse 95% and charging=true");
Assert(
    !AttackSharkHidBatteryProvider.TryParseBatteryReport([0x04, 0xA9, 0x40, 0x01, 50], out _, out _),
    "Non-0x03 report ID should be ignored");
Assert(
    !AttackSharkHidBatteryProvider.TryParseBatteryReport([0x03, 0xA9, 0x40, 0x01, 150], out _, out _),
    "Out-of-range battery (>100) should be rejected");
Assert(DeviceClassifier.Classify("Xbox Wireless Controller") == DeviceCategory.Gamepad, "Xbox Controller should classify as Gamepad");
Assert(DeviceClassifier.Classify("JBL Flip 6 Speaker") == DeviceCategory.Speaker, "JBL Speaker should classify as Speaker");

// 2. SettingsService roundtrip & normalization tests
string tempDir = Path.Combine(Path.GetTempPath(), "BatteryHubTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
try
{
    string settingsFile = Path.Combine(tempDir, "settings.json");
    var svc = new SettingsService(settingsFile);
    var s = svc.Load();
    s.LowBatteryThreshold = 18;
    s.TaskbarLayout = TaskbarLayout.BatteryAndPercentage;
    s.DeviceAliases["id-1"] = "Work Mouse";
    s.CategoryOverrides["id-1"] = DeviceCategory.Mouse;
    s.HiddenDeviceIds.Add("id-2");
    svc.Save(s);

    var reloaded = svc.Load();
    Assert(reloaded.LowBatteryThreshold == 18, "LowBatteryThreshold should persist");
    Assert(reloaded.TaskbarLayout == TaskbarLayout.BatteryAndPercentage, "TaskbarLayout should persist");
    Assert(reloaded.DeviceAliases["id-1"] == "Work Mouse", "Device alias should persist");
    Assert(reloaded.CategoryOverrides["id-1"] == DeviceCategory.Mouse, "Category override should persist");
    Assert(reloaded.HiddenDeviceIds.Contains("id-2"), "Hidden device should persist");
}
finally
{
    Directory.Delete(tempDir, recursive: true);
}

// 3. BatteryMonitorService settings merge + low battery alert hysteresis tests
var rawDevices = new List<BluetoothDeviceBattery>
{
    new() { Id = "d1", Name = "Mouse", Category = DeviceCategory.Other, BatteryPercent = 15, IsConnected = true, Source = BatterySource.WindowsPnP },
    new() { Id = "d2", Name = "Headset", Category = DeviceCategory.Headphones, BatteryPercent = 30, IsConnected = true, Source = BatterySource.WindowsPnP },
    new() { Id = "d3", Name = "Old KB", Category = DeviceCategory.Keyboard, BatteryPercent = 80, IsConnected = false, Source = BatterySource.WindowsPnP }
};

var mergeSettings = new AppSettings
{
    LowBatteryThreshold = 20,
    ShowOnlyConnected = true,
    DeviceAliases = { ["d1"] = "MX Master" },
    CategoryOverrides = { ["d1"] = DeviceCategory.Mouse },
    HiddenDeviceIds = { "d2" }
};

var merged = BatteryMonitorService.ApplySettings(rawDevices, mergeSettings);
Assert(merged[0].EffectiveName == "MX Master", "Alias should override EffectiveName");
Assert(merged[0].Category == DeviceCategory.Mouse, "Category override should apply");
Assert(merged[1].IsHidden, "Hidden device flag should apply");

var hbNow = DateTimeOffset.UtcNow;
Assert(!AttackSharkHidBatteryProvider.IsHeartbeatStale(hbNow.AddSeconds(-2.75), hbNow), "Report 2.75s ago (slowest observed heartbeat) should count as connected");
Assert(AttackSharkHidBatteryProvider.IsHeartbeatStale(hbNow.AddSeconds(-4), hbNow), "No report for 4s should count as disconnected (mouse switched off)");

var mockProvider = new StubProvider(rawDevices);
string tempSettings2 = Path.Combine(Path.GetTempPath(), $"bh_test_{Guid.NewGuid():N}.json");
try
{
    var ss = new SettingsService(tempSettings2);
    ss.Save(new AppSettings { LowBatteryThreshold = 20, EnableLowBatteryNotification = true, EnableChargingStateNotification = true, EnableCriticalBatteryNotification = false });
    using var monitor = new BatteryMonitorService(ss, liveProvider: mockProvider);

    int alertCount = 0;
    var events = new List<NotificationEventType>();
    monitor.NotificationTriggered += n =>
    {
        events.Add(n.Type);
        if (n.Type == NotificationEventType.LowBattery) alertCount++;
    };

    await monitor.RefreshAsync();
    Assert(alertCount == 1, "First refresh at 15% should trigger 1 low battery alert");

    await monitor.RefreshAsync();
    Assert(alertCount == 1, "Second refresh still at 15% should not re-alert");

    mockProvider.Devices[0] = rawDevices[0] with { BatteryPercent = 40 };
    await monitor.RefreshAsync();
    mockProvider.Devices[0] = rawDevices[0] with { BatteryPercent = 12 };
    await monitor.RefreshAsync();
    Assert(alertCount == 2, "Alert should re-arm after charging above threshold+5 and dropping again");

    events.Clear();
    mockProvider.Devices[0] = rawDevices[0] with { BatteryPercent = 12, IsCharging = true };
    await monitor.RefreshAsync();
    Assert(events.Contains(NotificationEventType.ChargingStarted), "Plugging in should raise ChargingStarted");

    events.Clear();
    mockProvider.Devices[0] = rawDevices[0] with { BatteryPercent = 100, IsCharging = true };
    await monitor.RefreshAsync();
    Assert(events.Count(t => t == NotificationEventType.FullBattery) == 1, "Reaching 100% should raise FullBattery once");
    await monitor.RefreshAsync();
    Assert(events.Count(t => t == NotificationEventType.FullBattery) == 1, "FullBattery should not repeat while still at 100%");

    events.Clear();
    mockProvider.Devices[0] = rawDevices[0] with { BatteryPercent = 100, IsCharging = false };
    await monitor.RefreshAsync();
    Assert(events.Contains(NotificationEventType.ChargingStopped), "Unplugging should raise ChargingStopped");

    monitor.UpdateSettings(x => x.EnableChargingStateNotification = false);
    events.Clear();
    mockProvider.Devices[0] = rawDevices[0] with { BatteryPercent = 100, IsCharging = true };
    await monitor.RefreshAsync();
    Assert(!events.Contains(NotificationEventType.ChargingStarted), "Charging notifications should respect the toggle");
}
finally
{
    if (File.Exists(tempSettings2)) File.Delete(tempSettings2);
}

// 4. Change detection: identical scans don't re-render; settings changes apply immediately
string tempSettings3 = Path.Combine(Path.GetTempPath(), $"bh_test_{Guid.NewGuid():N}.json");
try
{
    var provider3 = new StubProvider(rawDevices.Select(d => d with { }).ToList());
    using var monitor3 = new BatteryMonitorService(new SettingsService(tempSettings3), liveProvider: provider3);
    int updates = 0;
    IReadOnlyList<BluetoothDeviceBattery>? lastPublished = null;
    monitor3.DevicesUpdated += list => { updates++; lastPublished = list; };

    await monitor3.RefreshAsync();
    Assert(updates == 1, "First scan should publish");

    provider3.Devices[0] = provider3.Devices[0] with { LastUpdated = DateTimeOffset.UtcNow.AddSeconds(5) };
    await monitor3.RefreshAsync();
    Assert(updates == 1, "Identical scan (only LastUpdated differs) should not publish");

    provider3.Devices[0] = provider3.Devices[0] with { BatteryPercent = 14 };
    await monitor3.RefreshAsync();
    Assert(updates == 2, "Battery change should publish");

    monitor3.UpdateSettings(x => x.HiddenDeviceIds.Add("d1"));
    Assert(updates == 3 && lastPublished!.First(d => d.Id == "d1").IsHidden, "Settings change should re-apply and publish without a new scan");
    Assert(!monitor3.GetTaskbarDevices().Any(d => d.Id == "d1"), "Hidden device should leave the taskbar immediately");
}
finally
{
    if (File.Exists(tempSettings3)) File.Delete(tempSettings3);
}

// 5. Settings snapshot is a deep copy
var original = new AppSettings { HiddenDeviceIds = { "a" } };
var snapshot = original.Clone();
original.HiddenDeviceIds.Add("b");
Assert(!snapshot.HiddenDeviceIds.Contains("b") && snapshot.HiddenDeviceIds.Contains("A"), "Clone should deep-copy collections and keep case-insensitive comparers");

// 6. Display order: connected first regardless of provider order
var sorted = BatteryMonitorService.SortForDisplay(new[]
{
    new BluetoothDeviceBattery { Id = "bt-off", Name = "AirPods", Category = DeviceCategory.Headphones, IsConnected = false },
    new BluetoothDeviceBattery { Id = "hid-on", Name = "Attack Shark X11", Category = DeviceCategory.Mouse, IsConnected = true },
    new BluetoothDeviceBattery { Id = "bt-on", Name = "WH-CH520", Category = DeviceCategory.Headphones, IsConnected = true },
});
Assert(sorted.Select(d => d.Id).SequenceEqual(new[] { "bt-on", "hid-on", "bt-off" }), "Connected devices should come first, then category/name");

// 7. NotificationEngine: critical repeat, full threshold, connection, reminder
{
    var engine = new NotificationEngine();
    var cfg = new AppSettings
    {
        EnableCriticalBatteryNotification = true, CriticalBatteryThreshold = 10, CriticalRepeatMinutes = 10,
        EnableConnectionNotification = true,
        EnableFullBatteryNotification = true, FullChargeMode = FullChargeMode.Threshold, FullChargeThreshold = 80,
        EnableChargeReminder = true, ChargeReminderTime = new TimeSpan(21, 0, 0), ChargeReminderThreshold = 30
    };
    var t0 = new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.FromHours(7));
    var dev = new BluetoothDeviceBattery { Id = "x", Name = "Mouse", BatteryPercent = 8, IsConnected = true };

    var r1 = engine.Evaluate([dev], cfg, t0);
    Assert(r1.Count(n => n.Type == NotificationEventType.CriticalBattery) == 1, "Critical fires at 8% (threshold 10)");
    Assert(!r1.Any(n => n.Type == NotificationEventType.LowBattery), "Low is suppressed when critical fires at the same time");
    Assert(!engine.Evaluate([dev], cfg, t0.AddMinutes(5)).Any(n => n.Type == NotificationEventType.CriticalBattery), "Critical does not repeat before the interval");
    Assert(engine.Evaluate([dev], cfg, t0.AddMinutes(10)).Any(n => n.Type == NotificationEventType.CriticalBattery), "Critical repeats after 10 minutes");
    Assert(!engine.Evaluate([dev with { IsCharging = true }], cfg, t0.AddMinutes(30)).Any(n => n.Type == NotificationEventType.CriticalBattery), "No critical while charging");

    var r2 = engine.Evaluate([dev with { IsConnected = false }], cfg, t0.AddMinutes(31));
    Assert(r2.Any(n => n.Type == NotificationEventType.DeviceDisconnected), "Disconnect is announced");
    Assert(engine.Evaluate([dev], cfg, t0.AddMinutes(32)).Any(n => n.Type == NotificationEventType.DeviceConnected), "Reconnect is announced");

    var full = dev with { BatteryPercent = 80, IsCharging = true };
    Assert(engine.Evaluate([full], cfg, t0.AddMinutes(40)).Any(n => n.Type == NotificationEventType.FullBattery), "Threshold mode: charging device at 80% counts as full");
    Assert(!engine.Evaluate([full with { BatteryPercent = 85 }], cfg, t0.AddMinutes(41)).Any(n => n.Type == NotificationEventType.FullBattery), "Full fires once per charge");

    var engine2 = new NotificationEngine();
    var low = dev with { BatteryPercent = 25 };
    var headset = new BluetoothDeviceBattery { Id = "h", Name = "Headset", BatteryPercent = 90, IsConnected = true };
    Assert(!engine2.Evaluate([low, headset], cfg, t0).Any(n => n.Type == NotificationEventType.ChargeReminder), "No reminder before 21:00");
    var rem = engine2.Evaluate([low, headset], cfg, t0.AddHours(1).AddMinutes(1)).SingleOrDefault(n => n.Type == NotificationEventType.ChargeReminder);
    Assert(rem is not null && rem.Devices.Count == 1 && rem.Device.Id == "x", "Reminder at 21:01 lists only devices at or below 30%");
    Assert(!engine2.Evaluate([low], cfg, t0.AddHours(2)).Any(n => n.Type == NotificationEventType.ChargeReminder), "Reminder fires once per day");
    Assert(engine2.Evaluate([low], cfg, t0.AddDays(1).AddHours(1).AddMinutes(5)).Any(n => n.Type == NotificationEventType.ChargeReminder), "Reminder fires again the next day");
}

// 8. Battery history: change-only logging, read back, prune
{
    string histDir = Path.Combine(Path.GetTempPath(), "bh_hist_" + Guid.NewGuid().ToString("N"));
    try
    {
        var history = new BatteryHistoryService(histDir);
        var now = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
        var a = new BluetoothDeviceBattery { Id = "a", Name = "Mouse, wireless", BatteryPercent = 50, IsConnected = true };
        history.Record([a], now, 30);
        history.Record([a], now.AddMinutes(1), 30);                       // unchanged -> skipped
        history.Record([a with { BatteryPercent = 49 }], now.AddMinutes(2), 30);
        history.Record([a with { BatteryPercent = 49 }], now.AddMinutes(20), 30); // heartbeat
        var samples = history.Read("a", now.AddHours(-1));
        Assert(samples.Count == 3, $"History writes on change + heartbeat only (got {samples.Count})");
        Assert(samples[0].DeviceName == "Mouse  wireless" && samples[1].Percent == 49, "History round-trips name and level");

        File.WriteAllText(Path.Combine(histDir, "2020-01-01.csv"), "timestamp,id,percent,charging,connected,name\n");
        history.Prune(30, now);
        Assert(!File.Exists(Path.Combine(histDir, "2020-01-01.csv")), "Prune deletes files older than retention");
        history.Clear();
        Assert(history.Read(null, now.AddDays(-1)).Count == 0, "Clear removes all samples");
    }
    finally
    {
        if (Directory.Exists(histDir)) Directory.Delete(histDir, true);
    }
}

// 9. Settings export / import / normalization
{
    string exportPath = Path.Combine(Path.GetTempPath(), $"bh_export_{Guid.NewGuid():N}.json");
    try
    {
        var cfg = new AppSettings { Language = AppLanguage.Vietnamese, Theme = AppTheme.Dark, CriticalRepeatMinutes = 12, HistoryRetentionDays = 40, GameOverlayDeviceIds = { "Dev1" } };
        SettingsService.Export(cfg, exportPath);
        Assert(File.ReadAllText(exportPath).Contains("\"Vietnamese\""), "Export writes enums as names");
        var imported = SettingsService.Import(exportPath);
        Assert(imported.Language == AppLanguage.Vietnamese && imported.Theme == AppTheme.Dark, "Import restores enums");
        Assert(imported.CriticalRepeatMinutes == 10 && imported.HistoryRetentionDays == 30, "Normalize snaps to allowed choices");
        Assert(imported.GameOverlayDeviceIds.Contains("dev1"), "Imported id sets are case-insensitive");

        File.WriteAllText(exportPath, "not json");
        bool threw = false;
        try { SettingsService.Import(exportPath); } catch (InvalidDataException) { threw = true; }
        Assert(threw, "Importing a non-settings file throws InvalidDataException");

        File.WriteAllText(exportPath, "{\"TaskbarLayout\": 2, \"LowBatteryThreshold\": 25}");
        var legacy = SettingsService.Import(exportPath);
        Assert(legacy.TaskbarLayout == TaskbarLayout.BatteryAndPercentage && legacy.LowBatteryThreshold == 25, "Older files with numeric enums still load");
    }
    finally
    {
        if (File.Exists(exportPath)) File.Delete(exportPath);
    }
}

Console.WriteLine($"BatteryHub.Core.Tests: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class StubProvider(List<BluetoothDeviceBattery> devices) : IBatteryProvider
{
    public List<BluetoothDeviceBattery> Devices { get; } = devices;
    public Task<IReadOnlyList<BluetoothDeviceBattery>> GetDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BluetoothDeviceBattery>>(Devices.ToList());
}
