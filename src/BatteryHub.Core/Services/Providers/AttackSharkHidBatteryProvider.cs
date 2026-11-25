using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BatteryHub.Core.Models;
using BatteryHub.Core.Services.Win32;
using Microsoft.Win32.SafeHandles;

namespace BatteryHub.Core.Services.Providers;

/// <summary>
/// Reads asynchronous battery telemetry pushed by Attack Shark X11 2.4GHz USB receivers
/// (VID 0x1D57, PID 0xFA60 / 0xFA55) via Interrupt-IN HID Report ID 0x03.
/// </summary>
public sealed class AttackSharkHidBatteryProvider : IBatteryProvider, IDisposable
{
    public const ushort AttackSharkVendorId = 0x1D57;
    public const ushort AttackSharkReceiverProductId = 0xFA60;
    public const ushort AttackSharkWiredProductId = 0xFA55;
    public const ushort AttackSharkWiredAltProductId = 0x2125;

    public static bool EnableDiagnosticLog { get; set; }

    /// <summary>
    /// While the mouse is powered on, the receiver pushes a battery report roughly every 2s.
    /// When the mouse is switched off the receiver stays enumerated but simply goes silent
    /// (no "disconnected" packet), so a missing heartbeat is the only disconnect signal.
    /// Measured gaps: 2.0s steady, up to 2.75s right after reconnect -> 3.5s is the tightest safe timeout.
    /// </summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(3.5);

    public static bool IsHeartbeatStale(DateTimeOffset lastReport, DateTimeOffset now) =>
        now - lastReport > HeartbeatTimeout;

    private sealed class ActiveListener : IDisposable
    {
        public required string DevicePath { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required Task LoopTask { get; init; }

        public void Dispose()
        {
            try { Cts.Cancel(); } catch { }
            Cts.Dispose();
        }
    }

    private readonly ConcurrentDictionary<string, ActiveListener> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();
    private BluetoothDeviceBattery? _latestReading;
    private readonly System.Threading.Timer _staleTimer;
    private bool _disposed;

    public event Action? BatteryChanged;

    public AttackSharkHidBatteryProvider()
    {
        // Fires once when the heartbeat expires so the UI flips to "Disconnected" immediately
        // instead of waiting for the next poll.
        _staleTimer = new System.Threading.Timer(_ => BatteryChanged?.Invoke());
    }

    public Task<IReadOnlyList<BluetoothDeviceBattery>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_disposed)
        {
            SyncListeners();
        }

        BluetoothDeviceBattery? snapshot;
        lock (_stateLock)
        {
            snapshot = _latestReading;
        }

        if (snapshot is null)
            return Task.FromResult<IReadOnlyList<BluetoothDeviceBattery>>(Array.Empty<BluetoothDeviceBattery>());

        // Receiver still plugged in but the mouse stopped reporting -> mouse is off / out of range.
        // Keep the last known percentage but surface it as disconnected (and not charging).
        if (IsHeartbeatStale(snapshot.LastUpdated, DateTimeOffset.UtcNow))
            snapshot = snapshot with { IsConnected = false, IsCharging = false };

