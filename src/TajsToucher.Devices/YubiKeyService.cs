using System.ComponentModel;
using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;
using Yubico.YubiKey.Oath;
using Yubico.YubiKey.Piv;

namespace TajsToucher.Devices;

// Constructed only by the desktop Devices feature. Never used by the GPG proxy.
public sealed class YubiKeyService : IDeviceService
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<Guid, IYubiKeyDevice> devices = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Timer refreshTimer;
    private readonly IKeyDiscovery discovery;
    private ActiveTest? activeTest;
    private bool disposed;
    private bool initialized;

    public event Action<InventoryResult>? InventoryChanged;
    public event Action<DeviceSignal>? Signal;

    public YubiKeyService() : this(new SdkKeyDiscovery()) { }

    internal YubiKeyService(IKeyDiscovery discovery)
    {
        this.discovery = discovery;
        refreshTimer = new Timer(_state => { _ = RefreshInventoryAsync(); }, null, Timeout.Infinite, Timeout.Infinite);
        discovery.PresenceChanged += OnPresenceChanged;
    }

    public Task<InventoryResult> RefreshInventoryAsync() => Task.Run(async () =>
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return new InventoryResult([], DeviceOutcome.Unavailable);
            var found = discovery.FindAll();
            var removed = devices.Where(pair => !found.Any(key => SameDevice(key, pair.Value))).Select(pair => pair.Key).ToArray();
            foreach (var id in removed)
            {
                devices.Remove(id);
                Emit(new(Guid.NewGuid(), id, DateTimeOffset.UtcNow, DeviceSignalKind.Removed, DeviceOutcome.Removed));
            }
            foreach (var key in found)
            {
                var existing = devices.FirstOrDefault(pair => SameDevice(pair.Value, key));
                if (existing.Value is not null) devices[existing.Key] = key;
                else
                {
                    var id = Guid.NewGuid();
                    devices.Add(id, key);
                    if (initialized) Emit(new(Guid.NewGuid(), id, DateTimeOffset.UtcNow, DeviceSignalKind.Arrived, DeviceOutcome.Ready));
                }
            }
            initialized = true;
            var keys = devices.Select((pair, index) => new ConnectedKey(pair.Key,
                $"Key {index + 1} · {pair.Value.FormFactor}", pair.Value.FirmwareVersion.Major == 0 ? "unknown" : pair.Value.FirmwareVersion.ToString(),
                Capabilities(pair.Value.AvailableUsbCapabilities), Capabilities(pair.Value.EnabledUsbCapabilities),
                Capabilities(pair.Value.AvailableNfcCapabilities), Capabilities(pair.Value.EnabledNfcCapabilities),
                Supports(pair.Value, YubiKeyCapabilities.Fido2) && FirmwareAtLeast(pair.Value, 5, 5, 1))).ToArray();
            var result = new InventoryResult(keys, DeviceOutcome.Ready);
            PublishInventory(result);
            return result;
        }
        catch (Exception ex)
        {
            var result = new InventoryResult([], Classify(ex));
            PublishInventory(result);
            return result;
        }
        finally { gate.Release(); }
    });

    private void OnPresenceChanged(IYubiKeyDevice key, bool arrived)
    {
        var test = Volatile.Read(ref activeTest);
        if (!arrived && test is not null && SameDevice(key, test.Key)) test.Removed();
        try { refreshTimer.Change(400, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    public Task<IReadOnlyList<DeviceStatus>> ReadStatusAsync(Guid id) => Task.Run(async () =>
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed || !devices.TryGetValue(id, out var key))
                return (IReadOnlyList<DeviceStatus>)[new DeviceStatus("Device", DeviceOutcome.Removed, "Select a connected key.")];
            var results = new List<DeviceStatus>();
            Read("PIV", () => ReadPiv(key, results), results);
            Read("OATH", () =>
            {
                if (!Supports(key, YubiKeyCapabilities.Oath)) { results.Add(Unsupported("OATH")); return; }
                using var session = new OathSession(key);
                session.KeyCollector = _ => false;
                results.Add(new("OATH", DeviceOutcome.Ready, session.IsPasswordProtected ? "Password protected (not unlocked)." : "Not password protected."));
            }, results);
            Read("FIDO2", () =>
            {
                if (!Supports(key, YubiKeyCapabilities.Fido2)) { results.Add(Unsupported("FIDO2")); return; }
                using var session = new Fido2Session(key);
                session.KeyCollector = _ => false;
                var info = session.AuthenticatorInfo;
                var options = string.Join("; ", new[] { "clientPin", "uv", "rk", "alwaysUv" }.Select(option =>
                    $"{option}: {(info.Options is { } map && map.TryGetValue(option, out var value) ? value.ToString() : "unknown")}"));
                results.Add(new("FIDO2", DeviceOutcome.Ready, "Versions: " + string.Join(", ", info.Versions) + "; " + options));
                var response = session.Connection.SendCommand(new GetPinRetriesCommand());
                var retries = response.GetData();
                results.Add(new("FIDO2 PIN", DeviceOutcome.Ready,
                    $"Retries remaining (no PIN attempted). Power cycle required: {retries.Item2?.ToString() ?? "unknown"}.", retries.Item1 < 0 ? null : retries.Item1));
            }, results);
            foreach (var result in results.Where(r => r.RetriesRemaining is >= 0 and <= 2))
                Emit(new(Guid.NewGuid(), id, DateTimeOffset.UtcNow, DeviceSignalKind.LowRetries, DeviceOutcome.Ready));
            return results;
        }
        finally { gate.Release(); }
    });

    private static void ReadPiv(IYubiKeyDevice key, List<DeviceStatus> results)
    {
        if (!Supports(key, YubiKeyCapabilities.Piv) || !FirmwareAtLeast(key, 5, 3, 0))
        { results.Add(Unsupported("PIV metadata (requires firmware 5.3+)")); return; }
        using var session = new PivSession(key);
        session.KeyCollector = _ => false;
        var slots = new byte[] { PivSlot.Pin, PivSlot.Puk, PivSlot.Authentication, PivSlot.Signing, PivSlot.KeyManagement, PivSlot.CardAuthentication, PivSlot.Attestation }
            .Concat(Enumerable.Range(PivSlot.Retired1, 20).Select(slot => (byte)slot));
        foreach (var slot in slots)
        {
            var outcome = Read($"PIV {slot:X2}", () =>
            {
                var metadata = session.GetMetadata(slot);
                results.Add(new($"PIV {slot:X2}", DeviceOutcome.Ready,
                    slot is PivSlot.Pin or PivSlot.Puk ? (metadata.RetriesRemaining < 0 ? "Retries remaining: unknown (no secret attempted)." : "Retries remaining (no secret attempted).") :
                    $"Algorithm: {metadata.Algorithm}; PIN policy: {metadata.PinPolicy}; touch policy: {metadata.TouchPolicy} (not live activity).",
                    metadata.RetriesRemaining < 0 ? null : metadata.RetriesRemaining));
            }, results);
            if (outcome is DeviceOutcome.Removed or DeviceOutcome.Busy or DeviceOutcome.PermissionDenied or DeviceOutcome.TimedOut)
                break; // Do not repeat a failed transport read for every remaining slot.
        }
    }

    public Task<DeviceOutcome> IdentifyAsync(Guid id, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        await gate.WaitAsync().ConfigureAwait(false);
        var correlation = Guid.NewGuid();
        var outcome = DeviceOutcome.Failed;
        ActiveTest? test = null;
        try
        {
            if (disposed || !devices.TryGetValue(id, out var key)) return outcome = DeviceOutcome.Removed;
            if (!Supports(key, YubiKeyCapabilities.Fido2) || !FirmwareAtLeast(key, 5, 5, 1)) return outcome = DeviceOutcome.Unsupported;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            test = new ActiveTest(key, linked);
            Volatile.Write(ref activeTest, test);
            linked.Token.ThrowIfCancellationRequested();
            using var session = new Fido2Session(key);
            using var touch = new TouchRequestLifetime(linked.Token);
            session.KeyCollector = entry =>
            {
                if (entry.Request == KeyEntryRequest.TouchRequest)
                {
                    var cancel = entry.SignalUserCancel;
                    touch.Request(cancel is null ? null : () => cancel(), () =>
                        Emit(new(correlation, id, DateTimeOffset.UtcNow, DeviceSignalKind.TouchRequested, DeviceOutcome.Ready)));
                    return true;
                }
                if (entry.Request == KeyEntryRequest.Release)
                {
                    touch.Release();
                    Emit(new(correlation, id, DateTimeOffset.UtcNow, DeviceSignalKind.TouchReleased, DeviceOutcome.Ready));
                    return true;
                }
                return false; // Never collect PINs or credentials.
            };
            outcome = session.TryAuthenticatorSelection(out var response) ? DeviceOutcome.Ready :
                response.CtapStatus == CtapStatus.InvalidCommand ? DeviceOutcome.Unsupported : DeviceOutcome.Cancelled;
            return outcome;
        }
        catch (Exception ex) { return outcome = test?.WasRemoved == true ? DeviceOutcome.Removed : Classify(ex); }
        finally
        {
            Volatile.Write(ref activeTest, null);
            Emit(new(correlation, id, DateTimeOffset.UtcNow, DeviceSignalKind.TestFinished, outcome));
            gate.Release();
        }
    });

    private static bool FirmwareAtLeast(IYubiKeyDevice key, int major, int minor, int patch) =>
        Version.TryParse(key.FirmwareVersion.ToString(), out var version) && version >= new Version(major, minor, patch);
    internal static bool SameDevice(IYubiKeyDevice left, IYubiKeyDevice right) =>
        ReferenceEquals(left, right) || (left.SerialNumber is int serial && right.SerialNumber == serial);
    private static bool Supports(IYubiKeyDevice key, YubiKeyCapabilities capability) =>
        (((key.AvailableTransports & Transport.NfcSmartCard) != 0 ? key.EnabledNfcCapabilities : key.EnabledUsbCapabilities) & capability) != 0;
    private static string Capabilities(YubiKeyCapabilities capabilities) => capabilities == YubiKeyCapabilities.None
        ? "None reported (unknown if unsupported by firmware)" : capabilities.ToString();
    private static DeviceStatus Unsupported(string application) => new(application, DeviceOutcome.Unsupported, "Unavailable, disabled, or unsupported.");
    private static DeviceOutcome Read(string application, Action read, List<DeviceStatus> results)
    {
        try { read(); return DeviceOutcome.Ready; }
        catch (Exception ex)
        {
            var outcome = Classify(ex);
            results.Add(new(application, outcome, "Read did not complete; no credentials requested. Retry when other applications release the key."));
            return outcome;
        }
    }
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
            try { ((Action<DeviceSignal>)handler)(signal); } catch { /* Independent observers cannot break device operations. */ }
    }
    private void PublishInventory(InventoryResult result)
    {
        foreach (var handler in InventoryChanged?.GetInvocationList() ?? [])
            try { ((Action<InventoryResult>)handler)(result); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            await refreshTimer.DisposeAsync().ConfigureAwait(false);
            discovery.PresenceChanged -= OnPresenceChanged;
            discovery.Dispose();
            devices.Clear();
            InventoryChanged = null;
            Signal = null;
        }
        finally { gate.Release(); }
    }

    private sealed class ActiveTest(IYubiKeyDevice key, CancellationTokenSource cancellation)
    {
        private int removed;
        public IYubiKeyDevice Key { get; } = key;
        public bool WasRemoved => Volatile.Read(ref removed) != 0;
        public void Removed()
        {
            Interlocked.Exchange(ref removed, 1);
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
    }
}
