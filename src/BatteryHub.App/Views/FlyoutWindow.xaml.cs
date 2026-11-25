using System.Reflection;
using System.Windows;
using System.Windows.Media;
using BatteryHub.App.ViewModels;

namespace BatteryHub.App.Views;

/// <summary>Compact device list shown from the taskbar / tray. Settings live in <see cref="SettingsWindow"/>.</summary>
public partial class FlyoutWindow : Window
{
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action<string>? ToggleVisibilityRequested;
    public event Action<string>? CycleCategoryRequested;

    private string? _devicesSignature;

    public FlyoutWindow()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.VersionLabel;
    }

    public void BindState(IReadOnlyList<DeviceDisplayItem> items, bool gameModeActive)
    {
        // Rebuild the list only when something visible changed, so hover state isn't reset.
        string signature = string.Join("|", items.Select(i =>
            $"{i.Id}:{i.EffectiveName}:{i.Category}:{i.BatteryPercent}:{i.IsCharging}:{i.IsConnected}:{i.IsHidden}:{i.StatusText}:{(i.AccentBrush as SolidColorBrush)?.Color}"));
        if (signature != _devicesSignature)
        {
            _devicesSignature = signature;
            DevicesList.ItemsSource = items;
        }

        EmptyStatePanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GameModeChip.Visibility = gameModeActive ? Visibility.Visible : Visibility.Collapsed;
    }

    public void PositionAboveWorkArea(bool alignLeft = false)
    {
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : 340;
        Left = alignLeft ? workArea.Left + 2 : workArea.Right - w - 2;
        Top = workArea.Bottom - h - 2;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Hide();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke();
    }

    private void OnCycleCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
            CycleCategoryRequested?.Invoke(id);
    }

    private void OnToggleVisibilityClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
            ToggleVisibilityRequested?.Invoke(id);
    }
}

public static class AppInfo
{
    public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string VersionString => $"{Version.Major}.{Version.Minor}.{Version.Build}";
    public static string VersionLabel => "v" + VersionString;
    public const string RepoOwner = "hcngdanh";
    public const string RepoName = "Device-Battery-Hub";
    public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";
}
