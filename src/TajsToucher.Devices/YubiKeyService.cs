using System.ComponentModel;
using Yubico.YubiKey;

namespace TajsToucher.Devices;

// Inventory synchronization never covers SDK sessions or human touch waits.
public sealed class YubiKeyService : IDeviceService
{
    private readonly object sync = new();
    private readonly SemaphoreSlim discoveryGate = new(1);
    private readonly Dictionary<Guid, DeviceEntry> devices = new();
    private readonly HashSet<Task> running = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Timer refreshTimer;
    private readonly IKeyDiscovery discovery;
    private readonly IKeyOperations operations;
    private bool initialized;
    private Task? disposal;

    public event Action<InventoryResult>? InventoryChanged;
    public event Action<DeviceSignal>? Signal;

    public YubiKeyService() : this(new SdkKeyDiscovery()) { }
    internal YubiKeyService(IKeyDiscovery discovery, IKeyOperations? operations = null)
    {
        this.discovery = discovery;
        this.operations = operations ?? new SdkKeyOperations();
        refreshTimer = new Timer(_state => { _ = RefreshInventoryAsync(); }, null, Timeout.Infinite, Timeout.Infinite);
        discovery.PresenceChanged += OnPresenceChanged;
    }

    public Task<InventoryResult> RefreshInventoryAsync() => Task.Run(async () =>
    {
        await discoveryGate.WaitAsync().ConfigureAwait(false);
        InventoryResult result;
        var signals = new List<DeviceSignal>();
        var removed = new List<DeviceEntry>();
        try
        {
            if (stopping.IsCancellationRequested) return new InventoryResult([], DeviceOutcome.Unavailable);
            var found = discovery.FindAll();
            lock (sync)
            {
                foreach (var pair in devices.Where(pair => !found.Any(key => SameDevice(key, pair.Value.Key))).ToArray())
                {
                    devices.Remove(pair.Key);
                    removed.Add(pair.Value);
                    signals.Add(new(Guid.NewGuid(), pair.Key, DateTimeOffset.UtcNow, DeviceSignalKind.Removed, DeviceOutcome.Removed));
                }
                foreach (var key in found)
                {
                    var existing = devices.FirstOrDefault(pair => SameDevice(pair.Value.Key, key));
                    if (existing.Value is not null)
                    {
                        if (existing.Value.Removed.IsCancellationRequested) devices[existing.Key] = new DeviceEntry(key);
                        else existing.Value.Key = key;
                    }
                    else
                    {
                        var id = Guid.NewGuid();
                        devices.Add(id, new DeviceEntry(key));
                        if (initialized) signals.Add(new(Guid.NewGuid(), id, DateTimeOffset.UtcNow, DeviceSignalKind.Arrived, DeviceOutcome.Ready));
                    }
                }
                initialized = true;
                result = new InventoryResult(devices.Select((pair, index) => Snapshot(pair.Key, pair.Value.Key, index)).ToArray(), DeviceOutcome.Ready);
            }
        }
        catch (Exception ex) { result = new InventoryResult([], Classify(ex)); }
        finally { discoveryGate.Release(); }
        foreach (var entry in removed) entry.Removed.Cancel();
        foreach (var signal in signals) Emit(signal);
        PublishInventory(result);
        return result;
    });

    private static ConnectedKey Snapshot(Guid id, IYubiKeyDevice key, int index) => new(id,
        $"Key {index + 1} · {key.FormFactor}", key.FirmwareVersion.Major == 0 ? "unknown" : key.FirmwareVersion.ToString(),
        Capabilities(key.AvailableUsbCapabilities), Capabilities(key.EnabledUsbCapabilities),
        Capabilities(key.AvailableNfcCapabilities), Capabilities(key.EnabledNfcCapabilities),
        SdkKeyOperations.Supports(key, YubiKeyCapabilities.Fido2) && SdkKeyOperations.FirmwareAtLeast(key, 5, 5, 1));

