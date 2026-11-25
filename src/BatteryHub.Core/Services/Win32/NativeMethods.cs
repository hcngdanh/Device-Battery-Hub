using System.Runtime.InteropServices;

namespace BatteryHub.Core.Services.Win32;

internal static class NativeMethods
{
    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_ALLCLASSES = 0x00000004;
    internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    internal const uint DEVPROP_TYPE_BYTE = 0x00000003;
    internal const uint DEVPROP_TYPE_UINT32 = 0x00000007;
    internal const uint DEVPROP_TYPE_GUID = 0x0000000D;
    internal const uint DEVPROP_TYPE_BOOLEAN = 0x00000011;
    internal const uint DEVPROP_TYPE_STRING = 0x00000012;

    internal const uint DN_STARTED = 0x00000008;

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    internal const uint BLUETOOTH_GATT_FLAG_NONE = 0x00000000;

    internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    internal static readonly Guid GUID_BLUETOOTHLE_DEVICE_INTERFACE = new("781AEE18-7733-4CE4-ADD0-91F41C67B592");

    // Standard Windows devpkey.h keys
    internal static readonly DEVPROPKEY DEVPKEY_Device_DeviceDesc = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);
    internal static readonly DEVPROPKEY DEVPKEY_Device_Class = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 9);
    internal static readonly DEVPROPKEY DEVPKEY_Device_FriendlyName = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    internal static readonly DEVPROPKEY DEVPKEY_Device_InstanceId = new(new("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);
    internal static readonly DEVPROPKEY DEVPKEY_Device_ContainerId = new(new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    internal static readonly DEVPROPKEY DEVPKEY_Device_IsConnected = new(new("83DA6326-97A6-4088-9453-A1923F573B29"), 15);
    internal static readonly DEVPROPKEY DEVPKEY_Bluetooth_Battery = new(new("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    internal static readonly DEVPROPKEY DEVPKEY_Bluetooth_ClassOfDevice = new(new("A92F26CA-EDA7-4B1D-9DB2-27B68AA5A2EB"), 10);

    [StructLayout(LayoutKind.Sequential)]
    internal struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;

        public DEVPROPKEY(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnConnected;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    internal struct BTH_LE_UUID
    {
        [FieldOffset(0)]
        public byte IsShortUuid;
        [FieldOffset(4)]
        public ushort ShortUuid;
        [FieldOffset(4)]
        public Guid LongUuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BTH_LE_GATT_SERVICE
    {
        public BTH_LE_UUID ServiceUuid;
        public ushort AttributeHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BTH_LE_GATT_CHARACTERISTIC
    {
        public ushort ServiceHandle;
        public BTH_LE_UUID CharacteristicUuid;
        public ushort AttributeHandle;
        public ushort CharacteristicValueHandle;
        public byte IsBroadcastable;
        public byte IsReadable;
        public byte IsWritable;
        public byte IsWritableWithoutResponse;
        public byte IsSignedWritable;
        public byte IsNotifiable;
        public byte IsIndicatable;
        public byte HasExtendedProperties;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        IntPtr ClassGuid,
        string? Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        string? Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet,
        uint MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        byte[]? PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    internal static extern uint CM_Get_DevNode_Status(
        out uint pulStatus,
        out uint pulProblemNumber,
        uint dnDevInst,
        uint ulFlags);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr BluetoothFindFirstDevice(
        ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp,
        ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindNextDevice(
        IntPtr hFind,
        ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern void HidD_GetHidGuid(out Guid HidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetInputReport(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

    [DllImport("bluetoothapis.dll", SetLastError = true)]
    internal static extern int BluetoothGATTGetServices(
        IntPtr hDevice,
        ushort ServicesBufferCount,
        [Out] BTH_LE_GATT_SERVICE[]? ServicesBuffer,
        out ushort ServicesBufferActual,
        uint Flags);

    [DllImport("bluetoothapis.dll", SetLastError = true)]
    internal static extern int BluetoothGATTGetCharacteristics(
        IntPtr hDevice,
        ref BTH_LE_GATT_SERVICE Service,
        ushort CharacteristicsBufferCount,
        [Out] BTH_LE_GATT_CHARACTERISTIC[]? CharacteristicsBuffer,
        out ushort CharacteristicsBufferActual,
        uint Flags);

    [DllImport("bluetoothapis.dll", SetLastError = true)]
    internal static extern int BluetoothGATTGetCharacteristicValue(
        IntPtr hDevice,
        ref BTH_LE_GATT_CHARACTERISTIC Characteristic,
        uint CharacteristicValueDataSize,
        IntPtr CharacteristicValue,
        out ushort CharacteristicValueSizeRequired,
        uint Flags);
}
