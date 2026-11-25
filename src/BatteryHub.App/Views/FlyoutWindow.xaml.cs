using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BatteryHub.App.Services;
using BatteryHub.App.ViewModels;
using BatteryHub.Core.Models;

namespace BatteryHub.App.Views;

public partial class FlyoutWindow : Window
{
    public event Action? RefreshRequested;
    public event Action<string>? ToggleVisibilityRequested;
    public event Action<string>? CycleCategoryRequested;
    public event Action<bool>? OverlayToggled;
    public event Action<bool>? StartupToggled;
    public event Action<bool>? LowAlertToggled;
    public event Action<bool>? FullAlertToggled;
    public event Action<bool>? ChargingAlertToggled;
    public event Action<bool>? ColorCodeToggled;
    public event Action<int>? LowThresholdChanged;
    public event Action<TaskbarLayout>? TaskbarLayoutChanged;

    private bool _suppressEvents;
    private bool? _appliedLightTheme;
    private System.Windows.Media.Color _appliedAccent;
    private string? _devicesSignature;

    public FlyoutWindow()
    {
        InitializeComponent();
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver is not null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : string.Empty;
    }

    public void BindState(
        IReadOnlyList<DeviceDisplayItem> items,
        AppSettings settings,
        bool isStartupEnabled,
        bool isLightTheme)
    {
        _suppressEvents = true;
        try
        {
            ApplyTheme(isLightTheme);

            // The monitor refreshes every second; only rebuild the list when something visible changed
            // so hover state and in-flight clicks aren't reset.
            string signature = string.Join("|", items.Select(i =>
                $"{i.Id}:{i.EffectiveName}:{i.Category}:{i.BatteryPercent}:{i.IsCharging}:{i.IsConnected}:{i.IsHidden}:{(i.AccentBrush as SolidColorBrush)?.Color}"));
            if (signature != _devicesSignature)
            {
                _devicesSignature = signature;
                DevicesList.ItemsSource = items;
            }
            EmptyStatePanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            OverlayCheckBox.IsChecked = settings.ShowTaskbarOverlay;
            StartupCheckBox.IsChecked = isStartupEnabled;
            LowBatteryAlertCheck.IsChecked = settings.EnableLowBatteryNotification;
            FullBatteryAlertCheck.IsChecked = settings.EnableFullBatteryNotification;
            ChargingStateAlertCheck.IsChecked = settings.EnableChargingStateNotification;
            ColorCodeCheckBox.IsChecked = settings.ColorCodeBatteryLevel;

            if (!LowThresholdSlider.IsMouseCaptureWithin)
                LowThresholdSlider.Value = settings.LowBatteryThreshold;
            UpdateThresholdLabels(settings.LowBatteryThreshold);

            LayoutPercentRadio.IsChecked = settings.TaskbarLayout == TaskbarLayout.Percentage;
            LayoutBatteryRadio.IsChecked = settings.TaskbarLayout == TaskbarLayout.Battery;
            LayoutBothRadio.IsChecked = settings.TaskbarLayout == TaskbarLayout.BatteryAndPercentage;
            UpdatePreviews(settings, isLightTheme);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void PositionAboveWorkArea()
    {
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : 340;
        Left = workArea.Right - w - 2;
        Top = workArea.Bottom - h - 2;
    }

    private void ShowSettingsView(bool show)
    {
        DevicesPanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        VersionText.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        LogoImage.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = show ? "Settings" : "Battery Hub";

        if (IsVisible)
            PositionAboveWorkArea();
    }

    private static readonly BluetoothDeviceBattery PreviewDevice = new()
    {
        Id = "preview",
        Name = "Mouse",
        Category = DeviceCategory.Mouse,
        BatteryPercent = 72,
        IsConnected = true
    };

    /// <summary>Renders each layout option with the same template the taskbar uses.</summary>
    private void UpdatePreviews(AppSettings settings, bool isLightTheme)
    {
        PreviewPercent.Content = DeviceDisplayItem.FromModel(PreviewDevice, isLightTheme, settings, TaskbarLayout.Percentage);
        PreviewBattery.Content = DeviceDisplayItem.FromModel(PreviewDevice, isLightTheme, settings, TaskbarLayout.Battery);
        PreviewBoth.Content = DeviceDisplayItem.FromModel(PreviewDevice, isLightTheme, settings, TaskbarLayout.BatteryAndPercentage);
    }

    private void UpdateThresholdLabels(int threshold)
    {
        LowThresholdText.Text = $"{threshold}%";
        ColorCodeCaption.Text = $"Green > 50% · Yellow {threshold}–50% · Red < {threshold}%";
    }

    private void ApplyTheme(bool light)
    {
        var accent = WindowsThemeHelper.GetAccentColor(light);
        if (_appliedLightTheme == light && _appliedAccent == accent)
            return;

        _appliedLightTheme = light;
        _appliedAccent = accent;

        if (light)
        {
            SetBrush("BgBrush", 0xFF, 0xF3, 0xF3, 0xF3);
            SetBrush("StrokeBrush", 0x1A, 0x00, 0x00, 0x00);
            SetBrush("CardBrush", 0xFF, 0xFB, 0xFB, 0xFB);
            SetBrush("DividerBrush", 0x0F, 0x00, 0x00, 0x00);
            SetBrush("TextBrush", 0xFF, 0x1B, 0x1B, 0x1B);
            SetBrush("SubTextBrush", 0xFF, 0x5F, 0x5F, 0x5F);
            SetBrush("ControlBrush", 0xB3, 0xFF, 0xFF, 0xFF);
            SetBrush("ControlHoverBrush", 0x0D, 0x00, 0x00, 0x00);
            SetBrush("ControlPressedBrush", 0x08, 0x00, 0x00, 0x00);
            SetBrush("ToggleOffBrush", 0xFF, 0x86, 0x86, 0x86);
            SetBrush("TrackBrush", 0x29, 0x00, 0x00, 0x00);
            SetBrush("TileBrush", 0x0A, 0x00, 0x00, 0x00);
            SetBrush("ThumbBrush", 0xFF, 0xFF, 0xFF, 0xFF);
            SetBrush("PreviewBgBrush", 0xFF, 0xEE, 0xEE, 0xEE);
        }
        else
        {
            SetBrush("BgBrush", 0xFF, 0x2B, 0x2B, 0x2B);
            SetBrush("StrokeBrush", 0x1F, 0xFF, 0xFF, 0xFF);
            SetBrush("CardBrush", 0xFF, 0x33, 0x33, 0x33);
            SetBrush("DividerBrush", 0x14, 0xFF, 0xFF, 0xFF);
            SetBrush("TextBrush", 0xFF, 0xFF, 0xFF, 0xFF);
            SetBrush("SubTextBrush", 0xFF, 0xAB, 0xAB, 0xAB);
            SetBrush("ControlBrush", 0x0F, 0xFF, 0xFF, 0xFF);
            SetBrush("ControlHoverBrush", 0x18, 0xFF, 0xFF, 0xFF);
            SetBrush("ControlPressedBrush", 0x0A, 0xFF, 0xFF, 0xFF);
            SetBrush("ToggleOffBrush", 0xFF, 0xC5, 0xC5, 0xC5);
            SetBrush("TrackBrush", 0x33, 0xFF, 0xFF, 0xFF);
            SetBrush("TileBrush", 0x12, 0xFF, 0xFF, 0xFF);
            SetBrush("ThumbBrush", 0xFF, 0x45, 0x45, 0x45);
            SetBrush("PreviewBgBrush", 0xFF, 0x1C, 0x1C, 0x1C);
        }

        SetBrush("SystemAccent", 0xFF, accent.R, accent.G, accent.B);
        var onAccent = WindowsThemeHelper.GetOnAccentColor(accent);
        SetBrush("OnAccentBrush", 0xFF, onAccent.R, onAccent.G, onAccent.B);
    }

    private void SetBrush(string key, byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        Resources[key] = brush;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        Hide();
        ShowSettingsView(false);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettingsView(true);

    private void OnBackClick(object sender, RoutedEventArgs e) => ShowSettingsView(false);

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

    private void OnOverlayCheckChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressEvents)
            OverlayToggled?.Invoke(OverlayCheckBox.IsChecked == true);
    }

    private void OnStartupCheckChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressEvents)
            StartupToggled?.Invoke(StartupCheckBox.IsChecked == true);
    }

    private void OnLowBatteryAlertCheckChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressEvents)
            LowAlertToggled?.Invoke(LowBatteryAlertCheck.IsChecked == true);
    }

    private void OnFullBatteryAlertCheckChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressEvents)
            FullAlertToggled?.Invoke(FullBatteryAlertCheck.IsChecked == true);
    }

    private void OnChargingStateAlertCheckChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressEvents)
            ChargingAlertToggled?.Invoke(ChargingStateAlertCheck.IsChecked == true);
    }

    private void OnColorCodeCheckChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressEvents)
            ColorCodeToggled?.Invoke(ColorCodeCheckBox.IsChecked == true);
    }

    private void OnLowThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || LowThresholdText is null)
            return;

        int val = (int)Math.Round(e.NewValue);
        UpdateThresholdLabels(val);
        LowThresholdChanged?.Invoke(val);
    }

    private void OnTaskbarLayoutChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;

        var layout = sender == LayoutBatteryRadio ? TaskbarLayout.Battery
                   : sender == LayoutBothRadio ? TaskbarLayout.BatteryAndPercentage
                   : TaskbarLayout.Percentage;
        TaskbarLayoutChanged?.Invoke(layout);
    }
}
