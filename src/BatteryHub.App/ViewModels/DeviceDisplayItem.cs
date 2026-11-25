using System.Windows;
using System.Windows.Media;
using BatteryHub.App.Controls;
using BatteryHub.App.Localization;
using BatteryHub.Core.Models;

namespace BatteryHub.App.ViewModels;

public sealed class DeviceDisplayItem
{
    /// <summary>Inner width (DIP) of the taskbar battery glyph's fill area. Integer sizes keep the glyph pixel-aligned.</summary>
    public const int BatteryFillMaxWidth = 16;

    public required string Id { get; init; }
    public required string RawName { get; init; }
    public required string EffectiveName { get; init; }
    public required DeviceCategory Category { get; init; }
    public required int BatteryPercent { get; init; }
    public required bool IsConnected { get; init; }
    public required bool IsCharging { get; init; }
    public required bool IsHidden { get; init; }
    public required BatterySource Source { get; init; }
    public required Geometry IconGeometry { get; init; }
    public Geometry ChargingBoltGeometry => DeviceIconPaths.ChargingBoltGeometry;

    /// <summary>Level color (green / yellow / red) or neutral, used for the device icon and the percentage.</summary>
    public required System.Windows.Media.Brush ForegroundBrush { get; init; }
    public required System.Windows.Media.Brush AccentBrush { get; init; }
    /// <summary>Neutral taskbar text color (battery outline, inline charging bolt).</summary>
    public required System.Windows.Media.Brush ChargingBrush { get; init; }

    // Taskbar layout (device icon is always shown)
    public Visibility PercentageVisibility { get; init; } = Visibility.Visible;
    public Visibility BatteryIconVisibility { get; init; } = Visibility.Collapsed;
    public Visibility ChargingVisibility => IsCharging ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InlineChargingVisibility =>
        IsCharging && BatteryIconVisibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

    // Battery glyph
    public double BatteryFillWidth => BatteryPercent <= 0
        ? 0
        : Math.Max(1, Math.Round(Math.Min(BatteryPercent, 100) / 100.0 * BatteryFillMaxWidth));
    /// <summary>Green while charging, otherwise the level color.</summary>
    public System.Windows.Media.Brush BatteryFillBrush { get; init; } = System.Windows.Media.Brushes.White;
    public System.Windows.Media.Brush BoltBrush { get; init; } = System.Windows.Media.Brushes.White;
    public System.Windows.Media.Brush BoltStrokeBrush { get; init; } = System.Windows.Media.Brushes.Black;

    public string PercentText => $"{BatteryPercent}%";
    public string TooltipText => $"{EffectiveName} — {BatteryPercent}% ({StateLabel})";
    public string StatusText => $"{StateLabel} · {SourceLabel}";
    public string VisibilityActionText => Loc.T(IsHidden ? "ShowOnTaskbarAction" : "HideFromTaskbarAction");
    public string VisibilityGlyph => IsHidden ? "" : ""; // Segoe Fluent Icons: View / Hide
    public double OpacityValue => IsConnected ? 1.0 : 0.55;

    private string StateLabel => Loc.T(IsCharging ? "StatusCharging" : IsConnected ? "StatusConnected" : "StatusDisconnected");

    private string SourceLabel => Source switch
    {
        BatterySource.WindowsPnP => "Windows PnP",
        BatterySource.BleGatt => "BLE GATT",
        BatterySource.UsbHid => Loc.T("SourceReceiver"),
        _ => "—"
    };

    public static DeviceDisplayItem FromModel(
        BluetoothDeviceBattery model,
        bool isLightTaskbar,
        AppSettings settings) =>
        FromModel(model, isLightTaskbar, settings, settings.TaskbarLayout);

    public static DeviceDisplayItem FromModel(
        BluetoothDeviceBattery model,
        bool isLightTaskbar,
        AppSettings settings,
        TaskbarLayout layout)
    {
        bool isLow = model.BatteryPercent <= settings.LowBatteryThreshold;

        System.Windows.Media.Color normalColor = isLightTaskbar
            ? System.Windows.Media.Color.FromRgb(26, 26, 26)
            : System.Windows.Media.Color.FromRgb(236, 236, 236);

        // Traffic-light level colors: Green > 50%, Yellow threshold–50%, Red < threshold
        System.Windows.Media.Color greenColor = isLightTaskbar
            ? System.Windows.Media.Color.FromRgb(22, 138, 70)
            : System.Windows.Media.Color.FromRgb(82, 196, 125);

        System.Windows.Media.Color yellowColor = isLightTaskbar
            ? System.Windows.Media.Color.FromRgb(180, 110, 10)
            : System.Windows.Media.Color.FromRgb(242, 180, 65);

        System.Windows.Media.Color redColor = isLightTaskbar
            ? System.Windows.Media.Color.FromRgb(210, 45, 30)
            : System.Windows.Media.Color.FromRgb(255, 90, 80);

        System.Windows.Media.Color chosenColor;
        if (settings.ColorCodeBatteryLevel)
        {
            if (model.BatteryPercent > 50)
                chosenColor = greenColor;
            else if (model.BatteryPercent >= settings.LowBatteryThreshold)
                chosenColor = yellowColor;
            else
                chosenColor = redColor;
        }
        else
        {
            chosenColor = isLow ? redColor : normalColor;
        }

        // Windows-style: the battery fills green while charging, with a white bolt on top.
        // In light mode the white bolt gets a dark outline so it stays visible over the empty part.
        var fillColor = model.IsCharging ? greenColor : chosenColor;
        var boltStroke = isLightTaskbar
            ? System.Windows.Media.Color.FromRgb(26, 26, 26)
            : System.Windows.Media.Color.FromRgb(28, 28, 28);

        return new DeviceDisplayItem
        {
            Id = model.Id,
            RawName = model.Name,
            EffectiveName = model.EffectiveName,
            Category = model.Category,
            BatteryPercent = model.BatteryPercent,
            IsConnected = model.IsConnected,
            IsCharging = model.IsCharging,
            IsHidden = model.IsHidden,
            Source = model.Source,
            IconGeometry = DeviceIconPaths.GetGeometry(model.Category),
            ForegroundBrush = Frozen(chosenColor),
            AccentBrush = Frozen(chosenColor),
            ChargingBrush = Frozen(normalColor),
            BatteryFillBrush = Frozen(fillColor),
            BoltBrush = Frozen(Colors.White),
            BoltStrokeBrush = Frozen(boltStroke),
            BatteryIconVisibility = layout != TaskbarLayout.Percentage ? Visibility.Visible : Visibility.Collapsed,
            PercentageVisibility = layout != TaskbarLayout.Battery ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private static SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
