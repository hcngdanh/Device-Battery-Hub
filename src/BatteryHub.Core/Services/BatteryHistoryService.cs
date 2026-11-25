using System.Globalization;
using System.Text;
using BatteryHub.Core.Models;

namespace BatteryHub.Core.Services;

public sealed record BatterySample(DateTimeOffset Timestamp, string DeviceId, string DeviceName, int Percent, bool IsCharging, bool IsConnected);

/// <summary>
/// Append-only battery log: one CSV file per local day (<c>history/yyyy-MM-dd.csv</c>).
/// A sample is written when a device's level, charging or connection state changes, plus a
/// heartbeat every <see cref="HeartbeatInterval"/> so charts stay continuous. Thread-safe.
/// </summary>
public sealed class BatteryHistoryService
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(15);
    private const string Header = "timestamp,id,percent,charging,connected,name";

    private readonly string _directory;
    private readonly object _lock = new();
    private readonly Dictionary<string, BatterySample> _lastWritten = new(StringComparer.OrdinalIgnoreCase);
    private DateOnly? _lastPruneDate;

    public BatteryHistoryService(string directory)
    {
        _directory = directory;
    }

    public string Directory => _directory;

    public void Record(IReadOnlyList<BluetoothDeviceBattery> devices, DateTimeOffset now, int retentionDays)
    {
        lock (_lock)
        {
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            if (_lastPruneDate != today)
            {
                PruneLocked(retentionDays, today);
                _lastPruneDate = today;
            }

            var lines = new StringBuilder();
            foreach (var d in devices)
            {
                var sample = new BatterySample(now, d.Id, d.EffectiveName, d.BatteryPercent, d.IsCharging, d.IsConnected);
                if (_lastWritten.TryGetValue(d.Id, out var last) &&
                    last.Percent == sample.Percent &&
                    last.IsCharging == sample.IsCharging &&
                    last.IsConnected == sample.IsConnected &&
                    now - last.Timestamp < HeartbeatInterval)
                {
                    continue;
                }

                _lastWritten[d.Id] = sample;
                lines.AppendLine(Format(sample));
            }

            if (lines.Length == 0)
                return;

            System.IO.Directory.CreateDirectory(_directory);
            string file = FileFor(today);
            if (!File.Exists(file))
                lines.Insert(0, Header + Environment.NewLine);
            File.AppendAllText(file, lines.ToString());
        }
    }

    /// <summary>Samples for one device (or all devices when <paramref name="deviceId"/> is null) since <paramref name="from"/>.</summary>
    public IReadOnlyList<BatterySample> Read(string? deviceId, DateTimeOffset from)
    {
        lock (_lock)
        {
            var result = new List<BatterySample>();
            var fromDay = DateOnly.FromDateTime(from.LocalDateTime);
            foreach (var (day, file) in EnumerateFilesLocked())
            {
                if (day < fromDay)
                    continue;

                foreach (var line in File.ReadLines(file))
                {
                    if (TryParse(line, out var s) &&
                        s.Timestamp >= from &&
                        (deviceId is null || string.Equals(s.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(s);
                    }
                }
            }
            return result.OrderBy(s => s.Timestamp).ToList();
        }
    }

    /// <summary>Devices that appear in the log, with their most recent name.</summary>
    public IReadOnlyList<(string Id, string Name)> KnownDevices(DateTimeOffset from) =>
        Read(null, from)
            .GroupBy(s => s.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Last().DeviceName))
            .OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void Prune(int retentionDays, DateTimeOffset now)
    {
        lock (_lock)
            PruneLocked(retentionDays, DateOnly.FromDateTime(now.LocalDateTime));
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var (_, file) in EnumerateFilesLocked())
                TryDelete(file);
            _lastWritten.Clear();
        }
    }

    private void PruneLocked(int retentionDays, DateOnly today)
    {
        var cutoff = today.AddDays(-Math.Max(1, retentionDays));
        foreach (var (day, file) in EnumerateFilesLocked())
        {
            if (day < cutoff)
                TryDelete(file);
        }
    }

    private IEnumerable<(DateOnly Day, string File)> EnumerateFilesLocked()
    {
        if (!System.IO.Directory.Exists(_directory))
            yield break;

        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.csv").Order())
        {
            if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                yield return (day, file);
        }
    }

    private string FileFor(DateOnly day) => Path.Combine(_directory, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");

    private static string Format(BatterySample s) =>
        string.Join(',',
            s.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Escape(s.DeviceId),
            s.Percent.ToString(CultureInfo.InvariantCulture),
            s.IsCharging ? "1" : "0",
            s.IsConnected ? "1" : "0",
            Escape(s.DeviceName));

    // Name is the last column, so it only needs commas/newlines neutralised.
    private static string Escape(string value) => value.Replace(',', ' ').Replace('\r', ' ').Replace('\n', ' ');

    internal static bool TryParse(string line, out BatterySample sample)
    {
        sample = null!;
        var parts = line.Split(',', 6);
        if (parts.Length < 6 ||
            !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
        {
            return false;
        }

        sample = new BatterySample(ts, parts[1], parts[5], percent, parts[3] == "1", parts[4] == "1");
        return true;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* in use or already gone */ }
    }
}
