using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BatteryHub.App.Localization;
using BatteryHub.App.Services;
using BatteryHub.App.ViewModels;
using BatteryHub.App.Views;
using BatteryHub.Core.Models;
using BatteryHub.Core.Services;
using Microsoft.Win32;

namespace BatteryHub.App;

public partial class App : System.Windows.Application, ISettingsHost
{
    private const string MutexName = "BatteryHub_SingleInstance_Mutex";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private SettingsService _settingsService = null!;
    private BatteryHistoryService _history = null!;
    private BatteryMonitorService _monitorService = null!;
    private TaskbarOverlayWindow _overlayWindow = null!;
    private TaskbarAnchorService _anchorService = null!;
    private FlyoutWindow _flyoutWindow = null!;
    private TrayIconService _trayIconService = null!;
    private NotificationPresenter _presenter = null!;
    private GameModeService _gameMode = null!;
    private HotkeyService _hotkeys = null!;
    private GameOverlayWindow? _gameOverlay;
    private SettingsWindow? _settingsWindow;
    private readonly UpdateService _updates = new();
    private DispatcherTimer? _updateTimer;
    private string? _announcedUpdate;
    private System.Threading.Timer? _deviceChangeDebounce;

    // Hotkey overrides for the game overlay (see UpdateGameOverlay).
    private bool _overlayHiddenByHotkey;
    private bool _overlayShownByHotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // After a self-update the new process starts while the old one is still exiting: wait for it.
        bool afterUpdate = e.Args.Any(a => string.Equals(a, "--updated", StringComparison.OrdinalIgnoreCase));
        _singleInstanceMutex = new Mutex(false, MutexName);
        try
        {
            _ownsMutex = _singleInstanceMutex.WaitOne(afterUpdate ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        if (e.Args.Any(a => string.Equals(a, "--log-hid", StringComparison.OrdinalIgnoreCase)))
            BatteryHub.Core.Services.Providers.AttackSharkHidBatteryProvider.EnableDiagnosticLog = true;

        UpdateService.CleanupPreviousVersion();
        RepairStartupEntry();

        _settingsService = new SettingsService();
        _history = new BatteryHistoryService(Path.Combine(_settingsService.DataDirectory, "history"));
        _monitorService = new BatteryMonitorService(_settingsService) { History = _history };

        Loc.Instance.Language = Settings.Language;
        ThemeManager.Apply(Settings.Theme);

        _overlayWindow = new TaskbarOverlayWindow();
        _anchorService = new TaskbarAnchorService(_overlayWindow);
        _flyoutWindow = new FlyoutWindow();
        _trayIconService = new TrayIconService();
        _gameMode = new GameModeService(() => Settings.GameModeAutoEnable);
        _presenter = new NotificationPresenter(() => Settings, () => _gameMode.IsActive);
        _hotkeys = new HotkeyService();
        _hotkeys.Register(Settings.GameOverlayHotkey);

        WireEvents();

        // Initialize HWND before starting TaskbarAnchorService
        _overlayWindow.Show();
        _anchorService.SetPosition(Settings.TaskbarPosition);
        _anchorService.Start();
        _anchorService.SetEnabled(Settings.ShowTaskbarOverlay);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _monitorService.Start();
        _gameMode.Start();
        StartUpdateChecks();
    }

    /// <summary>
    /// Each release lives in its own folder, so a "Start with Windows" entry written by an older
    /// version points at an old exe. Re-point it at the running exe. Skipped in Debug builds so
    /// `dotnet run` never hijacks the installed copy's startup entry.
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
        _monitorService.NotificationTriggered += n => RunOnUi(() => _presenter.Show(n));

        _overlayWindow.LayoutUpdatedOnce += () => _anchorService.Reposition(force: true);
        _overlayWindow.LeftClicked += () => ToggleFlyout(fromTaskbarItem: true);
        _overlayWindow.RightClicked += () => _trayIconService.ShowContextMenuAtCursor();
        _overlayWindow.DeviceChangeDetected += ScheduleDebouncedRefresh;

        _trayIconService.OpenFlyoutRequested += () => ToggleFlyout(fromTaskbarItem: false);
        _trayIconService.RefreshRequested += RequestRefresh;
        _trayIconService.SettingsRequested += () => OpenSettings();
        _trayIconService.ToggleOverlayRequested += enabled =>
        {
            Update(s => s.ShowTaskbarOverlay = enabled);
            _settingsWindow?.Refresh();
        };
        _trayIconService.ToggleGameModeRequested += _gameMode.SetManual;
        _trayIconService.ToggleStartupRequested += enabled =>
        {
            SetStartup(enabled);
            _settingsWindow?.Refresh();
        };
        _trayIconService.UpdateRequested += () => OpenSettings("About");
        _trayIconService.ExitRequested += Shutdown;

        _flyoutWindow.IsVisibleChanged += (_, _) =>
        {
            if (!_flyoutWindow.IsVisible)
                _anchorService.Reposition(force: true);
        };
        _flyoutWindow.RefreshRequested += RequestRefresh;
        _flyoutWindow.SettingsRequested += () => OpenSettings();
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

        _gameMode.Changed += active =>
        {
            if (!active)
                _overlayHiddenByHotkey = false;
            RenderAll();
        };
        _hotkeys.Pressed += ToggleOverlayByHotkey;

        Loc.Instance.LanguageChanged += () =>
        {
            _trayIconService.ApplyLanguage();
            RenderAll();
        };
        ThemeManager.Changed += () =>
        {
            RenderAll();
            _settingsWindow?.Refresh();
        };
    }

