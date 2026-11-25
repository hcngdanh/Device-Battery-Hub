using System.Windows.Media;
using BatteryHub.Core.Models;

namespace BatteryHub.App.Controls;

/// <summary>
/// Crisp 16x16 vector icon paths inspired by Windows 11 Fluent outline icons.
/// </summary>
public static class DeviceIconPaths
{
    // Minimalist over-ear headphones (matches screenshot style)
    private static readonly Geometry HeadphonesGeometry = Geometry.Parse(
        "M 3,9.5 V 8 A 5,5 0 0 1 13,8 V 9.5 " +
        "M 2.5,9.5 H 4.5 A 1,1 0 0 1 5.5,10.5 V 13 A 1,1 0 0 1 4.5,14 H 3.5 A 1.5,1.5 0 0 1 2,12.5 V 10 A 0.5,0.5 0 0 1 2.5,9.5 Z " +
        "M 11.5,9.5 H 13.5 A 0.5,0.5 0 0 1 14,10 V 12.5 A 1.5,1.5 0 0 1 12.5,14 H 11.5 A 1,1 0 0 1 10.5,13 V 10.5 A 1,1 0 0 1 11.5,9.5 Z");

    // Vertical pill-shaped mouse with scroll wheel line (matches screenshot style)
    private static readonly Geometry MouseGeometry = Geometry.Parse(
        "M 8,1.8 A 4.2,4.2 0 0 1 12.2,6 V 10 A 4.2,4.2 0 0 1 3.8,10 V 6 A 4.2,4.2 0 0 1 8,1.8 Z " +
        "M 8,3.6 V 6.2");

    // Compact minimalist keyboard outline
    private static readonly Geometry KeyboardGeometry = Geometry.Parse(
        "M 1.5,4 H 14.5 A 1,1 0 0 1 15.5,5 V 11 A 1,1 0 0 1 14.5,12 H 1.5 A 1,1 0 0 1 0.5,11 V 5 A 1,1 0 0 1 1.5,4 Z " +
        "M 3.5,6.8 H 4.7 M 6.4,6.8 H 7.6 M 9.3,6.8 H 10.5 M 11.8,6.8 H 12.6 " +
        "M 5.2,9.5 H 10.8");

    // Minimalist gamepad
    private static readonly Geometry GamepadGeometry = Geometry.Parse(
        "M 4.5,4.5 H 11.5 A 3,3 0 0 1 14.4,8.2 L 13.8,11.6 A 1.4,1.4 0 0 1 11.4,12.2 L 10,10.5 H 6 L 4.6,12.2 A 1.4,1.4 0 0 1 2.2,11.6 L 1.6,8.2 A 3,3 0 0 1 4.5,4.5 Z " +
        "M 4.5,6.6 V 8.8 M 3.4,7.7 H 5.6 M 10.8,7.2 H 11 M 12,8.2 H 12.2");

    // Minimalist speaker
    private static readonly Geometry SpeakerGeometry = Geometry.Parse(
        "M 4,2.5 H 12 A 1,1 0 0 1 13,3.5 V 12.5 A 1,1 0 0 1 12,13.5 H 4 A 1,1 0 0 1 3,12.5 V 3.5 A 1,1 0 0 1 4,2.5 Z " +
        "M 8,5 A 0.5,0.5 0 1 1 8,4 A 0.5,0.5 0 1 1 8,5 Z " +
        "M 8,7.2 A 2.3,2.3 0 1 1 7.99,7.2 Z");

    // Bluetooth symbol for Other/Generic
    private static readonly Geometry OtherGeometry = Geometry.Parse(
        "M 5,5 L 11,10.5 L 8,13.5 V 2.5 L 11,5.5 L 5,11");

    // Crisp lightning bolt for charging indicator
    public static readonly Geometry ChargingBoltGeometry = Geometry.Parse(
        "M 9.5,1.5 L 3.5,9 H 8 L 6.5,14.5 L 12.5,7 H 8 Z");

    static DeviceIconPaths()
    {
        HeadphonesGeometry.Freeze();
        MouseGeometry.Freeze();
        KeyboardGeometry.Freeze();
        GamepadGeometry.Freeze();
        SpeakerGeometry.Freeze();
        OtherGeometry.Freeze();
        ChargingBoltGeometry.Freeze();
    }

    public static Geometry GetGeometry(DeviceCategory category) => category switch
    {
        DeviceCategory.Headphones => HeadphonesGeometry,
        DeviceCategory.Mouse => MouseGeometry,
        DeviceCategory.Keyboard => KeyboardGeometry,
        DeviceCategory.Gamepad => GamepadGeometry,
        DeviceCategory.Speaker => SpeakerGeometry,
        _ => OtherGeometry
    };
}
