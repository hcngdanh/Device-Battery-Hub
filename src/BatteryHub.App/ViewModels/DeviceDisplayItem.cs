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
    /// <summary>Larger battery glyph with the number drawn inside the fill.</summary>
    public Visibility LabeledBatteryVisibility { get; init; } = Visibility.Collapsed;
    public Visibility ChargingVisibility => IsCharging ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Bolt after the glyph/text: used when the small glyph (which carries its own bolt) isn't shown.</summary>
    public Visibility InlineChargingVisibility =>
        IsCharging && BatteryIconVisibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

    // Battery glyph
    public double BatteryFillWidth => FillWidth(BatteryFillMaxWidth);

    /// <summary>Inner width (DIP) of the labeled battery glyph's fill area.</summary>
    public const int LabeledFillMaxWidth = 24;
    public double LabeledFillWidth => FillWidth(LabeledFillMaxWidth);
    public string LevelText => BatteryPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>Text color for the part of the number that sits on the fill (contrasts with the fill color).</summary>
    public System.Windows.Media.Brush FillTextBrush { get; init; } = System.Windows.Media.Brushes.Black;

    private double FillWidth(int max) => BatteryPercent <= 0
        ? 0
        : Math.Max(1, Math.Round(Math.Min(BatteryPercent, 100) / 100.0 * max));
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

    public bool ConnectionInferred { get; init; }

    private string StateLabel => Loc.T(
        IsCharging ? "StatusCharging"
        : IsConnected ? "StatusConnected"
        : ConnectionInferred ? "StatusAsleep" // silent 2.4GHz device: asleep or switched off
        : "StatusDisconnected");

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
        System.Windows.Media.Color normalColor = isLightTaskbar
            ? System.Windows.Media.Color.FromRgb(26, 26, 26)
            : System.Windows.Media.Color.FromRgb(236, 236, 236);

        // Windows' own charging green, independent of the user's level colours.
        System.Windows.Media.Color chargingGreen = isLightTaskbar
            ? System.Windows.Media.Color.FromRgb(22, 138, 70)
            : System.Windows.Media.Color.FromRgb(82, 196, 125);

        // Level colour (user-defined thresholds and colours) only tints the battery glyph fill
        // and the flyout bar; the device icon and percentage text always stay neutral.
        var levelColor = settings.ColorCodeBatteryLevel ? LevelColor(model.BatteryPercent, settings) : normalColor;

        // Windows-style: the battery fills green while charging, with a white bolt on top.
        // In light mode the white bolt gets a dark outline so it stays visible over the empty part.
        var fillColor = model.IsCharging ? chargingGreen : levelColor;
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
            ConnectionInferred = model.ConnectionInferred,
            IconGeometry = DeviceIconPaths.GetGeometry(model.Category),
            ForegroundBrush = Frozen(normalColor),
            AccentBrush = Frozen(levelColor),
            ChargingBrush = Frozen(normalColor),
            BatteryFillBrush = Frozen(fillColor),
            BoltBrush = Frozen(Colors.White),
            BoltStrokeBrush = Frozen(boltStroke),
            FillTextBrush = Frozen(ContrastText(fillColor)),
            BatteryIconVisibility = layout == TaskbarLayout.Battery ? Visibility.Visible : Visibility.Collapsed,
            LabeledBatteryVisibility = layout == TaskbarLayout.BatteryAndPercentage ? Visibility.Visible : Visibility.Collapsed,
            PercentageVisibility = layout == TaskbarLayout.Percentage ? Visibility.Visible : Visibility.Collapsed
        };
    }

    /// <summary>Near-black on light fills, white on dark fills (relative luminance threshold).</summary>
    private static System.Windows.Media.Color ContrastText(System.Windows.Media.Color fill)
    {
        double luminance = (0.2126 * fill.R + 0.7152 * fill.G + 0.0722 * fill.B) / 255.0;
        return luminance > 0.45 ? System.Windows.Media.Color.FromRgb(20, 20, 20) : Colors.White;
    }

    /// <summary>High above <c>LevelHighThreshold</c>, low below <c>LevelLowThreshold</c>, mid in between.</summary>
    public static System.Windows.Media.Color LevelColor(int percent, AppSettings settings) =>
        ParseColor(percent > settings.LevelHighThreshold ? settings.LevelHighColor
                 : percent < settings.LevelLowThreshold ? settings.LevelLowColor
                 : settings.LevelMidColor);

    public static System.Windows.Media.Color ParseColor(string hex)
    {
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    private static SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