    // ------------------------------------------------------------------ ISettingsHost

    public AppSettings Settings => _monitorService.Settings;
    public IReadOnlyList<BluetoothDeviceBattery> Devices => _monitorService.Devices;
    public SettingsService SettingsService => _settingsService;
    public BatteryHistoryService History => _history;
    public UpdateService Updates => _updates;
    public bool IsStartupEnabled => StartupService.IsEnabled();

    /// <summary>Persists a settings change, then applies its side effects (language, theme, taskbar, game mode…).</summary>
    public void Update(Action<AppSettings> mutate)
    {
        _monitorService.UpdateSettings(mutate); // raises DevicesUpdated → RenderAll
        ApplySettingSideEffects();
    }

    public void ReplaceSettings(AppSettings settings)
    {
        try { StartupService.SetEnabled(settings.LaunchAtStartup); } catch { /* registry locked */ }
        _monitorService.UpdateSettings(s => s.CopyFrom(settings));
        _hotkeys.Register(Settings.GameOverlayHotkey);
        ApplySettingSideEffects();
        _settingsWindow?.Refresh();
    }

    public void SetStartup(bool enabled)
    {
        StartupService.SetEnabled(enabled);
        Update(s => s.LaunchAtStartup = enabled);
    }

    public bool ApplyHotkey(string hotkey) => _hotkeys.Register(hotkey);

    public async Task InstallUpdateAsync(UpdateInfo update, IProgress<int> progress)
    {
        await _updates.InstallAsync(update, progress);
        Shutdown();
    }

    private void ApplySettingSideEffects()
    {
        Loc.Instance.Language = Settings.Language;
        ThemeManager.Apply(Settings.Theme);
        _anchorService.SetPosition(Settings.TaskbarPosition);
        _anchorService.SetEnabled(Settings.ShowTaskbarOverlay);
        _gameMode.Evaluate();
        ConfigureUpdateTimer();
        RenderAll();
    }

    // ------------------------------------------------------------------ rendering

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    private void RequestRefresh() => _ = _monitorService.RefreshAsync();

