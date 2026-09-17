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
        .Where(IsPhysicalKey)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count();

    private static bool IsPhysicalKey(string id)
    {
        var parts = id.Split('\\');
        if (parts.Length != 3 || parts[2].Length == 0 || !parts[0].Equals("USB", StringComparison.OrdinalIgnoreCase)) return false;
        const string prefix = "VID_1050&PID_";
        var hardware = parts[1];
        if (hardware.Length != prefix.Length + 4 || !hardware.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !ushort.TryParse(hardware.AsSpan(prefix.Length), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var product)) return false;
        // Yubico SDK 1.17.3 ProductIdentifiers: excludes HSMs and unknown products.
        // https://github.com/Yubico/Yubico.NET.SDK/blob/1.17.3/Yubico.YubiKey/src/Yubico/YubiKey/ProductIdentifiers.cs
        return product is 0x0010 or 0x0410 or 0x0120 or >= 0x0110 and <= 0x0116 or >= 0x0401 and <= 0x0407;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint length, uint flags);
}
