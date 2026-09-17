using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using Yubico.Core.Devices.Hid;

// Research preflight only. Never call SDK ConnectToIOReports: it opens a
// read/write protocol connection. This tool neither reads nor writes reports.
if (!OperatingSystem.IsWindows()) return 2;
Yubico.Core.Logging.Log.Instance = NullLoggerFactory.Instance;
using var identity = WindowsIdentity.GetCurrent();
var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"Windows: {Environment.OSVersion.Version}; elevated: {elevated}");
try
{
    var devices = HidDevice.GetHidDevices()
        .Where(device => device.VendorId == 0x1050 && (ushort)device.UsagePage == 0xf1d0 && device.Usage == 1)
        .ToArray();
    Console.WriteLine($"Yubico FIDO HID collections: {devices.Length}");
    var index = 0;
    foreach (var device in devices)
    {
        using var handle = Native.CreateFileW(device.Path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        var error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        var result = error switch
        {
            0 => "ReadHandleOpened (report delivery and coexistence NOT validated)",
            5 => "AccessDenied",
            32 => "SharingViolation",
            2 or 3 or 1167 => "RemovedOrUnavailable",
            _ => "UnknownFailure",
        };
        // No paths, serial numbers, container IDs, or raw reports in output.
        Console.WriteLine($"Collection {++index}: {result}; Win32={error}");
    }
    Console.WriteLine("All handles closed. No reports read/written, authentication initiated, or elevation requested.");
    return devices.Length == 0 ? 3 : 0; // Exit 0 means the experiment ran, not monitoring support.
}
catch
{
    Console.WriteLine("Enumeration unavailable; no device identifiers or exception text recorded.");
    return 1;
}

internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);
}
