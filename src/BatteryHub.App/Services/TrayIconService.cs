using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using BatteryHub.App.Localization;
using BatteryHub.App.Views;
using BatteryHub.Core.Models;

namespace BatteryHub.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _gameModeItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripSeparator _updateSeparator;
    private readonly ToolStripMenuItem _exitItem;
    private string? _updateVersion;

    public event Action? OpenFlyoutRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action<bool>? ToggleOverlayRequested;
    public event Action<bool>? ToggleGameModeRequested;
    public event Action<bool>? ToggleStartupRequested;
    public event Action? UpdateRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _trayIcon = CreateTrayIcon();

        _headerItem = new ToolStripMenuItem { Enabled = false };
        _openItem = new ToolStripMenuItem(string.Empty, null, (_, _) => OpenFlyoutRequested?.Invoke());
        _refreshItem = new ToolStripMenuItem(string.Empty, null, (_, _) => RefreshRequested?.Invoke());
        _settingsItem = new ToolStripMenuItem(string.Empty, null, (_, _) => SettingsRequested?.Invoke());

        _overlayItem = new ToolStripMenuItem { CheckOnClick = true };
        _overlayItem.Click += (_, _) => ToggleOverlayRequested?.Invoke(_overlayItem.Checked);
        _gameModeItem = new ToolStripMenuItem { CheckOnClick = true };
        _gameModeItem.Click += (_, _) => ToggleGameModeRequested?.Invoke(_gameModeItem.Checked);
        _startupItem = new ToolStripMenuItem { CheckOnClick = true };
        _startupItem.Click += (_, _) => ToggleStartupRequested?.Invoke(_startupItem.Checked);

        _updateItem = new ToolStripMenuItem(string.Empty, null, (_, _) => UpdateRequested?.Invoke()) { Visible = false };
        _updateSeparator = new ToolStripSeparator { Visible = false };
        _exitItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ExitRequested?.Invoke());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _headerItem, new ToolStripSeparator(),
            _openItem, _refreshItem, _settingsItem, new ToolStripSeparator(),
            _overlayItem, _gameModeItem, _startupItem,
            _updateSeparator, _updateItem,
            new ToolStripSeparator(), _exitItem
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Battery Hub",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenFlyoutRequested?.Invoke();
        };

        ApplyLanguage();
    }

    public void ApplyLanguage()
    {
        _headerItem.Text = $"Battery Hub {AppInfo.VersionLabel}";
        _openItem.Text = Loc.T("TrayOpen");
        _refreshItem.Text = Loc.T("Refresh");
        _settingsItem.Text = Loc.T("TraySettings");
        _overlayItem.Text = Loc.T("ShowOnTaskbar");
        _gameModeItem.Text = Loc.T("TrayGameMode");
        _startupItem.Text = Loc.T("Startup");
        _exitItem.Text = Loc.T("TrayExit");
        if (_updateVersion is not null)
            _updateItem.Text = Loc.T("TrayUpdate", _updateVersion);
    }

    public void UpdateMenuState(AppSettings settings, bool isStartupEnabled, bool gameModeManual, IReadOnlyList<BluetoothDeviceBattery> devices)
    {
        _overlayItem.Checked = settings.ShowTaskbarOverlay;
        _gameModeItem.Checked = gameModeManual;
        _startupItem.Checked = isStartupEnabled;

        var connected = devices.Where(d => d.IsConnected && !d.IsHidden).ToList();
        string tooltip = connected.Count == 0
            ? Loc.T("TrayNoDevices")
            : string.Join("\n", connected.Take(4).Select(d => $"{d.EffectiveName} {d.BatteryPercent}%{(d.IsCharging ? " ⚡" : "")}"));

        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..124] + "..." : tooltip;
    }

    /// <summary>Shows or hides the "Update to vX" menu entry.</summary>
    public void SetUpdateAvailable(string? version)
    {
        _updateVersion = version;
        _updateItem.Visible = _updateSeparator.Visible = version is not null;
        if (version is not null)
            _updateItem.Text = Loc.T("TrayUpdate", version);
    }

    public void ShowContextMenuAtCursor() => _notifyIcon.ContextMenuStrip?.Show(Cursor.Position);

    private static Icon CreateTrayIcon()
    {
        // Multi-size app icon embedded from Assets/BatteryHub.ico; pick the frame matching the tray size for the current DPI.
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BatteryHub.ico")
                           ?? throw new InvalidOperationException("Embedded BatteryHub.ico not found.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
    }
}
