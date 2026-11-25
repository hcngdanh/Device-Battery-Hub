using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BatteryHub.App.Localization;
using BatteryHub.App.Services;
using BatteryHub.App.ViewModels;
using BatteryHub.Core.Models;
using BatteryHub.Core.Services;

namespace BatteryHub.App.Views;

/// <summary>What the Settings window needs from the running app.</summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }
    IReadOnlyList<BluetoothDeviceBattery> Devices { get; }
    SettingsService SettingsService { get; }
    BatteryHistoryService History { get; }
    UpdateService Updates { get; }
    bool IsStartupEnabled { get; }

    void Update(Action<AppSettings> mutate);
    void ReplaceSettings(AppSettings settings);
    void SetStartup(bool enabled);
    /// <summary>Registers the overlay hotkey; false when another app owns it.</summary>
    bool ApplyHotkey(string hotkey);
    Task InstallUpdateAsync(UpdateInfo update, IProgress<int> progress);
}

public partial class SettingsWindow : Window
{
    private static readonly int[] FullTargets = [100, 95, 90, 85, 80];
    private static readonly int[] ReminderMinutes = [0, 15, 30, 45];

    private readonly ISettingsHost _host;
    private bool _suppress;
    private bool _capturingHotkey;

    public SettingsWindow(ISettingsHost host)
    {
        _host = host;
        InitializeComponent();

        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            typeof(SettingsWindow).Assembly.GetManifestResourceStream("BatteryHub.ico")!);
        NavVersion.Text = AppInfo.VersionLabel;
        AboutVersion.Text = Loc.T("VersionFmt", AppInfo.VersionString);

        for (int h = 0; h < 24; h++)
            ReminderHourCombo.Items.Add(new ComboBoxItem { Content = h.ToString("00") });
        foreach (int m in ReminderMinutes)
            ReminderMinuteCombo.Items.Add(new ComboBoxItem { Content = m.ToString("00") });

        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loc.Instance.LanguageChanged += OnLanguageSwitched;
        Closed += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageSwitched;

