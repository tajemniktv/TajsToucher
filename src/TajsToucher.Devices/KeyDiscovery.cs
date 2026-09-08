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
    private static readonly SharedKeyDiscovery shared = new(() => new SdkDiscoverySource());
    private readonly IKeyDiscovery lease = shared.Acquire();
    public event Action<IYubiKeyDevice, bool>? PresenceChanged { add => lease.PresenceChanged += value; remove => lease.PresenceChanged -= value; }
    public IReadOnlyList<IYubiKeyDevice> FindAll() => lease.FindAll();
    public void Dispose() => lease.Dispose();
}

// All in-process SDK discovery clients lease the same owner. Only the final
// release may stop the SDK singleton, never an individual page/probe owner.
internal sealed class SharedKeyDiscovery(Func<IKeyDiscovery> create)
{
    private readonly object sync = new();
    private IKeyDiscovery? source;
    private int references;

    public IKeyDiscovery Acquire()
    {
        lock (sync)
        {
            source ??= create();
            references++;
            return new Lease(this, source);
        }
    }
    private void Release()
    {
        lock (sync)
        {
            if (--references == 0)
            {
                try { source!.Dispose(); }
                finally { source = null; }
            }
        }
    }
    private sealed class Lease : IKeyDiscovery
    {
        private readonly SharedKeyDiscovery owner;
        private readonly IKeyDiscovery source;
        private int disposed;
        public event Action<IYubiKeyDevice, bool>? PresenceChanged;
        public Lease(SharedKeyDiscovery owner, IKeyDiscovery source)
        {
            this.owner = owner; this.source = source;
            source.PresenceChanged += OnPresence;
        }
        private void OnPresence(IYubiKeyDevice key, bool arrived)
        {
            foreach (var handler in PresenceChanged?.GetInvocationList() ?? [])
                try { ((Action<IYubiKeyDevice, bool>)handler)(key, arrived); } catch { }
        }
        public IReadOnlyList<IYubiKeyDevice> FindAll()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return source.FindAll();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            source.PresenceChanged -= OnPresence;
            PresenceChanged = null;
            owner.Release();
        }
    }
}

internal sealed class SdkDiscoverySource : IKeyDiscovery
{
    private readonly object sync = new();
    private YubiKeyDeviceListener? listener;
    public event Action<IYubiKeyDevice, bool>? PresenceChanged;

    public SdkDiscoverySource()
    {
        // Never let SDK logs expose device identifiers through console/config sinks.
        Yubico.Core.Logging.Log.Instance = NullLoggerFactory.Instance;
    }

    public IReadOnlyList<IYubiKeyDevice> FindAll()
    {
        lock (sync)
        {
            if (listener is null)
            {
                listener = YubiKeyDeviceListener.Instance;
                listener.Arrived += Arrived;
                listener.Removed += Removed;
            }
            return YubiKeyDevice.FindAll().ToArray();
        }
    }

    private void Arrived(object? sender, YubiKeyDeviceEventArgs args) => PresenceChanged?.Invoke(args.Device, true);
    private void Removed(object? sender, YubiKeyDeviceEventArgs args) => PresenceChanged?.Invoke(args.Device, false);

    public void Dispose()
    {
        lock (sync)
        {
            if (listener is null) return;
            listener.Arrived -= Arrived;
            listener.Removed -= Removed;
            YubiKeyDeviceListener.StopListening();
            listener = null;
            PresenceChanged = null;
        }
    }
}
