using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using BatteryHub.Core.Models;

namespace BatteryHub.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _colorCodeItem;
    private readonly ToolStripMenuItem _startupItem;

    public event Action? OpenFlyoutRequested;
    public event Action? RefreshRequested;
    public event Action<bool>? ToggleOverlayRequested;
    public event Action<bool>? ToggleColorCodeRequested;
    public event Action<bool>? ToggleStartupRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _trayIcon = CreateBatteryTrayIcon();

        _overlayItem = new ToolStripMenuItem("Show on Taskbar") { CheckOnClick = true };
        _overlayItem.Click += (_, _) => ToggleOverlayRequested?.Invoke(_overlayItem.Checked);

        _colorCodeItem = new ToolStripMenuItem("Color-code Battery Level") { CheckOnClick = true };
        _colorCodeItem.Click += (_, _) => ToggleColorCodeRequested?.Invoke(_colorCodeItem.Checked);

        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startupItem.Click += (_, _) => ToggleStartupRequested?.Invoke(_startupItem.Checked);

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        string verLabel = ver is not null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v0.3.0";

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"Battery Hub {verLabel}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Battery Hub", null, (_, _) => OpenFlyoutRequested?.Invoke());
        menu.Items.Add("Refresh Now", null, (_, _) => RefreshRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_overlayItem);
        menu.Items.Add(_colorCodeItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

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
    }

    public void UpdateMenuState(AppSettings settings, bool isStartupEnabled, IReadOnlyList<BluetoothDeviceBattery> devices)
    {
        _overlayItem.Checked = settings.ShowTaskbarOverlay;
        _colorCodeItem.Checked = settings.ColorCodeBatteryLevel;
        _startupItem.Checked = isStartupEnabled;

        var connected = devices.Where(d => d.IsConnected).ToList();
        string tooltip = connected.Count == 0
            ? "Battery Hub (No connected battery devices)"
            : "Battery Hub: " + string.Join(", ", connected.Take(3).Select(d => $"{d.EffectiveName} {d.BatteryPercent}%{(d.IsCharging ? "⚡" : "")}"));

        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..60] + "..." : tooltip;
    }

    public void ShowContextMenuAtCursor()
    {
        _notifyIcon.ContextMenuStrip?.Show(Cursor.Position);
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3500, title, message, icon);
    }

    private static Icon CreateBatteryTrayIcon()
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
