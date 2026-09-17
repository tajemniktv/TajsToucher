using System.Runtime.InteropServices;

namespace TajsToucher.Devices;

// OS devnode metadata only. Never opens a card/HID connection or requests a PIN.
internal static class UsbDevicePresence
{
    internal static int? CountAttached()
    {
        try
        {
            const uint present = 0x100;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (CM_Get_Device_ID_List_SizeW(out var length, null, present) != 0 || length > 4_194_304)
                    return null;
                var buffer = new char[Math.Max(2, (int)length)];
                var result = CM_Get_Device_ID_ListW(null, buffer, (uint)buffer.Length, present);
                if (result == 0x1a) continue; // CR_BUFFER_SMALL: device list changed.
                if (result != 0) return null;
                return CountPhysicalDevices(new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch { /* Unknown is not zero devices. */ }
        return null;
    }

    internal static int CountPhysicalDevices(IEnumerable<string> ids) => ids
        .Where(id => id.StartsWith("USB\\VID_1050&PID_", StringComparison.OrdinalIgnoreCase) &&
            !id.Split('\\')[1].Contains("&MI_", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase).Count();

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint length, uint flags);
}
