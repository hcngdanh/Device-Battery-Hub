using System.Runtime.InteropServices;
using System.Text;

namespace BatteryHub.Core.Services.Win32;

/// <summary>
/// Thin managed helpers over SetupAPI: device-interface enumeration and typed DEVPROPKEY reads.
/// </summary>
internal static class SetupApi
{
    internal readonly record struct DeviceInterface(string DevicePath, Guid? ContainerId);

    /// <summary>
    /// Lists present device interfaces of <paramref name="interfaceClass"/> with their symbolic link path
    /// and the owning device's container id.
    /// </summary>
    public static List<DeviceInterface> EnumerateInterfaces(Guid interfaceClass, CancellationToken ct = default)
    {
        var result = new List<DeviceInterface>();

        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref interfaceClass,
            null,
            IntPtr.Zero,
            NativeMethods.DIGCF_DEVICEINTERFACE | NativeMethods.DIGCF_PRESENT);

        if (devInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
            return result;

        try
        {
            var ifaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
            };

            uint index = 0;
            while (!ct.IsCancellationRequested &&
                   NativeMethods.SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref interfaceClass, index++, ref ifaceData))
            {
                var devInfoData = new NativeMethods.SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>()
                };

                NativeMethods.SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifaceData, IntPtr.Zero, 0, out uint requiredSize, ref devInfoData);
                if (requiredSize == 0)
                    continue;

                IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on x64, 6 on x86.
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);

                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifaceData, detailBuffer, requiredSize, out _, ref devInfoData))
                        continue;

                    string? devicePath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
                    if (string.IsNullOrWhiteSpace(devicePath))
                        continue;

                    var containerId = GetGuidProperty(devInfoSet, ref devInfoData, NativeMethods.DEVPKEY_Device_ContainerId);
                    result.Add(new DeviceInterface(devicePath, containerId));
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

        return result;
    }

    public static byte[]? GetRawProperty(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, NativeMethods.DEVPROPKEY key, out uint propType)
    {
        NativeMethods.SetupDiGetDeviceProperty(devInfoSet, ref devInfoData, ref key, out propType, null, 0, out uint reqSize, 0);
        if (reqSize == 0)
            return null;

        byte[] buffer = new byte[reqSize];
        if (!NativeMethods.SetupDiGetDeviceProperty(devInfoSet, ref devInfoData, ref key, out propType, buffer, reqSize, out _, 0))
            return null;

        return buffer;
    }

    public static string? GetStringProperty(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, NativeMethods.DEVPROPKEY key)
    {
        var buf = GetRawProperty(devInfoSet, ref devInfoData, key, out uint propType);
        if (buf is null || propType != NativeMethods.DEVPROP_TYPE_STRING)
            return null;
        return Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    public static byte? GetByteProperty(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, NativeMethods.DEVPROPKEY key)
    {
        var buf = GetRawProperty(devInfoSet, ref devInfoData, key, out uint propType);
        if (buf is null || buf.Length < 1 || propType != NativeMethods.DEVPROP_TYPE_BYTE)
            return null;
        return buf[0];
    }

    public static uint? GetUInt32Property(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, NativeMethods.DEVPROPKEY key)
    {
        var buf = GetRawProperty(devInfoSet, ref devInfoData, key, out uint propType);
        if (buf is null || buf.Length < 4 || propType != NativeMethods.DEVPROP_TYPE_UINT32)
            return null;
        return BitConverter.ToUInt32(buf, 0);
    }

    public static bool? GetBoolProperty(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, NativeMethods.DEVPROPKEY key)
    {
        var buf = GetRawProperty(devInfoSet, ref devInfoData, key, out uint propType);
        if (buf is null || buf.Length < 1 || propType != NativeMethods.DEVPROP_TYPE_BOOLEAN)
            return null;
        return buf[0] != 0;
    }

    public static Guid? GetGuidProperty(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, NativeMethods.DEVPROPKEY key)
    {
        var buf = GetRawProperty(devInfoSet, ref devInfoData, key, out uint propType);
        if (buf is null || buf.Length < 16 || propType != NativeMethods.DEVPROP_TYPE_GUID)
            return null;
        return new Guid(buf.AsSpan(0, 16));
    }
}