        return Task.FromResult<IReadOnlyList<BluetoothDeviceBattery>>([snapshot]);
    }

    public static bool TryParseBatteryReport(ReadOnlySpan<byte> report, out int percent, out bool isCharging)
    {
        percent = 0;
        isCharging = false;

        if (report.Length < 5)
            return false;

        if (report[0] != 0x03)
            return false;

        if (report[2] is not (0x40 or 0x41))
            return false;

        byte rawPercent = report[4];
        if (rawPercent > 100)
            return false;

        percent = rawPercent;
        isCharging = report[3] == 0x03;
        return true;
    }

    private void SyncListeners()
    {
        var currentPaths = EnumerateTargetHidPaths();
        var currentSet = new HashSet<string>(currentPaths, StringComparer.OrdinalIgnoreCase);

        foreach (var existingKey in _listeners.Keys.ToList())
        {
            if (!currentSet.Contains(existingKey) && _listeners.TryRemove(existingKey, out var oldListener))
            {
                oldListener.Dispose();
            }
        }

        if (currentSet.Count == 0)
        {
            lock (_stateLock)
            {
                _latestReading = null;
            }
            return;
        }

        foreach (var path in currentPaths)
        {
            if (_listeners.ContainsKey(path))
                continue;

            var cts = new CancellationTokenSource();
            var loop = Task.Run(() => ListenLoopAsync(path, cts.Token), cts.Token);
            var listener = new ActiveListener
            {
                DevicePath = path,
                Cts = cts,
                LoopTask = loop
            };

            if (!_listeners.TryAdd(path, listener))
            {
                listener.Dispose();
            }
        }
    }

    private static List<string> EnumerateTargetHidPaths()
    {
        var paths = new List<string>();
        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);

        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            NativeMethods.DIGCF_DEVICEINTERFACE | NativeMethods.DIGCF_PRESENT);

        if (devInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
            return paths;

        try
        {
            var ifaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
            };

            uint index = 0;
            while (NativeMethods.SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, index++, ref ifaceData))
            {
                var devInfoData = new NativeMethods.SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>()
                };

                NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    devInfoSet,
                    ref ifaceData,
                    IntPtr.Zero,
                    0,
                    out uint requiredSize,
                    ref devInfoData);

                if (requiredSize == 0)
                    continue;

                IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    int cbSize = IntPtr.Size == 8 ? 8 : 6;
                    Marshal.WriteInt32(detailBuffer, cbSize);

                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                            devInfoSet,
                            ref ifaceData,
                            detailBuffer,
                            requiredSize,
                            out _,
                            ref devInfoData))
                    {
                        continue;
                    }

                    string? devicePath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
                    if (string.IsNullOrWhiteSpace(devicePath))
                        continue;

                    // Quick path filter before opening handle
                    if (!devicePath.Contains("vid_1d57", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (devicePath.Contains("pid_fa60", StringComparison.OrdinalIgnoreCase) ||
                        devicePath.Contains("pid_fa55", StringComparison.OrdinalIgnoreCase) ||
                        devicePath.Contains("pid_2125", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(devicePath);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return paths;
    }

    private async Task ListenLoopAsync(string devicePath, CancellationToken ct)
    {
        IntPtr handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        if (handle == NativeMethods.INVALID_HANDLE_VALUE)
            return;

        using var safeHandle = new SafeFileHandle(handle, ownsHandle: true);

        var attrs = new NativeMethods.HIDD_ATTRIBUTES
        {
            Size = Marshal.SizeOf<NativeMethods.HIDD_ATTRIBUTES>()
        };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attrs) ||
            attrs.VendorID != AttackSharkVendorId ||
            (attrs.ProductID != AttackSharkReceiverProductId &&
             attrs.ProductID != AttackSharkWiredProductId &&
             attrs.ProductID != AttackSharkWiredAltProductId))
        {
            return;
        }

        if (!NativeMethods.HidD_GetPreparsedData(handle, out IntPtr preparsed) || preparsed == IntPtr.Zero)
            return;

        ushort inputLen;
        ushort usagePage;
        ushort usage;
        try
        {
            if (NativeMethods.HidP_GetCaps(preparsed, out var caps) < 0)
                return;

            inputLen = caps.InputReportByteLength;
            usagePage = caps.UsagePage;
            usage = caps.Usage;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }

        // Skip standard mouse/keyboard top-level collections (UsagePage 0x01 Usage 0x02/0x06)
        if (inputLen < 5 || (usagePage == 0x0001 && (usage == 0x0002 || usage == 0x0006)))
            return;

        LogHid($"Listening PID=0x{attrs.ProductID:X4} UsagePage=0x{usagePage:X4} Usage=0x{usage:X4} InputLen={inputLen}");

        // Try immediate input report query first (some firmware revisions answer Report 0x03 directly)
        byte[] probeBuffer = new byte[inputLen];
        probeBuffer[0] = 0x03;
        if (NativeMethods.HidD_GetInputReport(handle, probeBuffer, inputLen))
        {
            LogHid($"GetInputReport PID=0x{attrs.ProductID:X4}: {Convert.ToHexString(probeBuffer)}");
            if (TryParseBatteryReport(probeBuffer, out int initialPct, out bool initialCharging))
            {
                RecordReading(initialPct, initialCharging, attrs.ProductID != AttackSharkReceiverProductId);
            }
        }

        try
        {
            using var stream = new FileStream(safeHandle, FileAccess.Read, bufferSize: inputLen, isAsync: true);
            byte[] readBuffer = new byte[inputLen];

            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(readBuffer.AsMemory(0, inputLen), ct).ConfigureAwait(false);
                if (bytesRead <= 0)
                    break;

                LogHid($"Input PID=0x{attrs.ProductID:X4}: {Convert.ToHexString(readBuffer, 0, bytesRead)}");
                if (TryParseBatteryReport(readBuffer.AsSpan(0, bytesRead), out int pct, out bool charging))
                {
                    RecordReading(pct, charging, attrs.ProductID != AttackSharkReceiverProductId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch
        {
            // Device disconnected or interface closed
        }
    }

    private void RecordReading(int percent, bool isCharging, bool isWired = false)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _latestReading is null ||
                      _latestReading.BatteryPercent != percent ||
                      _latestReading.IsCharging != isCharging ||
                      IsHeartbeatStale(_latestReading.LastUpdated, DateTimeOffset.UtcNow); // reconnected after being off

            _latestReading = new BluetoothDeviceBattery
            {
                Id = "hid-1d57-fa60-attackshark-x11",
                Name = isWired ? "Attack Shark X11 (USB)" : "Attack Shark X11",
                Category = DeviceCategory.Mouse,
                BatteryPercent = percent,
                IsConnected = true,
                IsCharging = isCharging,
                Source = BatterySource.UsbHid,
                LastUpdated = DateTimeOffset.UtcNow
            };
        }

        if (!_disposed)
            _staleTimer.Change(HeartbeatTimeout + TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);

        if (changed)
            BatteryChanged?.Invoke();
    }

    private static void LogHid(string line)
    {
        if (!EnableDiagnosticLog)
            return;

        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryHub");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "hid.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging errors
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _staleTimer.Dispose();
        foreach (var listener in _listeners.Values)
        {
            listener.Dispose();
        }
        _listeners.Clear();
    }
}