    private void OnPresenceChanged(IYubiKeyDevice key, bool arrived)
    {
        DeviceEntry[] removed = [];
        if (!arrived)
        {
            lock (sync) removed = devices.Values.Where(entry => SameDevice(key, entry.Key)).ToArray();
            foreach (var entry in removed) entry.Removed.Cancel();
        }
        try { refreshTimer.Change(400, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    public Task<IReadOnlyList<DeviceStatus>> ReadStatusAsync(Guid id) => Track<IReadOnlyList<DeviceStatus>>(async () =>
    {
        DeviceEntry? entry = null;
        var acquired = false;
        try
        {
            stopping.Token.ThrowIfCancellationRequested();
            lock (sync) devices.TryGetValue(id, out entry);
            if (entry is null) return [new("Device", DeviceOutcome.Removed, "Select a connected key.")];
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, entry.Removed.Token);
            await entry.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            acquired = true;
            IYubiKeyDevice key;
            lock (sync) key = entry.Key;
            var results = operations.ReadStatus(key, linked.Token);
            if (results.Any(result => result.RetriesRemaining is >= 0 and <= 2))
                Emit(new(Guid.NewGuid(), id, DateTimeOffset.UtcNow, DeviceSignalKind.LowRetries, DeviceOutcome.Ready));
            return results;
        }
        catch (Exception ex)
        {
            return [new("Device", entry?.Removed.IsCancellationRequested == true ? DeviceOutcome.Removed : Classify(ex),
                "Read did not complete; no credentials requested.")];
        }
        finally { if (acquired) entry!.Gate.Release(); }
    });

    public Task<DeviceOutcome> IdentifyAsync(Guid id, CancellationToken cancellationToken) => Track(async () =>
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        DeviceEntry? entry = null;
        var acquired = false;
        var finished = 0;
        var correlation = Guid.NewGuid();
        var outcome = DeviceOutcome.Failed;
        try
        {
            caller.Token.ThrowIfCancellationRequested();
            lock (sync) devices.TryGetValue(id, out entry);
            if (entry is null) return outcome = DeviceOutcome.Removed;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token, entry.Removed.Token);
            await entry.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            acquired = true;
            IYubiKeyDevice key;
            lock (sync) key = entry.Key;
            return outcome = operations.Identify(key, linked.Token, requested =>
            {
                if (Volatile.Read(ref finished) == 0)
                    Emit(new(correlation, id, DateTimeOffset.UtcNow,
                        requested ? DeviceSignalKind.TouchRequested : DeviceSignalKind.TouchReleased, DeviceOutcome.Ready));
            });
        }
        catch (Exception ex) { return outcome = entry?.Removed.IsCancellationRequested == true ? DeviceOutcome.Removed : Classify(ex); }
        finally
        {
            Volatile.Write(ref finished, 1);
            Emit(new(correlation, id, DateTimeOffset.UtcNow, DeviceSignalKind.TestFinished, outcome));
            if (acquired) entry!.Gate.Release();
        }
    });

    private Task<T> Track<T>(Func<Task<T>> action)
    {
        lock (sync)
        {
            var task = Task.Run(action);
            running.Add(task);
            _ = task.ContinueWith(completed => { lock (sync) running.Remove(completed); }, TaskScheduler.Default);
            return task;
        }
    }

    internal static bool SameDevice(IYubiKeyDevice left, IYubiKeyDevice right) =>
        ReferenceEquals(left, right) || (left.SerialNumber is int serial && right.SerialNumber == serial);
    private static string Capabilities(YubiKeyCapabilities capabilities) => capabilities == YubiKeyCapabilities.None
        ? "None reported (unknown if unsupported by firmware)" : capabilities.ToString();

    public static DeviceOutcome Classify(Exception exception) => exception switch
    {
        // HRESULTs retained by the pinned SDK's SCardException and Windows HID layer.
        { HResult: unchecked((int)0x8010000B) or unchecked((int)0x80070020) } => DeviceOutcome.Busy,
        { HResult: unchecked((int)0x80100069) or unchecked((int)0x8010000C) or unchecked((int)0x80100017) or unchecked((int)0x8007048F) } => DeviceOutcome.Removed,
        { HResult: unchecked((int)0x80100027) or unchecked((int)0x80070005) } => DeviceOutcome.PermissionDenied,
        { HResult: unchecked((int)0x8010000A) } => DeviceOutcome.TimedOut,
        UnauthorizedAccessException => DeviceOutcome.PermissionDenied,
        Win32Exception { NativeErrorCode: 5 } => DeviceOutcome.PermissionDenied,
        OperationCanceledException => DeviceOutcome.Cancelled,
        TimeoutException => DeviceOutcome.TimedOut,
        NotSupportedException => DeviceOutcome.Unsupported,
        DllNotFoundException or FileNotFoundException or BadImageFormatException => DeviceOutcome.Unavailable,
        _ => DeviceOutcome.Failed,
    };


    private void Emit(DeviceSignal signal)
    {
        foreach (var handler in Signal?.GetInvocationList() ?? [])
            try { ((Action<DeviceSignal>)handler)(signal); } catch { }
    }
    private void PublishInventory(InventoryResult result)
    {
        foreach (var handler in InventoryChanged?.GetInvocationList() ?? [])
            try { ((Action<InventoryResult>)handler)(result); } catch { }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync) return new ValueTask(disposal ??= Task.Run(DisposeCoreAsync));
    }
    private async Task DisposeCoreAsync()
    {
        stopping.Cancel();
        await refreshTimer.DisposeAsync().ConfigureAwait(false);
        await discoveryGate.WaitAsync().ConfigureAwait(false);
        Task[] pending;
        try
        {
            lock (sync)
            {
                devices.Clear();
                pending = running.ToArray();
            }
            discovery.PresenceChanged -= OnPresenceChanged;
            discovery.Dispose();
        }
        finally { discoveryGate.Release(); }
        await Task.WhenAll(pending).ConfigureAwait(false);
        InventoryChanged = null;
        Signal = null;
    }

    private sealed class DeviceEntry(IYubiKeyDevice key)
    {
        public IYubiKeyDevice Key { get; set; } = key;
        public SemaphoreSlim Gate { get; } = new(1);
        public CancellationTokenSource Removed { get; } = new();
    }
}