    private void ScheduleDebouncedRefresh()
    {
        _deviceChangeDebounce?.Dispose();
        _deviceChangeDebounce = new System.Threading.Timer(_ => RequestRefresh(), null, 300, Timeout.Infinite);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => RunOnUi(() =>
    {
        ThemeManager.Apply(Settings.Theme); // Windows theme / accent may have changed
        RenderAll();
    });

    /// <summary>Taskbar readout, tray, game overlay and (only while open) the flyout.</summary>
    private void RenderAll()
    {
        var settings = Settings;
        var allDevices = Devices;
        bool isLightTaskbar = WindowsThemeHelper.IsTaskbarLightMode();

        var taskbarItems = _monitorService.GetTaskbarDevices()
            .Select(d => DeviceDisplayItem.FromModel(d, isLightTaskbar, settings))
            .ToList();

        _overlayWindow.UpdateDevices(taskbarItems, isLightTaskbar);
        _anchorService.SetHasContent(taskbarItems.Count > 0);
        _anchorService.Reposition(force: true);

        _trayIconService.UpdateMenuState(settings, IsStartupEnabled, _gameMode.IsManual, allDevices);
        UpdateGameOverlay();

        if (_flyoutWindow.IsVisible)
            BindFlyout();
    }

    private void BindFlyout()
    {
        var items = Devices
            .Select(d => DeviceDisplayItem.FromModel(d, ThemeManager.IsLight, Settings))
            .ToList();
        _flyoutWindow.BindState(items, _gameMode.IsActive);
    }

    private void ToggleFlyout(bool fromTaskbarItem)
    {
        if (_flyoutWindow.IsVisible)
        {
            _flyoutWindow.Hide();
            return;
        }

        BindFlyout();
        _flyoutWindow.Show();
        // Open next to whatever was clicked: the left-edge readout opens the flyout on the left.
        _flyoutWindow.PositionAboveWorkArea(alignLeft: fromTaskbarItem && Settings.TaskbarPosition == TaskbarPosition.Left);
        _flyoutWindow.Activate();
    }

    private void OpenSettings(string page = "General")
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.ShowPage(page);
        _settingsWindow.Activate();
    }

    // ------------------------------------------------------------------ game overlay

    private void ToggleOverlayByHotkey()
    {
        bool visible = _gameOverlay?.IsVisible == true;
        if (visible)
        {
            _overlayShownByHotkey = false;
            _overlayHiddenByHotkey = _gameMode.IsActive;
        }
        else
        {
            _overlayHiddenByHotkey = false;
            _overlayShownByHotkey = !(_gameMode.IsActive && Settings.GameOverlayEnabled);
        }
        UpdateGameOverlay();
    }

    private void UpdateGameOverlay()
    {
        var s = Settings;
        bool show = _overlayShownByHotkey || (_gameMode.IsActive && s.GameOverlayEnabled && !_overlayHiddenByHotkey);
        if (!show)
        {
            _gameOverlay?.Hide();
            return;
        }

        var items = Devices
            .Where(d => d.IsConnected && !d.IsHidden &&
                        (s.GameOverlayDeviceIds.Count == 0 || s.GameOverlayDeviceIds.Contains(d.Id)))
            .Select(d => DeviceDisplayItem.FromModel(d, isLightTaskbar: false, s))
            .ToList();

        _gameOverlay ??= new GameOverlayWindow();
        if (!_gameOverlay.IsVisible)
            _gameOverlay.Show();
        _gameOverlay.Update(items, s.GameOverlayCorner, s.GameOverlayOpacity);
    }

    // ------------------------------------------------------------------ updates

    private void StartUpdateChecks()
    {
        ConfigureUpdateTimer();
        if (Settings.AutoCheckUpdates)
        {
            var initial = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            initial.Tick += (_, _) =>
            {
                initial.Stop();
                _ = CheckForUpdatesAsync();
            };
            initial.Start();
        }
    }

    private void ConfigureUpdateTimer()
    {
        if (Settings.AutoCheckUpdates && _updateTimer is null)
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
            _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
            _updateTimer.Start();
        }
        else if (!Settings.AutoCheckUpdates && _updateTimer is not null)
        {
            _updateTimer.Stop();
            _updateTimer = null;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await _updates.CheckAsync();
            string? version = update?.Version.ToString(3);
            _trayIconService.SetUpdateAvailable(version);
            _settingsWindow?.UpdateUpdateRow();

            if (version is not null && version != _announcedUpdate)
            {
                _announcedUpdate = version;
                _presenter.ShowMessage(Loc.T("N_UpdateTitle"), Loc.T("N_UpdateBody", version), "", () => OpenSettings("About"));
            }
        }
        catch
        {
            // Offline or rate-limited: try again on the next tick.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _deviceChangeDebounce?.Dispose();
        _updateTimer?.Stop();
        _gameMode?.Dispose();
        _hotkeys?.Dispose();
        _anchorService?.Dispose();
        _trayIconService?.Dispose();
        _monitorService?.Dispose();
        if (_ownsMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
