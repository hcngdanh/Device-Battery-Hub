using BatteryHub.Core.Models;

namespace BatteryHub.Core.Services;

public static class DeviceClassifier
{
    public static DeviceCategory Classify(
        string? deviceName,
        uint? bluetoothClassOfDevice = null,
        IEnumerable<string>? pnpClasses = null)
    {
        // Prefer explicit name cues first because some multi-role / combo BT keyboards
        // report a pointing or generic Class of Device.
        var fromName = ClassifyFromName(deviceName);
        if (fromName != DeviceCategory.Other)
        {
            return fromName;
        }

        if (bluetoothClassOfDevice is uint cod && cod != 0)
        {
            var fromCod = ClassifyFromClassOfDevice(cod);
            if (fromCod != DeviceCategory.Other)
            {
                return fromCod;
            }
        }

        if (pnpClasses != null)
        {
            foreach (var cls in pnpClasses)
            {
                if (string.Equals(cls, "Keyboard", StringComparison.OrdinalIgnoreCase))
                    return DeviceCategory.Keyboard;
                if (string.Equals(cls, "Mouse", StringComparison.OrdinalIgnoreCase))
                    return DeviceCategory.Mouse;
                if (string.Equals(cls, "AudioEndpoint", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cls, "MEDIA", StringComparison.OrdinalIgnoreCase))
                    return DeviceCategory.Headphones;
            }
        }

        return DeviceCategory.Other;
    }

    /// <summary>
    /// Classifies using Bluetooth SIG Class of Device (CoD) bitfields.
    /// Major Device Class: bits 8..12. Minor Device Class: bits 2..7.
    /// </summary>
    public static DeviceCategory ClassifyFromClassOfDevice(uint cod)
    {
        uint majorClass = (cod >> 8) & 0x1F;
        uint minorClass = (cod >> 2) & 0x3F;

        switch (majorClass)
        {
            case 0x05: // Peripheral (Mouse / Keyboard / Gamepad)
            {
                uint pointingBits = (minorClass >> 4) & 0x03; // bits 6..7 of CoD
                uint subType = minorClass & 0x0F;             // bits 2..5 of CoD

                if (pointingBits == 0x02) return DeviceCategory.Mouse;
                if (pointingBits == 0x01) return DeviceCategory.Keyboard;
                if (subType is 0x01 or 0x02) return DeviceCategory.Gamepad;
                if (pointingBits == 0x03) return DeviceCategory.Keyboard; // Combo keyboard/pointing
                break;
            }
            case 0x04: // Audio / Video
            {
                return minorClass switch
                {
                    0x01 or 0x02 or 0x06 or 0x07 => DeviceCategory.Headphones, // Wearable headset, Hands-free, Headphones, Portable Audio
                    0x05 or 0x08 or 0x0A => DeviceCategory.Speaker,            // Loudspeaker, Car audio, HiFi audio
                    _ => DeviceCategory.Headphones
                };
            }
        }

        return DeviceCategory.Other;
    }

    public static DeviceCategory ClassifyFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DeviceCategory.Other;

        string n = name.Trim().ToLowerInvariant();

        if (ContainsAny(n,
            "mouse", "chuột", "mx master", "mx anywhere", "mx vertical", "mx ergo",
            "deathadder", "viper", "basilisk", "naga", "orochi",
            "g pro", "g304", "g305", "g502", "g703", "g903", "superlight",
            "magic mouse", "trackpad", "trackball", "lift ", "m650", "m720",
            "attack shark", "attackshark"))
        {
            return DeviceCategory.Mouse;
        }

        if (ContainsAny(n,
            "keyboard", "bàn phím", "keychron", "nuphy", "hhkb", "leopold",
            "magic keyboard", "mx keys", "mx mechanical", "k380", "k480", "k780",
            "blackwidow", "huntsman", "anne pro", "akko", "monsgeek", "rainy75", "cidoo",
            "-kb", "_kb", " kb", "kbd") ||
            n.Equals("kb", StringComparison.Ordinal))
        {
            return DeviceCategory.Keyboard;
        }

        if (ContainsAny(n,
            "xbox", "dualsense", "dualshock", "wireless controller", "gamepad", "joy-con", "pro controller", "8bitdo"))
        {
            return DeviceCategory.Gamepad;
        }

        if (ContainsAny(n,
            "speaker", "soundlink", "flip ", "charge ", "boom ", "marshall", "homepod", "sonos", "soundbar"))
        {
            return DeviceCategory.Speaker;
        }

        if (ContainsAny(n,
            "headphone", "headset", "earbud", "earphone", "tai nghe", "airpods",
            "wh-1000", "wf-1000", "linkbuds", "qc35", "qc45", "quietcomfort",
            "momentum", "beats", "galaxy buds", "freebuds", "jabra", "arctis", "barracuda", "kraken"))
        {
            return DeviceCategory.Headphones;
        }

        return DeviceCategory.Other;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
