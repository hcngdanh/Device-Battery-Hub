using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BatteryHub.Core.Models;
using BatteryHub.Core.Services.Win32;

namespace BatteryHub.Core.Services.Providers;

public sealed partial class WindowsPnPBatteryProvider : IBatteryProvider
{
    private const ushort GattBatteryServiceUuid = 0x180F;
    private const ushort GattBatteryLevelCharUuid = 0x2A19;

    [GeneratedRegex(@"(?:DEV_|&|_)(?<mac>[0-9A-Fa-f]{12})(?:[\\&_]|$)", RegexOptions.Compiled)]
    private static partial Regex BluetoothAddressRegex();

    private sealed class ClassicBtRecord
    {
        public string Name { get; init; } = string.Empty;
        public string MacHex { get; init; } = string.Empty;
        public uint ClassOfDevice { get; init; }
        public bool IsConnected { get; init; }
    }

    private sealed class ContainerAggregate
    {
        public Guid ContainerId { get; init; }
        public List<(string? Name, string? PnPClass, string InstanceId)> Nodes { get; } = new();
        public int? PnPBattery { get; set; }
        public uint? ClassOfDevice { get; set; }
        public string? BluetoothMacHex { get; set; }
        public bool? ExplicitConnected { get; set; }
        public bool HasStartedNode { get; set; }
        public bool IsBluetoothContainer { get; set; }
    }

    public Task<IReadOnlyList<BluetoothDeviceBattery>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadDevicesInternal(cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<BluetoothDeviceBattery> ReadDevicesInternal(CancellationToken ct)
    {
        var classicDevices = ReadClassicBluetoothDevices();
        var classicByMac = classicDevices
            .Where(d => !string.IsNullOrEmpty(d.MacHex))
            .GroupBy(d => d.MacHex, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var containers = ScanPnPContainers(ct);
        var bleBatteryCache = new Lazy<Dictionary<Guid, int>>(() => ScanBleGattBatteries(ct));

        var results = new List<BluetoothDeviceBattery>();
        var now = DateTimeOffset.UtcNow;

        foreach (var container in containers.Values)
        {
            if (ct.IsCancellationRequested)
                break;

            if (!container.IsBluetoothContainer && container.PnPBattery is null)
                continue;

            ClassicBtRecord? classic = null;
            if (!string.IsNullOrEmpty(container.BluetoothMacHex))
            {
                classicByMac.TryGetValue(container.BluetoothMacHex, out classic);
            }

            bool isConnected = DetermineConnectionState(container, classic);
            int? battery = container.PnPBattery;
            BatterySource source = BatterySource.WindowsPnP;

            if (battery is null && isConnected && container.IsBluetoothContainer)
            {
                if (bleBatteryCache.Value.TryGetValue(container.ContainerId, out int gattBattery))
                {
                    battery = gattBattery;
                    source = BatterySource.BleGatt;
                }
            }

            if (battery is null)
                continue;

            string name = ResolveBestName(container, classic);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            uint? cod = container.ClassOfDevice ?? classic?.ClassOfDevice;
            var pnpClasses = container.Nodes
                .Select(n => n.PnPClass)
                .Where(c => !string.IsNullOrWhiteSpace(c))!;

            var category = DeviceClassifier.Classify(name, cod, pnpClasses!);

            results.Add(new BluetoothDeviceBattery
            {
                Id = container.ContainerId.ToString("N"),
                Name = name,
                Category = category,
                BatteryPercent = Math.Clamp(battery.Value, 0, 100),
                IsConnected = isConnected,
                Source = source,
                LastUpdated = now
            });
        }

        return results
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Category)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool DetermineConnectionState(ContainerAggregate container, ClassicBtRecord? classic)
    {
        if (classic is not null)
            return classic.IsConnected;

        if (container.ExplicitConnected is bool connected)
            return connected;

        return container.HasStartedNode;
    }

    private static string ResolveBestName(ContainerAggregate container, ClassicBtRecord? classic)
    {
        if (classic is not null && !string.IsNullOrWhiteSpace(classic.Name))
            return classic.Name.Trim();

        // Prefer Bluetooth-class node names, then Audio/HID, skipping generic GATT/service labels.
        static int Score((string? Name, string? PnPClass, string InstanceId) node)
        {
            if (string.IsNullOrWhiteSpace(node.Name)) return -1;
            string n = node.Name.Trim();
            if (n.Contains("Generic Attribute", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Generic Access", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Device Information Service", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Battery Service", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("HID-compliant", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Microsoft Bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(node.PnPClass, "Bluetooth", StringComparison.OrdinalIgnoreCase))
                return 100;
            if (node.InstanceId.StartsWith("BTHLE\\Dev_", StringComparison.OrdinalIgnoreCase) ||
                node.InstanceId.StartsWith("BTHENUM\\Dev_", StringComparison.OrdinalIgnoreCase))
                return 90;
            if (string.Equals(node.PnPClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.PnPClass, "MEDIA", StringComparison.OrdinalIgnoreCase))
                return 60;

            return 40;
        }

        return container.Nodes
            .OrderByDescending(Score)
            .Select(n => n.Name?.Trim())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty;
    }

    private static List<ClassicBtRecord> ReadClassicBluetoothDevices()
    {
        var list = new List<ClassicBtRecord>();
        var searchParams = new NativeMethods.BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<NativeMethods.BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = true,
            fReturnRemembered = true,
            fReturnUnknown = false,
            fReturnConnected = true,
            fIssueInquiry = false,
            cTimeoutMultiplier = 0,
            hRadio = IntPtr.Zero
        };

        var info = new NativeMethods.BLUETOOTH_DEVICE_INFO
        {
            dwSize = (uint)Marshal.SizeOf<NativeMethods.BLUETOOTH_DEVICE_INFO>()
        };

        IntPtr findHandle = NativeMethods.BluetoothFindFirstDevice(ref searchParams, ref info);
        if (findHandle == IntPtr.Zero)
            return list;

        try
        {
            do
            {
                string macHex = (info.Address & 0x0000FFFFFFFFFFFFUL).ToString("X12");
                list.Add(new ClassicBtRecord
                {
                    Name = info.szName ?? string.Empty,
                    MacHex = macHex,
                    ClassOfDevice = info.ulClassofDevice,
                    IsConnected = info.fConnected
                });
            }
            while (NativeMethods.BluetoothFindNextDevice(findHandle, ref info));
        }
        finally
        {
            NativeMethods.BluetoothFindDeviceClose(findHandle);
        }

        return list;
    }

    private static Dictionary<Guid, ContainerAggregate> ScanPnPContainers(CancellationToken ct)
    {
        var containers = new Dictionary<Guid, ContainerAggregate>();

        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            NativeMethods.DIGCF_ALLCLASSES | NativeMethods.DIGCF_PRESENT);

        if (devInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
            return containers;

        try
        {
            var devInfoData = new NativeMethods.SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>()
            };

            uint index = 0;
            while (!ct.IsCancellationRequested &&
                   NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, index++, ref devInfoData))
            {
                Guid? containerId = SetupApi.GetGuidProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Device_ContainerId);
                if (containerId is null || containerId.Value == Guid.Empty)
                    continue;

                string instanceId = SetupApi.GetStringProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Device_InstanceId) ?? string.Empty;
                string? pnpClass = SetupApi.GetStringProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Device_Class);
                byte? battery = SetupApi.GetByteProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Bluetooth_Battery);