        BuildLocalizedItems();
        Refresh();
        UpdateUpdateRow();
    }

    public void ShowPage(string page)
    {
        var nav = page switch
        {
            "About" => NavAbout,
            "GameMode" => NavGameMode,
            "History" => NavHistory,
            "Notifications" => NavNotifications,
            "Taskbar" => NavTaskbar,
            _ => NavGeneral
        };
        nav.IsChecked = true;
    }

    /// <summary>Re-reads every control from the current settings (after import, reset, or tray changes).</summary>
    public void Refresh()
    {
        var s = _host.Settings;
        _suppress = true;
        try
        {
            LanguageCombo.SelectedIndex = s.Language == AppLanguage.Vietnamese ? 1 : 0;
            ThemeCombo.SelectedIndex = (int)s.Theme;
            StartupToggle.IsChecked = _host.IsStartupEnabled;
            SettingsFileRow.Description = _host.SettingsService.IsPortable ? Loc.T("SettingsFilePortable") : Loc.T("SettingsFileDesc");

            OverlayToggle.IsChecked = s.ShowTaskbarOverlay;
            PositionLeft.IsChecked = s.TaskbarPosition == TaskbarPosition.Left;
            PositionTray.IsChecked = s.TaskbarPosition == TaskbarPosition.NearTray;
            LayoutPercentRadio.IsChecked = s.TaskbarLayout == TaskbarLayout.Percentage;
            LayoutBatteryRadio.IsChecked = s.TaskbarLayout == TaskbarLayout.Battery;
            LayoutBothRadio.IsChecked = s.TaskbarLayout == TaskbarLayout.BatteryAndPercentage;
            ColorCodeToggle.IsChecked = s.ColorCodeBatteryLevel;
            ColorCodeRow.Description = Loc.T("ColorCodeDesc", s.LowBatteryThreshold);
            UpdatePreviews(s);

            FocusToggle.IsChecked = s.RespectFocusAssist;
            SoundToggle.IsChecked = s.EnableSound;
            SoundCombo.SelectedIndex = (int)s.Sound;
            SoundChoiceRow.IsEnabled = s.EnableSound;

            LowToggle.IsChecked = s.EnableLowBatteryNotification;
            LowSlider.Value = s.LowBatteryThreshold;
            LowValue.Text = $"{s.LowBatteryThreshold}%";
            LowThresholdRow.IsEnabled = s.EnableLowBatteryNotification;

            CriticalToggle.IsChecked = s.EnableCriticalBatteryNotification;
            CriticalSlider.Value = s.CriticalBatteryThreshold;
            CriticalValue.Text = $"{s.CriticalBatteryThreshold}%";
            RepeatCombo.SelectedIndex = Array.IndexOf(AppSettings.CriticalRepeatChoices, s.CriticalRepeatMinutes);
            CriticalThresholdRow.IsEnabled = CriticalRepeatRow.IsEnabled = s.EnableCriticalBatteryNotification;

            FullToggle.IsChecked = s.EnableFullBatteryNotification;
            int target = s.FullChargeMode == FullChargeMode.Full ? 100 : s.FullChargeThreshold;
            FullTargetCombo.SelectedIndex = Math.Max(0, Array.IndexOf(FullTargets, FullTargets.OrderBy(t => Math.Abs(t - target)).First()));
            FullTargetRow.IsEnabled = s.EnableFullBatteryNotification;

            ChargerToggle.IsChecked = s.EnableChargingStateNotification;
            ConnectionToggle.IsChecked = s.EnableConnectionNotification;

            ReminderToggle.IsChecked = s.EnableChargeReminder;
            ReminderHourCombo.SelectedIndex = s.ChargeReminderTime.Hours;
            ReminderMinuteCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ReminderMinutes, ReminderMinutes.OrderBy(m => Math.Abs(m - s.ChargeReminderTime.Minutes)).First()));
            ReminderSlider.Value = s.ChargeReminderThreshold;
            ReminderValue.Text = $"{s.ChargeReminderThreshold}%";
            ReminderTimeRow.IsEnabled = ReminderThresholdRow.IsEnabled = s.EnableChargeReminder;

            GameAutoToggle.IsChecked = s.GameModeAutoEnable;
            GameNotifCompact.IsChecked = s.GameModeNotifications == GameModeNotificationStyle.Compact;
            GameNotifOff.IsChecked = s.GameModeNotifications == GameModeNotificationStyle.Off;
            GameOverlayToggle.IsChecked = s.GameOverlayEnabled;
            if (!_capturingHotkey)
                HotkeyButton.Content = string.IsNullOrWhiteSpace(s.GameOverlayHotkey) ? Loc.T("HotkeyNone") : s.GameOverlayHotkey.Replace("+", " + ");
            CornerTL.IsChecked = s.GameOverlayCorner == OverlayCorner.TopLeft;
            CornerTR.IsChecked = s.GameOverlayCorner == OverlayCorner.TopRight;
            CornerBL.IsChecked = s.GameOverlayCorner == OverlayCorner.BottomLeft;
            CornerBR.IsChecked = s.GameOverlayCorner == OverlayCorner.BottomRight;
            OpacitySlider.Value = Math.Round(s.GameOverlayOpacity * 100);
            OpacityValue.Text = $"{Math.Round(s.GameOverlayOpacity * 100)}%";
            HotkeyRow.IsEnabled = CornerRow.IsEnabled = OpacityRow.IsEnabled = OverlayDevicesRow.IsEnabled = s.GameOverlayEnabled;
            BuildOverlayDeviceList(s);

            HistoryToggle.IsChecked = s.HistoryEnabled;
            Keep7.IsChecked = s.HistoryRetentionDays == 7;
            Keep30.IsChecked = s.HistoryRetentionDays == 30;
            Keep90.IsChecked = s.HistoryRetentionDays == 90;
            RetentionRow.IsEnabled = s.HistoryEnabled;

            AutoUpdateToggle.IsChecked = s.AutoCheckUpdates;
        }
        finally
        {
            _suppress = false;
        }
    }

    // ---------------------------------------------------------------- navigation

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (PageGeneral is null)
            return;

        PageGeneral.Visibility = Vis(NavGeneral.IsChecked == true);
        PageTaskbar.Visibility = Vis(NavTaskbar.IsChecked == true);
        PageNotifications.Visibility = Vis(NavNotifications.IsChecked == true);
        PageGameMode.Visibility = Vis(NavGameMode.IsChecked == true);
        PageHistory.Visibility = Vis(NavHistory.IsChecked == true);
        PageAbout.Visibility = Vis(NavAbout.IsChecked == true);
        PageScroller.ScrollToTop();

        if (NavHistory.IsChecked == true)
            LoadHistoryDevices();
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // ---------------------------------------------------------------- general

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var lang = LanguageCombo.SelectedIndex == 1 ? AppLanguage.Vietnamese : AppLanguage.English;
        _host.Update(s => s.Language = lang);
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var theme = (AppTheme)Math.Max(0, ThemeCombo.SelectedIndex);
        _host.Update(s => s.Theme = theme);
    }

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _host.SetStartup(StartupToggle.IsChecked == true);
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"BatteryHub-settings-{DateTime.Now:yyyyMMdd}.json",
            Filter = Loc.T("SettingsFileFilter")
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SettingsService.Export(_host.Settings, dialog.FileName);
            GeneralStatus.Text = Loc.T("ExportDone");
        }
        catch (Exception ex)
        {
            GeneralStatus.Text = ex.Message;
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("SettingsFileFilter") };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _host.ReplaceSettings(SettingsService.Import(dialog.FileName));
            GeneralStatus.Text = Loc.T("ImportDone");
        }
        catch (Exception)
        {
            GeneralStatus.Text = Loc.T("ImportFailed");
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this, Loc.T("ResetConfirm"), Loc.T("AppName"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _host.ReplaceSettings(SettingsService.CreateDefaults());
        GeneralStatus.Text = string.Empty;
    }

    // ---------------------------------------------------------------- generic handlers

    private void OnSimpleToggle(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not System.Windows.Controls.CheckBox box) return;
        bool on = box.IsChecked == true;

        Action<AppSettings>? mutate = box.Name switch
        {
            nameof(OverlayToggle) => s => s.ShowTaskbarOverlay = on,
            nameof(ColorCodeToggle) => s => s.ColorCodeBatteryLevel = on,
            nameof(FocusToggle) => s => s.RespectFocusAssist = on,
            nameof(SoundToggle) => s => s.EnableSound = on,
            nameof(LowToggle) => s => s.EnableLowBatteryNotification = on,
            nameof(CriticalToggle) => s => s.EnableCriticalBatteryNotification = on,
            nameof(FullToggle) => s => s.EnableFullBatteryNotification = on,
            nameof(ChargerToggle) => s => s.EnableChargingStateNotification = on,
            nameof(ConnectionToggle) => s => s.EnableConnectionNotification = on,
            nameof(ReminderToggle) => s => s.EnableChargeReminder = on,
            nameof(GameAutoToggle) => s => s.GameModeAutoEnable = on,
            nameof(GameOverlayToggle) => s => s.GameOverlayEnabled = on,
            nameof(HistoryToggle) => s => s.HistoryEnabled = on,
            nameof(AutoUpdateToggle) => s => s.AutoCheckUpdates = on,
            _ => null
        };
        if (mutate is not null)
            _host.Update(mutate);
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || !IsLoaded) return;
        int v = (int)Math.Round(e.NewValue);

        if (sender == LowSlider) { LowValue.Text = $"{v}%"; _host.Update(s => s.LowBatteryThreshold = v); }
        else if (sender == CriticalSlider) { CriticalValue.Text = $"{v}%"; _host.Update(s => s.CriticalBatteryThreshold = v); }
        else if (sender == ReminderSlider) { ReminderValue.Text = $"{v}%"; _host.Update(s => s.ChargeReminderThreshold = v); }
        else if (sender == OpacitySlider) { OpacityValue.Text = $"{v}%"; _host.Update(s => s.GameOverlayOpacity = v / 100.0); }
    }

    private void OnComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || !IsLoaded) return;

        if (sender == RepeatCombo && RepeatCombo.SelectedIndex >= 0)
        {
            int minutes = AppSettings.CriticalRepeatChoices[RepeatCombo.SelectedIndex];
            _host.Update(s => s.CriticalRepeatMinutes = minutes);
        }
        else if (sender == FullTargetCombo && FullTargetCombo.SelectedIndex >= 0)
        {
            int target = FullTargets[FullTargetCombo.SelectedIndex];
            _host.Update(s =>
            {
                s.FullChargeMode = target >= 100 ? FullChargeMode.Full : FullChargeMode.Threshold;
                if (target < 100)
                    s.FullChargeThreshold = target;
            });
        }
        else if ((sender == ReminderHourCombo || sender == ReminderMinuteCombo) &&
                 ReminderHourCombo.SelectedIndex >= 0 && ReminderMinuteCombo.SelectedIndex >= 0)
        {
            var time = new TimeSpan(ReminderHourCombo.SelectedIndex, ReminderMinutes[ReminderMinuteCombo.SelectedIndex], 0);
            _host.Update(s => s.ChargeReminderTime = time);
        }
    }

    private void OnSegmentChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || !IsLoaded) return;

        if (sender == GameNotifCompact) _host.Update(s => s.GameModeNotifications = GameModeNotificationStyle.Compact);
        else if (sender == GameNotifOff) _host.Update(s => s.GameModeNotifications = GameModeNotificationStyle.Off);
        else if (sender == CornerTL) _host.Update(s => s.GameOverlayCorner = OverlayCorner.TopLeft);
        else if (sender == CornerTR) _host.Update(s => s.GameOverlayCorner = OverlayCorner.TopRight);
        else if (sender == CornerBL) _host.Update(s => s.GameOverlayCorner = OverlayCorner.BottomLeft);
        else if (sender == CornerBR) _host.Update(s => s.GameOverlayCorner = OverlayCorner.BottomRight);
        else if (sender == PositionLeft) _host.Update(s => s.TaskbarPosition = TaskbarPosition.Left);
        else if (sender == PositionTray) _host.Update(s => s.TaskbarPosition = TaskbarPosition.NearTray);
        else if (sender == Keep7) _host.Update(s => s.HistoryRetentionDays = 7);
        else if (sender == Keep30) _host.Update(s => s.HistoryRetentionDays = 30);
        else if (sender == Keep90) _host.Update(s => s.HistoryRetentionDays = 90);
    }

    // ---------------------------------------------------------------- taskbar previews

    private static readonly BluetoothDeviceBattery PreviewDevice = new()
    {
        Id = "preview", Name = "Mouse", Category = DeviceCategory.Mouse, BatteryPercent = 72, IsConnected = true
    };

    private void UpdatePreviews(AppSettings s)
    {
        bool light = ThemeManager.IsLight;
        PreviewPercent.Content = DeviceDisplayItem.FromModel(PreviewDevice, light, s, TaskbarLayout.Percentage);
        PreviewBattery.Content = DeviceDisplayItem.FromModel(PreviewDevice, light, s, TaskbarLayout.Battery);
        PreviewBoth.Content = DeviceDisplayItem.FromModel(PreviewDevice, light, s, TaskbarLayout.BatteryAndPercentage);
    }

    private void OnLayoutChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || !IsLoaded) return;
        var layout = sender == LayoutBatteryRadio ? TaskbarLayout.Battery
                   : sender == LayoutBothRadio ? TaskbarLayout.BatteryAndPercentage
                   : TaskbarLayout.Percentage;
        _host.Update(s => s.TaskbarLayout = layout);
    }

    // ---------------------------------------------------------------- sound

    private void OnSoundChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || !IsLoaded || SoundCombo.SelectedIndex < 0) return;
        var sound = (NotificationSound)SoundCombo.SelectedIndex;
        _host.Update(s => s.Sound = sound);
        NotificationPresenter.PlaySound(sound);
    }

    private void OnPreviewSoundClick(object sender, RoutedEventArgs e) =>
        NotificationPresenter.PlaySound(_host.Settings.Sound);

    // ---------------------------------------------------------------- hotkey capture

    private void OnHotkeyClick(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyButton.Content = Loc.T("HotkeyPress");
        HotkeyButton.Focus();
        Keyboard.Focus(HotkeyButton);
    }

    private void OnHotkeyKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey)
            return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            EndHotkeyCapture();
            return;
        }
        if (key is Key.Back or Key.Delete)
        {
            _host.ApplyHotkey(string.Empty);
            _host.Update(s => s.GameOverlayHotkey = string.Empty);
            EndHotkeyCapture();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // wait for the non-modifier key

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
            return;

        string hotkey = HotkeyService.Format(modifiers, key);
        if (_host.ApplyHotkey(hotkey))
        {
            _host.Update(s => s.GameOverlayHotkey = hotkey);
            HotkeyRow.Description = Loc.T("HotkeyDesc");
        }
        else
        {
            _host.ApplyHotkey(_host.Settings.GameOverlayHotkey); // restore the previous one
            HotkeyRow.Description = Loc.T("HotkeyInUse");
        }
        EndHotkeyCapture();
    }

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
            EndHotkeyCapture();
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        Refresh();
    }

    // ---------------------------------------------------------------- overlay device list

    private void BuildOverlayDeviceList(AppSettings s)
    {
        OverlayDevicesPanel.Children.Clear();
        var devices = _host.Devices
            .Where(d => !d.IsHidden)
            .Select(d => (d.Id, d.EffectiveName))
            .Concat(s.GameOverlayDeviceIds.Where(id => _host.Devices.All(d => !d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).Select(id => (id, id)))
            .ToList();

        foreach (var (id, name) in devices)
        {
            var box = new System.Windows.Controls.CheckBox
            {
                Content = name,
                Tag = id,
                IsChecked = s.GameOverlayDeviceIds.Contains(id),
                Style = (Style)FindResource("SmallFluentCheck")
            };
            box.Click += (_, _) =>
            {
                bool on = box.IsChecked == true;
                _host.Update(x =>
                {
                    if (on) x.GameOverlayDeviceIds.Add(id);
                    else x.GameOverlayDeviceIds.Remove(id);
                });
            };
            OverlayDevicesPanel.Children.Add(box);
        }
    }

    // ---------------------------------------------------------------- history

    private string? _historyDeviceId;

    private void LoadHistoryDevices()
    {
        var known = _host.History.KnownDevices(DateTimeOffset.Now.AddDays(-90));
        var current = _historyDeviceId;
        HistoryDeviceCombo.Items.Clear();
        foreach (var (id, name) in known)
            HistoryDeviceCombo.Items.Add(new ComboBoxItem { Content = name, Tag = id });

        int index = known.ToList().FindIndex(k => k.Id == current);
        HistoryDeviceCombo.SelectedIndex = index >= 0 ? index : (known.Count > 0 ? 0 : -1);
        DrawHistory();
    }

    private void OnHistoryFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _historyDeviceId = (HistoryDeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        DrawHistory();
    }

    private void OnHistoryCanvasSizeChanged(object sender, SizeChangedEventArgs e) => DrawHistory();

    private void DrawHistory()
    {
        HistoryCanvas.Children.Clear();
        double w = HistoryCanvas.ActualWidth, h = HistoryCanvas.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var range = Range7d.IsChecked == true ? TimeSpan.FromDays(7)
                  : Range30d.IsChecked == true ? TimeSpan.FromDays(30)
                  : TimeSpan.FromHours(24);
        var to = DateTimeOffset.Now;
        var from = to - range;
        IReadOnlyList<BatterySample> samples = _historyDeviceId is null ? [] : _host.History.Read(_historyDeviceId, from);

        var grid = (System.Windows.Media.Brush)FindResource("DividerBrush");
        var sub = (System.Windows.Media.Brush)FindResource("SubTextBrush");
        var accent = (SolidColorBrush)FindResource("SystemAccent");
        const double left = 36, bottom = 20;
        double plotW = w - left - 4, plotH = h - bottom - 6;

        foreach (int pct in new[] { 0, 25, 50, 75, 100 })
        {
            double y = 6 + plotH * (1 - pct / 100.0);
            HistoryCanvas.Children.Add(new Line { X1 = left, X2 = w - 4, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
            var label = new TextBlock { Text = $"{pct}%", FontSize = 10.5, Foreground = sub };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 7);
            HistoryCanvas.Children.Add(label);
        }

        string fmt = range.TotalDays > 1.5 ? "dd/MM" : "HH:mm";
        for (int i = 0; i <= 4; i++)
        {
            var t = from + range * (i / 4.0);
            var label = new TextBlock { Text = t.ToLocalTime().ToString(fmt, Loc.Instance.Culture), FontSize = 10.5, Foreground = sub };
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = left + plotW * (i / 4.0) - label.DesiredSize.Width / 2;
            Canvas.SetLeft(label, Math.Clamp(x, left - 4, w - label.DesiredSize.Width));
            Canvas.SetTop(label, h - bottom + 4);
            HistoryCanvas.Children.Add(label);
        }

        HistoryEmptyText.Visibility = samples.Count < 2 ? Visibility.Visible : Visibility.Collapsed;
        if (samples.Count < 2)
            return;

        System.Windows.Point Map(BatterySample s) => new(
            left + plotW * Math.Clamp((s.Timestamp - from) / range, 0, 1),
            6 + plotH * (1 - s.Percent / 100.0));

        var line = new Polyline { Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        var area = new Polygon
        {
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, accent.Color.R, accent.Color.G, accent.Color.B))
        };
        var first = Map(samples[0]);
        area.Points.Add(new System.Windows.Point(first.X, 6 + plotH));
        foreach (var s in samples)
        {
            var p = Map(s);
            line.Points.Add(p);
            area.Points.Add(p);
        }
        area.Points.Add(new System.Windows.Point(line.Points[^1].X, 6 + plotH));
        HistoryCanvas.Children.Add(area);
        HistoryCanvas.Children.Add(line);
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this, Loc.T("HistoryClearConfirm"), Loc.T("AppName"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _host.History.Clear();
        LoadHistoryDevices();
    }

    private void OnOpenHistoryFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_host.History.Directory);
        Process.Start(new ProcessStartInfo(_host.History.Directory) { UseShellExecute = true });
    }

    // ---------------------------------------------------------------- updates

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateRow.Header = Loc.T("UpdateChecking");
        try
        {
            await _host.Updates.CheckAsync();
            UpdateUpdateRow();
        }
        catch
        {
            UpdateRow.Header = Loc.T("UpdateFailed");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_host.Updates.Available is not UpdateInfo update)
            return;

        InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = false;
        var progress = new Progress<int>(p => UpdateRow.Header = Loc.T("UpdateDownloading", p));
        try
        {
            await _host.InstallUpdateAsync(update, progress);
        }
        catch (Exception ex)
        {
            UpdateRow.Header = Loc.T("UpdateInstallFailed", ex.Message);
            InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = true;
        }
    }

    public void UpdateUpdateRow()
    {
        var available = _host.Updates.Available;
        UpdateRow.Header = available is null ? Loc.T("UpToDate") : Loc.T("UpdateAvailable", available.Version.ToString(3));
        UpdateRow.Description = available is not null && !UpdateService.CanInstall ? Loc.T("UpdateDevBuild") : null;
        InstallUpdateButton.Visibility = available is not null ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = UpdateService.CanInstall;
    }

    // ---------------------------------------------------------------- localization

    private void OnLanguageSwitched()
    {
        BuildLocalizedItems();
        AboutVersion.Text = Loc.T("VersionFmt", AppInfo.VersionString);
        HotkeyRow.Description = Loc.T("HotkeyDesc");
        UpdateUpdateRow();
        Refresh();
        DrawHistory();
    }

    private void BuildLocalizedItems()
    {
        _suppress = true;
        try
        {
            int repeat = RepeatCombo.SelectedIndex;
            RepeatCombo.Items.Clear();
            foreach (int m in AppSettings.CriticalRepeatChoices)
                RepeatCombo.Items.Add(new ComboBoxItem { Content = Loc.T("MinutesFmt", m) });
            RepeatCombo.SelectedIndex = repeat;

            Keep7.Content = Loc.T("DaysFmt", 7);
            Keep30.Content = Loc.T("DaysFmt", 30);
            Keep90.Content = Loc.T("DaysFmt", 90);
        }
        finally
        {
            _suppress = false;
        }
    }
}
