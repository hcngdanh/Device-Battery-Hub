using System.Text.Json;
using BatteryHub.Core.Models;

namespace BatteryHub.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _settingsPath = customPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsPath = Path.Combine(appData, "BatteryHub", "settings.json");
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            string json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        string? dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tempPath = _settingsPath + ".tmp";
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    private static void Normalize(AppSettings s)
    {
        s.LowBatteryThreshold = Math.Clamp(s.LowBatteryThreshold, 5, 50);
        s.MaxTaskbarDevices = Math.Clamp(s.MaxTaskbarDevices, 1, 8);
        s.DeviceAliases ??= new(StringComparer.OrdinalIgnoreCase);
        s.CategoryOverrides ??= new(StringComparer.OrdinalIgnoreCase);
        s.HiddenDeviceIds ??= new(StringComparer.OrdinalIgnoreCase);
    }
}
