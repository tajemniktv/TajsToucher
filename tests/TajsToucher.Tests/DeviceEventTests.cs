using TajsToucher.Devices;

namespace TajsToucher.Tests;

[TestClass]
public sealed class DeviceEventTests
{
    [TestMethod]
    public async Task BrokerCoalescesNoticesAndIsolatesFailingDiagnostics()
    {
        var notices = new List<string>();
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = NotificationSettings.Defaults with { NotifyOnDevicePresence = true, RecordDiagnostics = true };
        var broker = new DeviceEventBroker(() => settings, _ => throw new IOException(), (message, _) =>
        { notices.Add(message); if (notices.Count == 2) processed.TrySetResult(); });
        var signal = new DeviceSignal(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DeviceSignalKind.Arrived, DeviceOutcome.Ready);
        broker.Publish(signal); broker.Publish(signal with { DeviceId = Guid.NewGuid() });
        broker.Publish(signal with { Timestamp = signal.Timestamp.AddSeconds(4) });
        try { await processed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { await broker.DisposeAsync(); }
        Assert.AreEqual(2, notices.Count);
    }

    [TestMethod]
    public async Task DiagnosticsContainNoDeviceIdentityAndSurviveFailedNotice()
    {
        var records = new List<string>();
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = NotificationSettings.Defaults with { NotifyOnLowRetries = true, RecordDiagnostics = true };
        var broker = new DeviceEventBroker(() => settings, signal =>
        { records.Add(DeviceEventBroker.Format(signal)); if (records.Count == 2) processed.TrySetResult(); }, (_, _) => throw new IOException());
        var signal = new DeviceSignal(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DeviceSignalKind.LowRetries, DeviceOutcome.Ready);
        broker.Publish(signal); broker.Publish(signal);
        try { await processed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { await broker.DisposeAsync(); }
        Assert.AreEqual(2, records.Count);
        Assert.IsFalse(records.Any(record => record.Contains(signal.DeviceId.ToString("N"))));
        StringAssert.Contains(records[0], "YubiKeySDK");
        Assert.IsNull(DeviceEventBroker.Notice(signal, NotificationSettings.Defaults));
        Assert.IsNull(DeviceEventBroker.Notice(signal with { Kind = DeviceSignalKind.TouchReleased }, settings));
    }
}
