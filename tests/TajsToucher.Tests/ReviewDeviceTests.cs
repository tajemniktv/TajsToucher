using TajsToucher.Devices;
using Yubico.Core.Iso7816;
using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Piv;
using Yubico.YubiKey.Piv.Commands;

namespace TajsToucher.Tests;

[TestClass]
public sealed class ReviewDeviceTests
{
    [TestMethod]
    public async Task QueuedIdentifyCancelsWithoutReleasingAnotherOperationAndOtherKeysRemainUsable()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var source = new Source { Keys = [Key(1), Key(2)] };
        var backend = new Operations
        {
            OnIdentify = (_, token, _) => { Interlocked.Increment(ref calls); entered.TrySetResult(); release.Wait(token); return DeviceOutcome.Ready; },
        };
        await using var service = new YubiKeyService(source, backend);
        var keys = (await service.RefreshInventoryAsync()).Keys;
        var first = service.IdentifyAsync(keys[0].Id, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var cancel = new CancellationTokenSource();
            var queued = service.IdentifyAsync(keys[0].Id, cancel.Token);
            cancel.Cancel();
            Assert.AreEqual(DeviceOutcome.Cancelled, await queued.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(2, (await service.RefreshInventoryAsync().WaitAsync(TimeSpan.FromSeconds(1))).Keys.Count);
            Assert.AreEqual(DeviceOutcome.Ready, (await service.ReadStatusAsync(keys[1].Id).WaitAsync(TimeSpan.FromSeconds(1))).Single().Outcome);
        }
        finally { release.Set(); }
        Assert.AreEqual(DeviceOutcome.Ready, await first);
    }

