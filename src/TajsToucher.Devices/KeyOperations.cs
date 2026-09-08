using Yubico.YubiKey;
using Yubico.YubiKey.Fido2;
using Yubico.YubiKey.Fido2.Commands;
using Yubico.YubiKey.Oath;
using Yubico.YubiKey.Piv;
using Yubico.YubiKey.Piv.Commands;

namespace TajsToucher.Devices;

internal interface IKeyOperations
{
    IReadOnlyList<DeviceStatus> ReadStatus(IYubiKeyDevice key, CancellationToken token);
    DeviceOutcome Identify(IYubiKeyDevice key, CancellationToken token, Action<bool> touchChanged);
}

internal sealed class SdkKeyOperations : IKeyOperations
{
    public IReadOnlyList<DeviceStatus> ReadStatus(IYubiKeyDevice key, CancellationToken token)
    {
        var results = new List<DeviceStatus>();
        Read("PIV", () =>
        {
            token.ThrowIfCancellationRequested();
            if (!Supports(key, YubiKeyCapabilities.Piv) || !FirmwareAtLeast(key, 5, 3, 0))
            { results.Add(Unsupported("PIV metadata (requires firmware 5.3+)")); return; }
            using var session = new PivSession(key) { KeyCollector = _ => false };
            results.AddRange(ReadPivSlots(slot => session.Connection.SendCommand(new GetMetadataCommand(slot)), token));
        }, results);
        Read("OATH", () =>
        {
            token.ThrowIfCancellationRequested();
            if (!Supports(key, YubiKeyCapabilities.Oath)) { results.Add(Unsupported("OATH")); return; }
            using var session = new OathSession(key) { KeyCollector = _ => false };
            results.Add(new("OATH", DeviceOutcome.Ready, session.IsPasswordProtected ? "Password protected (not unlocked)." : "Not password protected."));
        }, results);
        Read("FIDO2", () =>
        {
            token.ThrowIfCancellationRequested();
            if (!Supports(key, YubiKeyCapabilities.Fido2)) { results.Add(Unsupported("FIDO2")); return; }
            using var session = new Fido2Session(key) { KeyCollector = _ => false };
            var info = session.AuthenticatorInfo;
            var options = string.Join("; ", new[] { "clientPin", "uv", "rk", "alwaysUv" }.Select(option =>
                $"{option}: {(info.Options is { } map && map.TryGetValue(option, out var value) ? value.ToString() : "unknown")}"));
            results.Add(new("FIDO2", DeviceOutcome.Ready, "Versions: " + string.Join(", ", info.Versions) + "; " + options));
            var retries = session.Connection.SendCommand(new GetPinRetriesCommand()).GetData();
            results.Add(new("FIDO2 PIN", DeviceOutcome.Ready,
                $"Retries remaining (no PIN attempted). Power cycle required: {retries.Item2?.ToString() ?? "unknown"}.", retries.Item1 < 0 ? null : retries.Item1));
        }, results);
        return results;
    }

    internal static IReadOnlyList<DeviceStatus> ReadPivSlots(Func<byte, GetMetadataResponse> read, CancellationToken token)
    {
        var results = new List<DeviceStatus>();
        var slots = new byte[] { PivSlot.Pin, PivSlot.Puk, PivSlot.Authentication, PivSlot.Signing, PivSlot.KeyManagement, PivSlot.CardAuthentication, PivSlot.Attestation }
            .Concat(Enumerable.Range(PivSlot.Retired1, 20).Select(slot => (byte)slot));
        foreach (var slot in slots)
        {
            token.ThrowIfCancellationRequested();
            var outcome = Read($"PIV {slot:X2}", () =>
            {
                var response = read(slot);
                if (response.Status == ResponseStatus.NoData && slot is not (PivSlot.Pin or PivSlot.Puk))
                {
                    results.Add(new($"PIV {slot:X2}", DeviceOutcome.Ready, "Empty slot (no key metadata)."));
                    return;
                }
                var metadata = response.GetData();
                results.Add(new($"PIV {slot:X2}", DeviceOutcome.Ready,
                    slot is PivSlot.Pin or PivSlot.Puk ? (metadata.RetriesRemaining < 0 ? "Retries remaining: unknown (no secret attempted)." : "Retries remaining (no secret attempted).") :
                    $"Algorithm: {metadata.Algorithm}; PIN policy: {metadata.PinPolicy}; touch policy: {metadata.TouchPolicy} (not live activity).",
                    metadata.RetriesRemaining < 0 ? null : metadata.RetriesRemaining));
            }, results);
            if (outcome is DeviceOutcome.Removed or DeviceOutcome.Busy or DeviceOutcome.PermissionDenied or DeviceOutcome.TimedOut)
                break;
        }
        return results;
    }

