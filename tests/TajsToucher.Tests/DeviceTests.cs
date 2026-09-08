using System.Runtime.InteropServices;
using TajsToucher.Devices;
using Yubico.YubiKey;

namespace TajsToucher.Tests;

[TestClass]
public sealed class DeviceTests
{
    [TestMethod]
    public async Task InventoryKeepsIdenticalSeriallessKeysSeparateAndRefreshesHandles()
    {
        var first = DeviceProxy.Make(null);
        var second = DeviceProxy.Make(null);
        var source = new Discovery { Keys = [first, second, DeviceProxy.Make(123)] };
        await using var service = new YubiKeyService(source);
        var initial = await service.RefreshInventoryAsync();
        Assert.AreEqual(3, initial.Keys.Count);
        Assert.AreEqual(3, initial.Keys.Select(key => key.Id).Distinct().Count());
        source.Keys = [first, second, DeviceProxy.Make(123)];
        var refreshed = await service.RefreshInventoryAsync();
        CollectionAssert.AreEqual(initial.Keys.Select(k => k.Id).ToArray(), refreshed.Keys.Select(k => k.Id).ToArray());
        Assert.IsFalse(refreshed.Keys.Any(key => key.Label.Contains("123")));
    }

    [TestMethod]
    public async Task RemovalInvalidatesSelectionAndListenerShutdownIsIdempotent()
    {
        var source = new Discovery { Keys = [DeviceProxy.Make(null)] };
        var service = new YubiKeyService(source);
        var first = await service.RefreshInventoryAsync();
        var signals = new List<DeviceSignal>();
        service.Signal += _ => throw new InvalidOperationException("failed observer");
        service.Signal += signals.Add;
        source.Keys = [];
        await service.RefreshInventoryAsync();
        Assert.AreEqual(DeviceSignalKind.Removed, signals.Single().Kind);
        Assert.AreEqual(first.Keys[0].Id, signals[0].DeviceId);
        var status = await service.ReadStatusAsync(first.Keys[0].Id);
        Assert.AreEqual(DeviceOutcome.Removed, status.Single().Outcome);
        await service.DisposeAsync();
        await service.DisposeAsync();
        Assert.AreEqual(1, source.Disposals);
        Assert.AreEqual(0, source.Subscribers);
        var after = await service.RefreshInventoryAsync();
        Assert.AreEqual(DeviceOutcome.Unavailable, after.Outcome);
    }

    [TestMethod]
    public async Task DiscoveryFailureIsNotReportedAsNoDevice()
    {
        var source = new Discovery { Error = new UnauthorizedAccessException("serial secret") };
        await using var service = new YubiKeyService(source);
        var result = await service.RefreshInventoryAsync();
        Assert.AreEqual(DeviceOutcome.PermissionDenied, result.Outcome);
        Assert.AreEqual(0, result.Keys.Count);
    }

    [TestMethod]
    public async Task DisabledApplicationsDoNotOpenSessions()
    {
        var source = new Discovery { Keys = [DeviceProxy.Make(null)] };
        await using var service = new YubiKeyService(source);
        var inventory = await service.RefreshInventoryAsync();
        Assert.IsFalse(inventory.Keys[0].CanIdentify);
        var status = await service.ReadStatusAsync(inventory.Keys[0].Id);
        Assert.AreEqual(3, status.Count);
        Assert.IsTrue(status.All(s => s.Outcome == DeviceOutcome.Unsupported && s.RetriesRemaining is null));
        Assert.AreEqual(DeviceOutcome.Unsupported, await service.IdentifyAsync(inventory.Keys[0].Id, CancellationToken.None));
    }

    [TestMethod]
    public void CancellationBeforeTouchAndLateCallbacksAreSafe()
    {
        using var cancellation = new CancellationTokenSource();
        var lifetime = new TouchRequestLifetime(cancellation.Token);
        cancellation.Cancel();
        var calls = 0;
        var prompts = 0;
        Assert.IsTrue(lifetime.Request(() => calls++, () => prompts++));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(0, prompts);
        lifetime.Release();
        lifetime.Dispose();
        Assert.IsFalse(lifetime.Request(() => calls++, () => prompts++));
        lifetime.Release();
        lifetime.Dispose();
        Assert.AreEqual(1, calls);
        Assert.AreEqual(0, prompts);
    }

    [TestMethod]
    public void CancellationWhileTouchingSignalsSdkWithoutTreatingReleaseAsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        using var lifetime = new TouchRequestLifetime(cancellation.Token);
        var calls = 0;
        lifetime.Request(() => calls++);
        cancellation.Cancel();
        Assert.AreEqual(1, calls);
        lifetime.Release();
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(unchecked((int)0x8010000B), DeviceOutcome.Busy)]
    [DataRow(unchecked((int)0x80100069), DeviceOutcome.Removed)]
    [DataRow(unchecked((int)0x80100027), DeviceOutcome.PermissionDenied)]
    [DataRow(unchecked((int)0x8010000A), DeviceOutcome.TimedOut)]
    [DataRow(unchecked((int)0x80070005), DeviceOutcome.PermissionDenied)]
    public void PreservesWindowsDeviceErrorCategories(int hresult, DeviceOutcome expected) =>
        Assert.AreEqual(expected, YubiKeyService.Classify(new COMException("private metadata", hresult)));

    private sealed class Discovery : IKeyDiscovery
    {
        private Action<IYubiKeyDevice, bool>? changed;
        public IReadOnlyList<IYubiKeyDevice> Keys { get; set; } = [];
        public Exception? Error { get; set; }
        public int Disposals { get; private set; }
        public int Subscribers => changed?.GetInvocationList().Length ?? 0;
        public event Action<IYubiKeyDevice, bool>? PresenceChanged { add => changed += value; remove => changed -= value; }
        public IReadOnlyList<IYubiKeyDevice> FindAll() => Error is null ? Keys : throw Error;
        public void Dispose() => Disposals++;
    }

    // Real SDK device-info objects with no transport handles: no hardware is accessed.
    private static class DeviceProxy
    {
        public static IYubiKeyDevice Make(int? serial)
        {
            Yubico.Core.Logging.Log.Instance = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
            return new YubiKeyDevice(null, null, null, new YubiKeyDeviceInfo
            {
                SerialNumber = serial,
                FirmwareVersion = new FirmwareVersion(5, 5, 1),
            });
        }
    }
}