    [TestMethod]
    public async Task KnownRemovalCancelsOnlyItsTestAndReconnectRestoresUsability()
    {
        var key = Key(1);
        var source = new Source { Keys = [key] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Operations { OnIdentify = (_, token, _) =>
        { entered.TrySetResult(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return DeviceOutcome.Ready; } };
        await using var service = new YubiKeyService(source, backend);
        var id = (await service.RefreshInventoryAsync()).Keys.Single().Id;
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var test = service.IdentifyAsync(id, safety.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        source.Announce(key, false);
        Assert.AreEqual(DeviceOutcome.Removed, await test.WaitAsync(TimeSpan.FromSeconds(1)));
        source.Keys = [Key(1)];
        await service.RefreshInventoryAsync();
        Assert.AreEqual(DeviceOutcome.Ready, (await service.ReadStatusAsync(id)).Single().Outcome);
    }

    [TestMethod]
    public void MostlyEmptyPivIsNormalButMissingPinMetadataIsNotAnEmptyKey()
    {
        var results = SdkKeyOperations.ReadPivSlots(slot => new GetMetadataResponse(new ResponseApdu(
            slot is PivSlot.Pin or PivSlot.Puk ? new byte[] { 6, 2, 3, 3, 0x90, 0 } : new byte[] { 0x6A, 0x88 }), slot), CancellationToken.None);
        Assert.AreEqual(27, results.Count);
        Assert.IsTrue(results.All(result => result.Outcome == DeviceOutcome.Ready));
        Assert.AreEqual(25, results.Count(result => result.Detail.StartsWith("Empty slot")));
        Assert.AreEqual(2, results.Count(result => result.RetriesRemaining == 3));
        var missing = SdkKeyOperations.ReadPivSlots(slot => new GetMetadataResponse(new ResponseApdu(new byte[] { 0x6A, 0x88 }), slot), CancellationToken.None);
        Assert.AreEqual(DeviceOutcome.Failed, missing[0].Outcome);
    }

    [TestMethod]
    [DataRow(CtapStatus.InvalidCommand, DeviceOutcome.Unsupported)]
    [DataRow(CtapStatus.UserActionTimeout, DeviceOutcome.TimedOut)]
    [DataRow(CtapStatus.ActionTimeout, DeviceOutcome.TimedOut)]
    [DataRow(CtapStatus.KeepAliveCancel, DeviceOutcome.Cancelled)]
    [DataRow(CtapStatus.OperationDenied, DeviceOutcome.Failed)]
    [DataRow((CtapStatus)0x7F, DeviceOutcome.Failed)]
    public void SelectionOutcomesAreExplicit(CtapStatus status, DeviceOutcome expected) =>
        Assert.AreEqual(expected, SdkKeyOperations.SelectionOutcome(false, status));

    [TestMethod]
    public void CapabilitiesOnlyCombineConnectedTransports()
    {
        Assert.IsTrue(SdkKeyOperations.Supports(Transport.UsbSmartCard | Transport.NfcSmartCard, YubiKeyCapabilities.Piv, YubiKeyCapabilities.Oath, YubiKeyCapabilities.Piv));
        Assert.IsTrue(SdkKeyOperations.Supports(Transport.UsbSmartCard | Transport.NfcSmartCard, YubiKeyCapabilities.Piv, YubiKeyCapabilities.Oath, YubiKeyCapabilities.Oath));
        Assert.IsFalse(SdkKeyOperations.Supports(Transport.UsbSmartCard, YubiKeyCapabilities.Piv, YubiKeyCapabilities.Oath, YubiKeyCapabilities.Oath));
        Assert.IsFalse(SdkKeyOperations.Supports(Transport.None, YubiKeyCapabilities.Piv, YubiKeyCapabilities.Oath, YubiKeyCapabilities.Piv));
    }

    [TestMethod]
    public void ExternalTouchCallbacksCanWaitForConcurrentRelease()
    {
        using var cancellation = new CancellationTokenSource();
        using var lifetime = new TouchRequestLifetime(cancellation.Token);
        void Callback() => Assert.IsTrue(Task.Run(lifetime.Release).Wait(TimeSpan.FromSeconds(1)), "Callback was invoked under the state lock.");
        lifetime.Request(Callback, Callback);
        cancellation.Cancel();
        lifetime.Request(Callback);
    }

    [TestMethod]
    public void LastDiscoveryLeaseOwnsShutdownAndRemainingLeaseKeepsEvents()
    {
        var source = new Source();
        var shared = new SharedKeyDiscovery(() => source);
        var first = shared.Acquire();
        using var second = shared.Acquire();
        var events = 0;
        second.PresenceChanged += (_, _) => events++;
        first.FindAll(); second.FindAll(); first.Dispose(); first.Dispose();
        Assert.AreEqual(0, source.Disposals);
        source.Announce(Key(1), true);
        Assert.AreEqual(1, events);
        second.Dispose();
        Assert.AreEqual(1, source.Disposals);
    }

    private static IYubiKeyDevice Key(int serial)
    {
        Yubico.Core.Logging.Log.Instance = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        return new YubiKeyDevice(null, null, null, new YubiKeyDeviceInfo { SerialNumber = serial, FirmwareVersion = new FirmwareVersion(5, 5, 1) });
    }
    private sealed class Source : IKeyDiscovery
    {
        public IReadOnlyList<IYubiKeyDevice> Keys { get; set; } = [];
        public int Disposals { get; private set; }
        public event Action<IYubiKeyDevice, bool>? PresenceChanged;
        public void Announce(IYubiKeyDevice key, bool arrived) => PresenceChanged?.Invoke(key, arrived);
        public IReadOnlyList<IYubiKeyDevice> FindAll() => Keys;
        public void Dispose() => Disposals++;
    }
    private sealed class Operations : IKeyOperations
    {
        public Func<IYubiKeyDevice, CancellationToken, Action<bool>, DeviceOutcome> OnIdentify { get; init; } = (_, _, _) => DeviceOutcome.Ready;
        public DeviceOutcome Identify(IYubiKeyDevice key, CancellationToken token, Action<bool> changed) => OnIdentify(key, token, changed);
        public IReadOnlyList<DeviceStatus> ReadStatus(IYubiKeyDevice key, CancellationToken token) => [new("Test", DeviceOutcome.Ready, "")];
    }
}
