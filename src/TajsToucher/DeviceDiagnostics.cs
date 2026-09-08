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
        Console.WriteLine($"YubiKey SDK discovery: {result.Outcome}; connected keys: {result.Keys.Count}.");
        Console.WriteLine("Inventory only; no application-status reads, PIN attempts, or touch tests. Listeners stopped on exit.");
        return result.Outcome == DeviceOutcome.Ready ? 0 : 1;
    }
}
