using Microsoft.Extensions.Logging.Abstractions;
using Yubico.YubiKey;

namespace TajsToucher.Devices;

internal interface IKeyDiscovery : IDisposable
{
    event Action<IYubiKeyDevice, bool>? PresenceChanged;
    IReadOnlyList<IYubiKeyDevice> FindAll();
}

internal sealed class SdkKeyDiscovery : IKeyDiscovery
{
    private YubiKeyDeviceListener? listener;
    public event Action<IYubiKeyDevice, bool>? PresenceChanged;

    public SdkKeyDiscovery()
    {
        // Never let SDK logs expose device identifiers through console/config sinks.
        Yubico.Core.Logging.Log.Instance = NullLoggerFactory.Instance;
    }

    public IReadOnlyList<IYubiKeyDevice> FindAll()
    {
        if (listener is null)
        {
            listener = YubiKeyDeviceListener.Instance;
            listener.Arrived += Arrived;
            listener.Removed += Removed;
        }
        return YubiKeyDevice.FindAll().ToArray();
    }

    private void Arrived(object? sender, YubiKeyDeviceEventArgs args) => PresenceChanged?.Invoke(args.Device, true);
    private void Removed(object? sender, YubiKeyDeviceEventArgs args) => PresenceChanged?.Invoke(args.Device, false);

    public void Dispose()
    {
        if (listener is null) return;
        listener.Arrived -= Arrived;
        listener.Removed -= Removed;
        YubiKeyDeviceListener.StopListening();
        listener = null;
        PresenceChanged = null;
    }
}
