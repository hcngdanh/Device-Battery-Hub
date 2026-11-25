using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using BatteryHub.Core.Models;

namespace BatteryHub.App.Localization;

/// <summary>
/// Runtime-switchable UI strings. XAML binds through <c>{l:Loc Key}</c>; code uses <see cref="T(string)"/>.
/// Changing <see cref="Language"/> raises <c>Item[]</c> so every binding refreshes in place.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private AppLanguage _language = AppLanguage.English;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
                return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke();
        }
    }

    public CultureInfo Culture => _language == AppLanguage.Vietnamese ? new CultureInfo("vi-VN") : new CultureInfo("en-US");

    public string this[string key] => T(key);

    public static string T(string key)
    {
        var table = Instance._language == AppLanguage.Vietnamese ? Strings.Vi : Strings.En;
        return table.TryGetValue(key, out var value) ? value
             : Strings.En.TryGetValue(key, out var fallback) ? fallback
             : key;
    }

    public static string T(string key, params object[] args) =>
        string.Format(Instance.Culture, T(key), args);
}

/// <summary><c>Text="{l:Loc Settings}"</c> — a live binding to <see cref="Loc"/>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new System.Windows.Data.Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

internal static class Strings
{
    public static readonly Dictionary<string, string> En = new()
    {
        // Shell
        ["AppName"] = "Battery Hub",
        ["Refresh"] = "Refresh now",
        ["Settings"] = "Settings",
        ["Back"] = "Back",
        ["NoDevicesTitle"] = "No battery devices connected",
        ["NoDevicesDesc"] = "Turn on your Bluetooth device or plug in your 2.4GHz receiver.",
        ["ChangeIcon"] = "Change device icon",
        ["ShowOnTaskbarAction"] = "Show on taskbar",
        ["HideFromTaskbarAction"] = "Hide from taskbar",
        ["StatusConnected"] = "Connected",
        ["StatusDisconnected"] = "Disconnected",
        ["StatusCharging"] = "Charging",
        ["SourceReceiver"] = "2.4GHz receiver",
        ["GameModeActive"] = "Game mode",

        // Tray
        ["TrayOpen"] = "Open Battery Hub",
        ["TraySettings"] = "Settings…",
        ["TrayGameMode"] = "Game mode",
        ["TrayUpdate"] = "Update to {0}",
        ["TrayExit"] = "Exit",
        ["TrayNoDevices"] = "Battery Hub (no connected devices)",

        // Navigation
        ["NavGeneral"] = "General",
        ["NavTaskbar"] = "Taskbar",
        ["NavNotifications"] = "Notifications",
        ["NavGameMode"] = "Game mode",
        ["NavHistory"] = "Battery history",
        ["NavAbout"] = "About & updates",

        // General
        ["Language"] = "Language",
        ["LanguageDesc"] = "Display language for Battery Hub",
        ["Theme"] = "Theme",
        ["ThemeDesc"] = "Applies to Battery Hub windows",
        ["ThemeSystem"] = "Use Windows setting",
        ["ThemeLight"] = "Light",
        ["ThemeDark"] = "Dark",
        ["Startup"] = "Start with Windows",
        ["StartupDesc"] = "Launch Battery Hub when you sign in",
        ["SettingsFile"] = "Settings file",
        ["SettingsFileDesc"] = "Back up your settings or move them to another PC",
        ["SettingsFilePortable"] = "Portable mode — settings are saved next to the app",
        ["Export"] = "Export…",
        ["Import"] = "Import…",
        ["Reset"] = "Restore defaults",
        ["ResetDesc"] = "Reset every setting to its default value",
        ["ResetButton"] = "Reset",
        ["ResetConfirm"] = "Reset all settings to their defaults?",
        ["ImportFailed"] = "This file is not a valid Battery Hub settings file.",
        ["ImportDone"] = "Settings imported.",
        ["ExportDone"] = "Settings exported.",
        ["SettingsFileFilter"] = "Battery Hub settings (*.json)|*.json",

        // Taskbar
        ["ShowOnTaskbar"] = "Show on taskbar",
        ["ShowOnTaskbarDesc"] = "Battery levels next to the system tray",
        ["TaskbarDisplay"] = "Taskbar display",
        ["TaskbarDisplayDesc"] = "The device icon is always shown",
        ["LayoutPercent"] = "Percentage",
        ["LayoutBattery"] = "Battery",
        ["LayoutBoth"] = "Battery + %",
        ["ColorCode"] = "Color by battery level",
        ["ColorCodeDesc"] = "Tints the battery fill: above {0}%, {1}–{0}%, below {1}%",
        ["ColorCodeNeedsGlyph"] = "Only for display styles with a battery icon",
        ["LevelHigh"] = "High",
        ["LevelHighDesc"] = "Above {0}%",
        ["LevelMid"] = "Medium",
        ["LevelMidDesc"] = "{0}–{1}%",
        ["LevelLow"] = "Low",
        ["LevelLowDesc"] = "Below {0}%",
        ["LevelResetColors"] = "Reset colours",

        // Notifications
        ["RespectFocus"] = "Respect Do not disturb",
        ["RespectFocusDesc"] = "Stay quiet while Windows Do not disturb or Focus is on",
        ["Sound"] = "Sound",
        ["SoundDesc"] = "Play a sound with notifications",
        ["SoundChoice"] = "Notification sound",
        ["SoundDefault"] = "Windows default",
        ["SoundNotify"] = "Notify",
        ["SoundChimes"] = "Chimes",
        ["SoundDing"] = "Ding",
        ["SoundAlarm"] = "Alarm",
        ["Preview"] = "Play",
        ["LowBattery"] = "Low battery",
        ["LowBatteryDesc"] = "Notify once when a device drops to the threshold",
        ["Threshold"] = "Threshold",
        ["CriticalBattery"] = "Very low battery",
        ["CriticalBatteryDesc"] = "Keep reminding until the device is charged",
        ["RepeatEvery"] = "Repeat every",
        ["MinutesFmt"] = "{0} min",
        ["FullCharge"] = "Fully charged",
        ["FullChargeDesc"] = "Notify when charging reaches the target",
        ["FullTarget"] = "Notify at",
        ["ChargerState"] = "Charger plugged in / unplugged",
        ["Connection"] = "Device connected / disconnected",
        ["Reminder"] = "End-of-day charge reminder",
        ["ReminderDesc"] = "Once a day, list devices that need charging",
        ["ReminderTime"] = "Remind at",
        ["ReminderBelow"] = "Devices at or below",

        // Game mode
        ["GameAuto"] = "Turn on automatically",
        ["GameAutoDesc"] = "When a game or another app is running full screen",
        ["GameNotif"] = "Notifications in game mode",
        ["GameNotifDesc"] = "Compact shows a small alert that doesn't steal focus",
        ["GameNotifCompact"] = "Compact",
        ["GameNotifOff"] = "Off",
        ["OverlayGroup"] = "In-game overlay",
        ["Overlay"] = "Show battery overlay",
        ["OverlayDesc"] = "A small battery readout on top of your game while game mode is on",
        ["Hotkey"] = "Show / hide shortcut",
        ["HotkeyDesc"] = "Click, then press the keys. Esc cancels, Backspace clears.",
        ["HotkeyPress"] = "Press keys…",
        ["HotkeyNone"] = "None",
        ["HotkeyInUse"] = "That shortcut is used by another app",
        ["Corner"] = "Position",
        ["Opacity"] = "Opacity",
        ["OverlayDevices"] = "Devices shown",
        ["OverlayDevicesDesc"] = "Nothing selected = all connected devices",

        // History
        ["HistoryRecord"] = "Record battery history",
        ["HistoryRecordDesc"] = "Stored only on this PC",
        ["HistoryKeep"] = "Keep history for",
        ["DaysFmt"] = "{0} days",
        ["HistoryChart"] = "Battery level",
        ["Range24h"] = "24 hours",
        ["Range7d"] = "7 days",
        ["Range30d"] = "30 days",
        ["HistoryEmpty"] = "No history yet. Turn on recording and check back later.",
        ["HistoryClear"] = "Clear history",
        ["HistoryClearConfirm"] = "Delete all recorded battery history?",
        ["OpenFolder"] = "Open folder",

        // About
        ["VersionFmt"] = "Version {0}",
        ["AutoUpdate"] = "Check for updates automatically",
        ["AutoUpdateDesc"] = "Checks at startup and once a day",
        ["CheckNow"] = "Check now",
        ["UpdateChecking"] = "Checking…",
        ["UpToDate"] = "You're up to date",
        ["UpdateAvailable"] = "Version {0} is available",
        ["UpdateInstall"] = "Update & restart",
        ["UpdateDownloading"] = "Downloading… {0}%",
        ["UpdateFailed"] = "Couldn't check for updates",
        ["UpdateInstallFailed"] = "Update failed: {0}",
        ["UpdateDevBuild"] = "Updates install only in the published app",
        ["Author"] = "Made by Danny Ho",
        ["TaskbarPosition"] = "Position",
        ["TaskbarPositionDesc"] = "Where the readout sits on the taskbar",
        ["PositionLeft"] = "Far left",
        ["PositionTray"] = "Next to tray",
        ["License"] = "MIT License",

        // Notifications
        ["N_LowTitle"] = "Low battery",
        ["N_LowBody"] = "{0} is at {1}%. Charge it soon.",
        ["N_CriticalTitle"] = "Battery very low",
        ["N_CriticalBody"] = "{0} is at {1}%. Charge it now.",
        ["N_FullTitle"] = "Charged",
        ["N_FullBody"] = "{0} reached {1}%.",
        ["N_ChargingTitle"] = "Charging",
        ["N_ChargingBody"] = "{0} is charging ({1}%).",
        ["N_UnpluggedTitle"] = "Charger unplugged",
        ["N_UnpluggedBody"] = "{0} was unplugged at {1}%.",
        ["N_ConnectedTitle"] = "Connected",
        ["N_ConnectedBody"] = "{0} connected ({1}%).",
        ["N_DisconnectedTitle"] = "Disconnected",
        ["N_DisconnectedBody"] = "{0} disconnected.",
        ["N_ReminderTitle"] = "Time to charge",
        ["N_ReminderBody"] = "Charge before tomorrow: {0}",
        ["N_UpdateTitle"] = "Update available",
        ["N_UpdateBody"] = "Battery Hub {0} is ready to install.",
    };

