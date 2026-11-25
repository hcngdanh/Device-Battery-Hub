using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryHub.Core.Models;

namespace BatteryHub.Core.Services;

public sealed class SettingsService
{
    /// <summary>Placing this file next to the executable switches the app to portable mode.</summary>
    public const string PortableFileName = "BatteryHub.settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Enums are written as names (readable exports) but integers from older files still load.
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsPath { get; }
    public bool IsPortable { get; }

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            SettingsPath = customPath;
            return;
        }

        string portablePath = Path.Combine(AppContext.BaseDirectory, PortableFileName);
        if (File.Exists(portablePath))
        {
            SettingsPath = portablePath;
            IsPortable = true;
            return;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsPath = Path.Combine(appData, "BatteryHub", "settings.json");
    }

    /// <summary>Folder for app data that lives alongside the settings file (history, logs).</summary>
    public string DataDirectory => Path.GetDirectoryName(SettingsPath) ?? AppContext.BaseDirectory;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return CreateDefaults();

            return Parse(File.ReadAllText(SettingsPath)) ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        string? dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    public static void Export(AppSettings settings, string path)
    {
        var copy = settings.Clone();
        Normalize(copy);
        File.WriteAllText(path, JsonSerializer.Serialize(copy, JsonOptions));
    }

    /// <summary>Reads a settings file exported earlier. Throws <see cref="InvalidDataException"/> if it is not one.</summary>
    public static AppSettings Import(string path)
    {
        AppSettings? parsed;
        try
        {
            parsed = Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Not a valid Battery Hub settings file.", ex);
        }

        return parsed ?? throw new InvalidDataException("Not a valid Battery Hub settings file.");
    }

    /// <summary>Factory defaults; the language follows the Windows display language.</summary>
    public static AppSettings CreateDefaults() => new()
    {
        Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi"
            ? AppLanguage.Vietnamese
            : AppLanguage.English
    };

    private static AppSettings? Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        if (settings is null)
            return null;
        Normalize(settings);
        return settings;
    }

    public static void Normalize(AppSettings s)
    {
        s.LowBatteryThreshold = Math.Clamp(s.LowBatteryThreshold, 5, 50);
        s.CriticalBatteryThreshold = Math.Clamp(s.CriticalBatteryThreshold, 3, 30);
        s.CriticalRepeatMinutes = Snap(s.CriticalRepeatMinutes, AppSettings.CriticalRepeatChoices);
        s.FullChargeThreshold = Math.Clamp(s.FullChargeThreshold, 50, 100);
        s.ChargeReminderThreshold = Math.Clamp(s.ChargeReminderThreshold, 10, 90);
        if (s.ChargeReminderTime < TimeSpan.Zero || s.ChargeReminderTime >= TimeSpan.FromDays(1))
            s.ChargeReminderTime = new TimeSpan(21, 0, 0);
        s.MaxTaskbarDevices = Math.Clamp(s.MaxTaskbarDevices, 1, 8);
        s.GameOverlayOpacity = Math.Clamp(s.GameOverlayOpacity, 0.3, 1.0);
        s.GameOverlayHotkey ??= string.Empty;
        s.HistoryRetentionDays = Snap(s.HistoryRetentionDays, AppSettings.HistoryRetentionChoices);
        s.LevelHighThreshold = Snap(s.LevelHighThreshold, AppSettings.LevelHighChoices);
        s.LevelLowThreshold = Snap(s.LevelLowThreshold, AppSettings.LevelLowChoices);
        s.LevelHighColor = NormalizeColor(s.LevelHighColor, AppSettings.DefaultHighColor);
        s.LevelMidColor = NormalizeColor(s.LevelMidColor, AppSettings.DefaultMidColor);
        s.LevelLowColor = NormalizeColor(s.LevelLowColor, AppSettings.DefaultLowColor);

        // Deserialized collections use the default (case-sensitive) comparer; device ids are case-insensitive.
        s.DeviceAliases = new(s.DeviceAliases ?? new(), StringComparer.OrdinalIgnoreCase);
        s.CategoryOverrides = new(s.CategoryOverrides ?? new(), StringComparer.OrdinalIgnoreCase);
        s.HiddenDeviceIds = new(s.HiddenDeviceIds ?? new(), StringComparer.OrdinalIgnoreCase);
        s.GameOverlayDeviceIds = new(s.GameOverlayDeviceIds ?? new(), StringComparer.OrdinalIgnoreCase);
    }

    private static int Snap(int value, int[] choices) =>
        choices.OrderBy(c => Math.Abs(c - value)).First();

    /// <summary>Accepts <c>#RRGGBB</c> (any case); anything else falls back to the default.</summary>
    private static string NormalizeColor(string? value, string fallback) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)
            ? value.ToUpperInvariant()
            : fallback;
}
