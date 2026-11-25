using System.Threading;
using System.Windows;
using BatteryHub.App.Services;
using BatteryHub.App.ViewModels;
using BatteryHub.App.Views;
using BatteryHub.Core.Models;
using BatteryHub.Core.Services;
using Microsoft.Win32;

namespace BatteryHub.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private BatteryMonitorService _monitorService = null!;
    private TaskbarOverlayWindow _overlayWindow = null!;
    private TaskbarAnchorService _anchorService = null!;
    private FlyoutWindow _flyoutWindow = null!;
    private TrayIconService _trayIconService = null!;
    private System.Threading.Timer? _deviceChangeDebounce;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "BatteryHub_SingleInstance_Mutex", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--log-hid", StringComparison.OrdinalIgnoreCase)))
        {
            BatteryHub.Core.Services.Providers.AttackSharkHidBatteryProvider.EnableDiagnosticLog = true;
        }

        RepairStartupEntry();

        _monitorService = new BatteryMonitorService(new SettingsService());
        _overlayWindow = new TaskbarOverlayWindow();
        _anchorService = new TaskbarAnchorService(_overlayWindow);
        _flyoutWindow = new FlyoutWindow();
        _trayIconService = new TrayIconService();

        WireEvents();

        // Initialize HWND before starting TaskbarAnchorService
        _overlayWindow.Show();
        _anchorService.Start();
        _anchorService.SetEnabled(_monitorService.Settings.ShowTaskbarOverlay);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _monitorService.Start();
    }

    /// <summary>
    /// Each release lives in its own versioned folder, so a "Start with Windows" entry written by an older
    /// version points at an old exe. Re-point it at the exe that is running now. Skipped in Debug builds
    /// so `dotnet run` never hijacks the installed copy's startup entry.
    /// </summary>
    private static void RepairStartupEntry()
    {
#if !DEBUG
        try
        {
            if (StartupService.IsEnabled())
                StartupService.SetEnabled(true);
        }
        catch
        {
            // Registry not writable: keep the existing entry.
        }
#endif
    }

    private void WireEvents()
    {
        _monitorService.DevicesUpdated += _ => RunOnUi(RenderAll);
        _monitorService.NotificationTriggered += (_, type, message) =>
            RunOnUi(() => _trayIconService.ShowBalloon(GetNotificationTitle(type), message, GetNotificationIcon(type)));

        _overlayWindow.LayoutUpdatedOnce += () => _anchorService.Reposition(force: true);
        _overlayWindow.LeftClicked += ToggleFlyout;
        _overlayWindow.RightClicked += () => _trayIconService.ShowContextMenuAtCursor();
        _overlayWindow.DeviceChangeDetected += ScheduleDebouncedRefresh;

        // Tray menu
        _trayIconService.OpenFlyoutRequested += ToggleFlyout;
        _trayIconService.RefreshRequested += RequestRefresh;
        _trayIconService.ToggleOverlayRequested += SetOverlayEnabled;
        _trayIconService.ToggleColorCodeRequested += enabled => Update(s => s.ColorCodeBatteryLevel = enabled);
        _trayIconService.ToggleStartupRequested += SetStartupEnabled;
        _trayIconService.ExitRequested += Shutdown;

        // Flyout
        _flyoutWindow.IsVisibleChanged += (_, _) =>
        {
            if (!_flyoutWindow.IsVisible)
                _anchorService.Reposition(force: true);
        };
        _flyoutWindow.RefreshRequested += RequestRefresh;
        _flyoutWindow.OverlayToggled += SetOverlayEnabled;
        _flyoutWindow.StartupToggled += SetStartupEnabled;
        _flyoutWindow.LowAlertToggled += enabled => Update(s => s.EnableLowBatteryNotification = enabled);
        _flyoutWindow.FullAlertToggled += enabled => Update(s => s.EnableFullBatteryNotification = enabled);
        _flyoutWindow.ChargingAlertToggled += enabled => Update(s => s.EnableChargingStateNotification = enabled);
        _flyoutWindow.ColorCodeToggled += enabled => Update(s => s.ColorCodeBatteryLevel = enabled);
        _flyoutWindow.LowThresholdChanged += threshold => Update(s => s.LowBatteryThreshold = threshold);
        _flyoutWindow.TaskbarLayoutChanged += layout => Update(s => s.TaskbarLayout = layout);
        _flyoutWindow.ToggleVisibilityRequested += id => Update(s =>
        {
            if (!s.HiddenDeviceIds.Remove(id))
                s.HiddenDeviceIds.Add(id);
        });
        _flyoutWindow.CycleCategoryRequested += id =>
        {
            var current = _monitorService.Devices.FirstOrDefault(d => d.Id == id)?.Category ?? DeviceCategory.Other;
            var values = Enum.GetValues<DeviceCategory>();
            var next = values[(Array.IndexOf(values, current) + 1) % values.Length];
            Update(s => s.CategoryOverrides[id] = next);
        };
    }

    /// <summary>Persists a settings change; the monitor re-applies it and raises DevicesUpdated → RenderAll.</summary>
    private void Update(Action<AppSettings> mutate) => _monitorService.UpdateSettings(mutate);

    private void RequestRefresh() => _ = _monitorService.RefreshAsync();

    private void SetOverlayEnabled(bool enabled)
    {
        _anchorService.SetEnabled(enabled);
        Update(s => s.ShowTaskbarOverlay = enabled);
    }

    private void SetStartupEnabled(bool enabled)
    {
        StartupService.SetEnabled(enabled);
        Update(s => s.LaunchAtStartup = enabled);
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    private void ScheduleDebouncedRefresh()
    {
        _deviceChangeDebounce?.Dispose();
        _deviceChangeDebounce = new System.Threading.Timer(_ => RequestRefresh(), null, 300, Timeout.Infinite);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => RunOnUi(RenderAll);

    /// <summary>Refreshes the taskbar overlay, tray tooltip and (only while it is open) the flyout.</summary>
    private void RenderAll()
    {
        var settings = _monitorService.Settings;
        var allDevices = _monitorService.Devices;
        bool isLightTaskbar = WindowsThemeHelper.IsTaskbarLightMode();

        var taskbarItems = _monitorService.GetTaskbarDevices()
            .Select(d => DeviceDisplayItem.FromModel(d, isLightTaskbar, settings))
            .ToList();

        _overlayWindow.UpdateDevices(taskbarItems, isLightTaskbar);
        _anchorService.SetHasContent(taskbarItems.Count > 0);
        _anchorService.Reposition(force: true);

        bool startupEnabled = StartupService.IsEnabled();
        _trayIconService.UpdateMenuState(settings, startupEnabled, allDevices);

        if (_flyoutWindow.IsVisible)
            BindFlyout(startupEnabled);
    }

    private void BindFlyout(bool startupEnabled)
    {
        bool isLightApps = WindowsThemeHelper.IsAppsLightMode();
        var items = _monitorService.Devices
            .Select(d => DeviceDisplayItem.FromModel(d, isLightApps, _monitorService.Settings))
            .ToList();
        _flyoutWindow.BindState(items, _monitorService.Settings, startupEnabled, isLightApps);
    }

    private static string GetNotificationTitle(NotificationEventType type) => type switch
    {
        NotificationEventType.LowBattery => "Low battery",
        NotificationEventType.FullBattery => "Fully charged",
        NotificationEventType.ChargingStarted => "Charging started",
        NotificationEventType.ChargingStopped => "Charger unplugged",
        _ => "Battery Hub"
    };

    private static System.Windows.Forms.ToolTipIcon GetNotificationIcon(NotificationEventType type) =>
        type == NotificationEventType.LowBattery
            ? System.Windows.Forms.ToolTipIcon.Warning
            : System.Windows.Forms.ToolTipIcon.Info;

    private void ToggleFlyout()
    {
        if (_flyoutWindow.IsVisible)
        {
            _flyoutWindow.Hide();
            return;
        }

        BindFlyout(StartupService.IsEnabled());
        _flyoutWindow.Show();
        _flyoutWindow.PositionAboveWorkArea();
        _flyoutWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _deviceChangeDebounce?.Dispose();
        _anchorService?.Dispose();
        _trayIconService?.Dispose();
        _monitorService?.Dispose();
        if (_ownsMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
