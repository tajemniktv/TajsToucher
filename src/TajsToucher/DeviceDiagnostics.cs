using System.Runtime.CompilerServices;
using TajsToucher.Devices;

namespace TajsToucher;

internal static class DeviceDiagnostics
{
    public static int Run()
    {
        try { return ProbeAsync().GetAwaiter().GetResult(); }
        catch
        {
            Console.WriteLine("YubiKey SDK unavailable. GPG proxying does not require the SDK.");
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> ProbeAsync()
    {
        var native = System.Runtime.InteropServices.NativeLibrary.Load("Yubico.NativeShims.dll",
            typeof(YubiKeyService).Assembly, System.Runtime.InteropServices.DllImportSearchPath.AssemblyDirectory);
        System.Runtime.InteropServices.NativeLibrary.Free(native);
        Console.WriteLine("Yubico native shim: loaded successfully.");
        await using var service = new YubiKeyService();
        var result = await service.RefreshInventoryAsync();
        Console.WriteLine($"Windows attached Yubico USB devices: {result.AttachedUsbDevices?.ToString() ?? "unknown"}.");
        Console.WriteLine($"YubiKey SDK discovery: {result.Outcome}; accessible keys: {result.Keys.Count}.");
        if (result.Keys.Count == 0)
            Console.WriteLine("An empty SDK inventory does not prove the key is disconnected. Another application may own its interface, or Windows may deny access.");
        Console.WriteLine("Inventory only; no application-status reads, PIN attempts, or touch tests. Listeners stopped on exit.");
        return result.Outcome == DeviceOutcome.Ready ? 0 : 1;
    }
}