                bool isBluetoothNode =
                    instanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pnpClass, "Bluetooth", StringComparison.OrdinalIgnoreCase);

                if (!isBluetoothNode && battery is null && !containers.ContainsKey(containerId.Value))
                {
                    // Skip unrelated PCI/USB containers. HID/Audio child nodes can be enumerated before
                    // their BTH parent, so those classes are kept to avoid losing the container.
                    if (!string.Equals(pnpClass, "HIDClass", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(pnpClass, "Mouse", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(pnpClass, "Keyboard", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(pnpClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(pnpClass, "MEDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (!containers.TryGetValue(containerId.Value, out var agg))
                {
                    agg = new ContainerAggregate { ContainerId = containerId.Value };
                    containers[containerId.Value] = agg;
                }

                if (isBluetoothNode)
                    agg.IsBluetoothContainer = true;

                string? friendlyName = SetupApi.GetStringProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Device_FriendlyName)
                                       ?? SetupApi.GetStringProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Device_DeviceDesc);

                agg.Nodes.Add((friendlyName, pnpClass, instanceId));

                if (battery is byte b && b <= 100)
                    agg.PnPBattery = b;

                if (agg.ClassOfDevice is null)
                {
                    uint? cod = SetupApi.GetUInt32Property(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Bluetooth_ClassOfDevice);
                    if (cod is not null && cod.Value != 0)
                        agg.ClassOfDevice = cod.Value;
                }

                if (string.IsNullOrEmpty(agg.BluetoothMacHex) && isBluetoothNode)
                {
                    var match = BluetoothAddressRegex().Match(instanceId);
                    if (match.Success)
                        agg.BluetoothMacHex = match.Groups["mac"].Value.ToUpperInvariant();
                }

                if (isBluetoothNode)
                {
                    bool? isConnected = SetupApi.GetBoolProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Device_IsConnected);
                    if (isConnected is bool c)
                    {
                        agg.ExplicitConnected = agg.ExplicitConnected is true || c;
                    }
                }

                if (NativeMethods.CM_Get_DevNode_Status(out uint status, out uint problem, devInfoData.DevInst, 0) == 0)
                {
                    if ((status & NativeMethods.DN_STARTED) != 0 && problem == 0)
                        agg.HasStartedNode = true;
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
        }

        return containers;
    }

    private static Dictionary<Guid, int> ScanBleGattBatteries(CancellationToken ct)
    {
        var result = new Dictionary<Guid, int>();
        try
        {
            foreach (var iface in SetupApi.EnumerateInterfaces(NativeMethods.GUID_BLUETOOTHLE_DEVICE_INTERFACE, ct))
            {
                if (ct.IsCancellationRequested)
                    break;
                if (iface.ContainerId is not Guid containerId || containerId == Guid.Empty)
                    continue;
                if (ReadBleBatteryFromPath(iface.DevicePath) is int level)
                    result[containerId] = level;
            }
        }
        catch
        {
            // Ignore GATT enumeration failures on systems without a BLE radio.
        }

        return result;
    }

    private static int? ReadBleBatteryFromPath(string devicePath)
    {
        IntPtr handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (handle == NativeMethods.INVALID_HANDLE_VALUE)
            return null;

        try
        {
            NativeMethods.BluetoothGATTGetServices(handle, 0, null, out ushort serviceCount, NativeMethods.BLUETOOTH_GATT_FLAG_NONE);
            if (serviceCount == 0)
                return null;

            var services = new NativeMethods.BTH_LE_GATT_SERVICE[serviceCount];
            int hr = NativeMethods.BluetoothGATTGetServices(handle, serviceCount, services, out serviceCount, NativeMethods.BLUETOOTH_GATT_FLAG_NONE);
            if (hr != 0)
                return null;

            for (int i = 0; i < serviceCount; i++)
            {
                var svc = services[i];
                if (!IsShortUuidMatch(svc.ServiceUuid, GattBatteryServiceUuid))
                    continue;

                NativeMethods.BluetoothGATTGetCharacteristics(handle, ref svc, 0, null, out ushort charCount, NativeMethods.BLUETOOTH_GATT_FLAG_NONE);
                if (charCount == 0)
                    continue;

                var chars = new NativeMethods.BTH_LE_GATT_CHARACTERISTIC[charCount];
                hr = NativeMethods.BluetoothGATTGetCharacteristics(handle, ref svc, charCount, chars, out charCount, NativeMethods.BLUETOOTH_GATT_FLAG_NONE);
                if (hr != 0)
                    continue;

                for (int j = 0; j < charCount; j++)
                {
                    var ch = chars[j];
                    if (!IsShortUuidMatch(ch.CharacteristicUuid, GattBatteryLevelCharUuid) || ch.IsReadable == 0)
                        continue;

                    NativeMethods.BluetoothGATTGetCharacteristicValue(handle, ref ch, 0, IntPtr.Zero, out ushort valSize, NativeMethods.BLUETOOTH_GATT_FLAG_NONE);
                    if (valSize < 8)
                        continue;

                    IntPtr valPtr = Marshal.AllocHGlobal(valSize);
                    try
                    {
                        hr = NativeMethods.BluetoothGATTGetCharacteristicValue(handle, ref ch, valSize, valPtr, out _, NativeMethods.BLUETOOTH_GATT_FLAG_NONE);
                        if (hr == 0)
                        {
                            // BTH_LE_GATT_CHARACTERISTIC_VALUE: uint DataSize (offset 0), byte Data[1] (offset 4)
                            uint dataSize = (uint)Marshal.ReadInt32(valPtr, 0);
                            if (dataSize >= 1)
                            {
                                byte level = Marshal.ReadByte(valPtr, 4);
                                if (level <= 100)
                                    return level;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(valPtr);
                    }
                }
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        return null;
    }

    private static bool IsShortUuidMatch(NativeMethods.BTH_LE_UUID uuid, ushort shortUuid)
    {
        if (uuid.IsShortUuid != 0)
            return uuid.ShortUuid == shortUuid;

        byte[] bytes = uuid.LongUuid.ToByteArray();
        ushort extracted = (ushort)(bytes[0] | (bytes[1] << 8));
        return extracted == shortUuid;
    }
}
