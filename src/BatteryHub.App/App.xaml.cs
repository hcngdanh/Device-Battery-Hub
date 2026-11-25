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
    private SettingsService? _settingsService;
    private BatteryMonitorService? _monitorService;
    private TaskbarOverlayWindow? _overlayWindow;
    private TaskbarAnchorService? _anchorService;
    private FlyoutWindow? _flyoutWindow;
    private TrayIconService? _trayIconService;
    private System.Threading.Timer? _deviceChangeDebounce;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "BatteryHub_SingleInstance_Mutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            Shutdown();
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--log-hid", StringComparison.OrdinalIgnoreCase)))
        {
            BatteryHub.Core.Services.Providers.AttackSharkHidBatteryProvider.EnableDiagnosticLog = true;
        }

        _settingsService = new SettingsService();
        _monitorService = new BatteryMonitorService(_settingsService);

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

    private void WireEvents()
    {
        if (_monitorService is null || _overlayWindow is null || _anchorService is null ||
            _flyoutWindow is null || _trayIconService is null)
            return;

        _monitorService.DevicesUpdated += devices => Dispatcher.Invoke(() => RenderAll(devices));
        _monitorService.NotificationTriggered += (_, type, message) =>
            Dispatcher.Invoke(() => _trayIconService.ShowBalloon(GetNotificationTitle(type), message, GetNotificationIcon(type)));

        _overlayWindow.LayoutUpdatedOnce += () => _anchorService.Reposition(force: true);
        _overlayWindow.LeftClicked += ToggleFlyout;
        _overlayWindow.RightClicked += () => _trayIconService.ShowContextMenuAtCursor();
        _overlayWindow.DeviceChangeDetected += ScheduleDebouncedRefresh;

        _trayIconService.OpenFlyoutRequested += ToggleFlyout;
        _trayIconService.RefreshRequested += () => _ = _monitorService.RefreshAsync();
        _trayIconService.ToggleOverlayRequested += enabled =>
        {
            _monitorService.UpdateSettings(s => s.ShowTaskbarOverlay = enabled, triggerRefresh: false);
            _anchorService.SetEnabled(enabled);
            RenderAll(_monitorService.Devices);
        };
        _trayIconService.ToggleColorCodeRequested += enabled =>
            _monitorService.UpdateSettings(s => s.ColorCodeBatteryLevel = enabled);
        _trayIconService.ToggleStartupRequested += enabled =>
        {
            StartupService.SetEnabled(enabled);
            _monitorService.UpdateSettings(s => s.LaunchAtStartup = enabled, triggerRefresh: false);
            RenderAll(_monitorService.Devices);
        };
        _trayIconService.ExitRequested += Shutdown;

        _flyoutWindow.IsVisibleChanged += (_, _) =>
        {
            if (!_flyoutWindow.IsVisible)
                _anchorService.Reposition(force: true);
        };
        _flyoutWindow.RefreshRequested += () => _ = _monitorService.RefreshAsync();
        _flyoutWindow.OverlayToggled += enabled =>
        {
            _monitorService.UpdateSettings(s => s.ShowTaskbarOverlay = enabled, triggerRefresh: false);
            _anchorService.SetEnabled(enabled);
            RenderAll(_monitorService.Devices);
        };
        _flyoutWindow.StartupToggled += enabled =>
        {
            StartupService.SetEnabled(enabled);
            _monitorService.UpdateSettings(s => s.LaunchAtStartup = enabled, triggerRefresh: false);
            RenderAll(_monitorService.Devices);
        };
        _flyoutWindow.LowAlertToggled += enabled =>
            _monitorService.UpdateSettings(s => s.EnableLowBatteryNotification = enabled, triggerRefresh: false);
        _flyoutWindow.FullAlertToggled += enabled =>
            _monitorService.UpdateSettings(s => s.EnableFullBatteryNotification = enabled, triggerRefresh: false);
        _flyoutWindow.ChargingAlertToggled += enabled =>
            _monitorService.UpdateSettings(s => s.EnableChargingStateNotification = enabled, triggerRefresh: false);
        _flyoutWindow.ColorCodeToggled += enabled =>
            _monitorService.UpdateSettings(s => s.ColorCodeBatteryLevel = enabled);
        _flyoutWindow.LowThresholdChanged += threshold =>
            _monitorService.UpdateSettings(s => s.LowBatteryThreshold = threshold);
        _flyoutWindow.TaskbarLayoutChanged += layout =>
            _monitorService.UpdateSettings(s => s.TaskbarLayout = layout);

        _flyoutWindow.ToggleVisibilityRequested += id =>
        {
            _monitorService.UpdateSettings(s =>
            {
                if (!s.HiddenDeviceIds.Remove(id))
                    s.HiddenDeviceIds.Add(id);
            });
        };

        _flyoutWindow.CycleCategoryRequested += id =>
        {
            var current = _monitorService.Devices.FirstOrDefault(d => d.Id == id)?.Category ?? DeviceCategory.Other;
            var values = Enum.GetValues<DeviceCategory>();
            int nextIndex = (Array.IndexOf(values, current) + 1) % values.Length;
            var next = values[nextIndex];
            _monitorService.UpdateSettings(s => s.CategoryOverrides[id] = next);
        };
    }

    private void ScheduleDebouncedRefresh()
    {
        _deviceChangeDebounce?.Dispose();
        _deviceChangeDebounce = new System.Threading.Timer(_ =>
        {
            if (_monitorService is not null)
                _ = _monitorService.RefreshAsync();
        }, null, 300, Timeout.Infinite);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_monitorService is not null)
                RenderAll(_monitorService.Devices);
        });
    }

    private void RenderAll(IReadOnlyList<BluetoothDeviceBattery> allDevices)
    {
        if (_monitorService is null || _overlayWindow is null || _anchorService is null ||
            _flyoutWindow is null || _trayIconService is null)
            return;

        var settings = _monitorService.Settings;
        bool isLightTaskbar = WindowsThemeHelper.IsTaskbarLightMode();
        bool isLightApps = WindowsThemeHelper.IsAppsLightMode();

        var taskbarModels = _monitorService.GetTaskbarDevices();
        var taskbarItems = taskbarModels
            .Select(d => DeviceDisplayItem.FromModel(d, isLightTaskbar, settings))
            .ToList();

        _overlayWindow.UpdateDevices(taskbarItems, isLightTaskbar);
        _anchorService.SetHasContent(taskbarItems.Count > 0);
        _anchorService.Reposition(force: true);

        var allItems = allDevices
            .Select(d => DeviceDisplayItem.FromModel(d, isLightApps, settings))
            .ToList();

        bool startupEnabled = StartupService.IsEnabled();
        _flyoutWindow.BindState(allItems, settings, startupEnabled, isLightApps);
        _trayIconService.UpdateMenuState(settings, startupEnabled, allDevices);
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
        if (_flyoutWindow is null || _monitorService is null)
            return;

        if (_flyoutWindow.IsVisible)
        {
            _flyoutWindow.Hide();
            return;
        }

        RenderAll(_monitorService.Devices);
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
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
