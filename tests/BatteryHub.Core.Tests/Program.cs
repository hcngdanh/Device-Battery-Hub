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
    ss.Save(new AppSettings { LowBatteryThreshold = 20, EnableLowBatteryNotification = true, EnableChargingStateNotification = true });
    using var monitor = new BatteryMonitorService(ss, liveProvider: mockProvider);

    int alertCount = 0;
    var events = new List<NotificationEventType>();
    monitor.NotificationTriggered += (_, type, _) =>
    {
        events.Add(type);
        if (type == NotificationEventType.LowBattery) alertCount++;
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

Console.WriteLine($"BatteryHub.Core.Tests: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class StubProvider(List<BluetoothDeviceBattery> devices) : IBatteryProvider
{
    public List<BluetoothDeviceBattery> Devices { get; } = devices;
    public Task<IReadOnlyList<BluetoothDeviceBattery>> GetDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BluetoothDeviceBattery>>(Devices.ToList());
}