    public DeviceOutcome Identify(IYubiKeyDevice key, CancellationToken token, Action<bool> touchChanged)
    {
        if (!Supports(key, YubiKeyCapabilities.Fido2) || !FirmwareAtLeast(key, 5, 5, 1)) return DeviceOutcome.Unsupported;
        token.ThrowIfCancellationRequested();
        using var session = new Fido2Session(key);
        using var touch = new TouchRequestLifetime(token);
        session.KeyCollector = entry =>
        {
            if (entry.Request == KeyEntryRequest.TouchRequest)
            {
                var cancel = entry.SignalUserCancel;
                touch.Request(cancel is null ? null : () => cancel(), () => touchChanged(true));
                return true;
            }
            if (entry.Request == KeyEntryRequest.Release)
            {
                touch.Release();
                touchChanged(false);
                return true;
            }
            return false;
        };
        var success = session.TryAuthenticatorSelection(out var response);
        return SelectionOutcome(success, response.CtapStatus);
    }

    internal static DeviceOutcome SelectionOutcome(bool success, CtapStatus status) => success ? DeviceOutcome.Ready : status switch
    {
        CtapStatus.InvalidCommand => DeviceOutcome.Unsupported,
        CtapStatus.ActionTimeout or CtapStatus.UserActionTimeout => DeviceOutcome.TimedOut,
        CtapStatus.KeepAliveCancel => DeviceOutcome.Cancelled,
        // A denial is not necessarily caller cancellation.
        CtapStatus.OperationDenied => DeviceOutcome.Failed,
        _ => DeviceOutcome.Failed,
    };

    internal static bool FirmwareAtLeast(IYubiKeyDevice key, int major, int minor, int patch) =>
        Version.TryParse(key.FirmwareVersion.ToString(), out var version) && version >= new Version(major, minor, patch);
    internal static bool Supports(IYubiKeyDevice key, YubiKeyCapabilities capability)
        => Supports(key.AvailableTransports, key.EnabledUsbCapabilities, key.EnabledNfcCapabilities, capability);

    internal static bool Supports(Transport transports, YubiKeyCapabilities usbCapabilities, YubiKeyCapabilities nfcCapabilities, YubiKeyCapabilities capability)
    {
        const Transport usb = Transport.HidKeyboard | Transport.HidFido | Transport.UsbSmartCard;
        var enabled = (transports & usb) != 0 ? usbCapabilities : YubiKeyCapabilities.None;
        if ((transports & Transport.NfcSmartCard) != 0) enabled |= nfcCapabilities;
        return (enabled & capability) != 0;
    }
    private static DeviceStatus Unsupported(string application) => new(application, DeviceOutcome.Unsupported, "Unavailable, disabled, or unsupported.");
    private static DeviceOutcome Read(string application, Action read, List<DeviceStatus> results)
    {
        try { read(); return DeviceOutcome.Ready; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var outcome = YubiKeyService.Classify(ex);
            results.Add(new(application, outcome, "Read did not complete; no credentials requested. Retry when other applications release the key."));
            return outcome;
        }
    }
}
