namespace TajsToucher.Devices;

public sealed record ConnectedKey(Guid Id, string Label, string Firmware, string UsbAvailable,
    string UsbEnabled, string NfcAvailable, string NfcEnabled, bool CanIdentify);

public enum DeviceOutcome { Ready, Unsupported, PermissionDenied, Removed, Busy, Cancelled, TimedOut, Unavailable, Failed }
public sealed record DeviceStatus(string Application, DeviceOutcome Outcome, string Detail, int? RetriesRemaining = null);
public enum DeviceSignalKind { Arrived, Removed, TouchRequested, TouchReleased, TestFinished, LowRetries }
// No serial number, path, credential, account label or SDK exception text crosses this boundary.
public sealed record DeviceSignal(Guid CorrelationId, Guid DeviceId, DateTimeOffset Timestamp,
    DeviceSignalKind Kind, DeviceOutcome Outcome);
public sealed record InventoryResult(IReadOnlyList<ConnectedKey> Keys, DeviceOutcome Outcome);

public interface IDeviceService : IAsyncDisposable
{
    event Action<InventoryResult>? InventoryChanged;
    event Action<DeviceSignal>? Signal;
    Task<InventoryResult> RefreshInventoryAsync();
    Task<IReadOnlyList<DeviceStatus>> ReadStatusAsync(Guid id);
    Task<DeviceOutcome> IdentifyAsync(Guid id, CancellationToken cancellationToken);
}