    public static readonly Dictionary<string, string> Vi = new()
    {
        ["AppName"] = "Battery Hub",
        ["Refresh"] = "Làm mới",
        ["Settings"] = "Cài đặt",
        ["Back"] = "Quay lại",
        ["NoDevicesTitle"] = "Chưa có thiết bị nào có pin",
        ["NoDevicesDesc"] = "Hãy bật thiết bị Bluetooth hoặc cắm receiver 2.4GHz.",
        ["ChangeIcon"] = "Đổi biểu tượng thiết bị",
        ["ShowOnTaskbarAction"] = "Hiện trên taskbar",
        ["HideFromTaskbarAction"] = "Ẩn khỏi taskbar",
        ["StatusConnected"] = "Đã kết nối",
        ["StatusDisconnected"] = "Đã ngắt kết nối",
        ["StatusCharging"] = "Đang sạc",
        ["SourceReceiver"] = "Receiver 2.4GHz",
        ["GameModeActive"] = "Chế độ chơi game",

        ["TrayOpen"] = "Mở Battery Hub",
        ["TraySettings"] = "Cài đặt…",
        ["TrayGameMode"] = "Chế độ chơi game",
        ["TrayUpdate"] = "Cập nhật lên {0}",
        ["TrayExit"] = "Thoát",
        ["TrayNoDevices"] = "Battery Hub (chưa có thiết bị kết nối)",

        ["NavGeneral"] = "Chung",
        ["NavTaskbar"] = "Taskbar",
        ["NavNotifications"] = "Thông báo",
        ["NavGameMode"] = "Chế độ chơi game",
        ["NavHistory"] = "Lịch sử pin",
        ["NavAbout"] = "Giới thiệu & cập nhật",

        ["Language"] = "Ngôn ngữ",
        ["LanguageDesc"] = "Ngôn ngữ hiển thị của Battery Hub",
        ["Theme"] = "Giao diện",
        ["ThemeDesc"] = "Áp dụng cho các cửa sổ của Battery Hub",
        ["ThemeSystem"] = "Theo Windows",
        ["ThemeLight"] = "Sáng",
        ["ThemeDark"] = "Tối",
        ["Startup"] = "Khởi động cùng Windows",
        ["StartupDesc"] = "Tự mở Battery Hub khi đăng nhập",
        ["SettingsFile"] = "Tệp cấu hình",
        ["SettingsFileDesc"] = "Sao lưu cài đặt hoặc chuyển sang máy khác",
        ["SettingsFilePortable"] = "Chế độ portable — cài đặt được lưu cạnh file chạy",
        ["Export"] = "Xuất…",
        ["Import"] = "Nhập…",
        ["Reset"] = "Khôi phục mặc định",
        ["ResetDesc"] = "Đưa mọi cài đặt về giá trị ban đầu",
        ["ResetButton"] = "Khôi phục",
        ["ResetConfirm"] = "Khôi phục tất cả cài đặt về mặc định?",
        ["ImportFailed"] = "Tệp này không phải tệp cấu hình Battery Hub hợp lệ.",
        ["ImportDone"] = "Đã nhập cài đặt.",
        ["ExportDone"] = "Đã xuất cài đặt.",
        ["SettingsFileFilter"] = "Cấu hình Battery Hub (*.json)|*.json",

        ["ShowOnTaskbar"] = "Hiện trên taskbar",
        ["ShowOnTaskbarDesc"] = "Hiện mức pin cạnh khay hệ thống",
        ["TaskbarDisplay"] = "Kiểu hiển thị",
        ["TaskbarDisplayDesc"] = "Biểu tượng thiết bị luôn được hiện",
        ["LayoutPercent"] = "Phần trăm",
        ["LayoutBattery"] = "Cục pin",
        ["LayoutBoth"] = "Pin + %",
        ["ColorCode"] = "Tô màu theo mức pin",
        ["ColorCodeDesc"] = "Tô màu ruột pin: trên {0}%, {1}–{0}%, dưới {1}%",
        ["ColorCodeNeedsGlyph"] = "Chỉ dùng cho kiểu hiển thị có cục pin",
        ["LevelHigh"] = "Cao",
        ["LevelHighDesc"] = "Trên {0}%",
        ["LevelMid"] = "Trung bình",
        ["LevelMidDesc"] = "{0}–{1}%",
        ["LevelLow"] = "Thấp",
        ["LevelLowDesc"] = "Dưới {0}%",
        ["LevelResetColors"] = "Đặt lại màu",

        ["RespectFocus"] = "Tôn trọng chế độ Không làm phiền",
        ["RespectFocusDesc"] = "Không làm phiền khi Windows bật Do not disturb hoặc Focus",
        ["Sound"] = "Âm thanh",
        ["SoundDesc"] = "Phát âm thanh kèm thông báo",
        ["SoundChoice"] = "Âm báo",
        ["SoundDefault"] = "Mặc định của Windows",
        ["SoundNotify"] = "Thông báo",
        ["SoundChimes"] = "Chuông",
        ["SoundDing"] = "Ding",
        ["SoundAlarm"] = "Báo thức",
        ["Preview"] = "Nghe thử",
        ["LowBattery"] = "Pin yếu",
        ["LowBatteryDesc"] = "Thông báo một lần khi pin xuống tới ngưỡng",
        ["Threshold"] = "Ngưỡng",
        ["CriticalBattery"] = "Pin rất yếu",
        ["CriticalBatteryDesc"] = "Nhắc lại liên tục cho tới khi cắm sạc",
        ["RepeatEvery"] = "Lặp lại mỗi",
        ["MinutesFmt"] = "{0} phút",
        ["FullCharge"] = "Sạc đầy",
        ["FullChargeDesc"] = "Thông báo khi sạc đạt mức mong muốn",
        ["FullTarget"] = "Thông báo khi đạt",
        ["ChargerState"] = "Cắm / rút sạc",
        ["Connection"] = "Thiết bị kết nối / ngắt kết nối",
        ["Reminder"] = "Nhắc sạc cuối ngày",
        ["ReminderDesc"] = "Mỗi ngày một lần, liệt kê thiết bị cần sạc",
        ["ReminderTime"] = "Giờ nhắc",
        ["ReminderBelow"] = "Thiết bị có pin từ",

        ["GameAuto"] = "Tự bật",
        ["GameAutoDesc"] = "Khi có game hoặc ứng dụng chạy toàn màn hình",
        ["GameNotif"] = "Thông báo khi chơi game",
        ["GameNotifDesc"] = "Thu gọn: hiện cảnh báo nhỏ, không chiếm focus",
        ["GameNotifCompact"] = "Thu gọn",
        ["GameNotifOff"] = "Tắt",
        ["OverlayGroup"] = "Lớp phủ trong game",
        ["Overlay"] = "Hiện lớp phủ pin",
        ["OverlayDesc"] = "Hiện mức pin nhỏ trên màn hình game khi ở chế độ chơi game",
        ["Hotkey"] = "Phím tắt ẩn / hiện",
        ["HotkeyDesc"] = "Bấm vào rồi nhấn tổ hợp phím. Esc để hủy, Backspace để xóa.",
        ["HotkeyPress"] = "Nhấn phím…",
        ["HotkeyNone"] = "Không có",
        ["HotkeyInUse"] = "Phím tắt này đang được ứng dụng khác dùng",
        ["Corner"] = "Vị trí",
        ["Opacity"] = "Độ trong suốt",
        ["OverlayDevices"] = "Thiết bị hiển thị",
        ["OverlayDevicesDesc"] = "Không chọn = tất cả thiết bị đang kết nối",

        ["HistoryRecord"] = "Ghi lịch sử pin",
        ["HistoryRecordDesc"] = "Chỉ lưu trên máy này",
        ["HistoryKeep"] = "Thời gian lưu",
        ["DaysFmt"] = "{0} ngày",
        ["HistoryChart"] = "Mức pin",
        ["Range24h"] = "24 giờ",
        ["Range7d"] = "7 ngày",
        ["Range30d"] = "30 ngày",
        ["HistoryEmpty"] = "Chưa có lịch sử. Hãy bật ghi lịch sử và quay lại sau.",
        ["HistoryClear"] = "Xóa lịch sử",
        ["HistoryClearConfirm"] = "Xóa toàn bộ lịch sử pin đã ghi?",
        ["OpenFolder"] = "Mở thư mục",

        ["VersionFmt"] = "Phiên bản {0}",
        ["AutoUpdate"] = "Tự kiểm tra cập nhật",
        ["AutoUpdateDesc"] = "Kiểm tra khi khởi động và mỗi ngày một lần",
        ["CheckNow"] = "Kiểm tra ngay",
        ["UpdateChecking"] = "Đang kiểm tra…",
        ["UpToDate"] = "Bạn đang dùng bản mới nhất",
        ["UpdateAvailable"] = "Đã có bản {0}",
        ["UpdateInstall"] = "Cập nhật & khởi động lại",
        ["UpdateDownloading"] = "Đang tải… {0}%",
        ["UpdateFailed"] = "Không kiểm tra được cập nhật",
        ["UpdateInstallFailed"] = "Cập nhật thất bại: {0}",
        ["UpdateDevBuild"] = "Chỉ bản đã phát hành mới tự cài cập nhật được",
        ["Author"] = "Phát triển bởi Danny Ho",
        ["TaskbarPosition"] = "Vị trí",
        ["TaskbarPositionDesc"] = "Chỗ hiển thị mức pin trên taskbar",
        ["PositionLeft"] = "Bên trái cùng",
        ["PositionTray"] = "Cạnh khay hệ thống",
        ["License"] = "Giấy phép MIT",

        ["N_LowTitle"] = "Pin yếu",
        ["N_LowBody"] = "{0} còn {1}%. Hãy sạc sớm.",
        ["N_CriticalTitle"] = "Pin rất yếu",
        ["N_CriticalBody"] = "{0} chỉ còn {1}%. Hãy sạc ngay.",
        ["N_FullTitle"] = "Đã sạc đầy",
        ["N_FullBody"] = "{0} đã đạt {1}%.",
        ["N_ChargingTitle"] = "Đang sạc",
        ["N_ChargingBody"] = "{0} đang sạc ({1}%).",
        ["N_UnpluggedTitle"] = "Đã rút sạc",
        ["N_UnpluggedBody"] = "{0} đã rút sạc ở mức {1}%.",
        ["N_ConnectedTitle"] = "Đã kết nối",
        ["N_ConnectedBody"] = "{0} đã kết nối ({1}%).",
        ["N_DisconnectedTitle"] = "Đã ngắt kết nối",
        ["N_DisconnectedBody"] = "{0} đã ngắt kết nối.",
        ["N_ReminderTitle"] = "Đến giờ sạc",
        ["N_ReminderBody"] = "Nên sạc trước ngày mai: {0}",
        ["N_UpdateTitle"] = "Có bản cập nhật",
        ["N_UpdateBody"] = "Battery Hub {0} đã sẵn sàng để cài.",
    };
}
