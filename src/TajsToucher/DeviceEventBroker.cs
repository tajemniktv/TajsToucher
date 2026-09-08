using System.Threading.Channels;
using TajsToucher.Devices;

namespace TajsToucher;

// A bounded, desktop-only consumer. SDK callbacks never wait for notifications or disk I/O.
internal sealed class DeviceEventBroker : IAsyncDisposable
{
    private readonly Channel<DeviceSignal> queue = Channel.CreateBounded<DeviceSignal>(new BoundedChannelOptions(64)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task worker;
    private readonly Func<NotificationSettings> settings;
    private readonly Action<DeviceSignal> diagnostic;
    private readonly Action<string, NotificationSettings> notify;
    private readonly Dictionary<DeviceSignalKind, DateTimeOffset> lastNotice = new();

    public DeviceEventBroker() : this(() => new ConfigurationStore().LoadNotificationSettings(),
        signal => new DiagnosticEventSink(DiagnosticEventSink.DefaultDirectory).Append(Format(signal)),
        App.ShowDeviceNotice) { }

    internal DeviceEventBroker(Func<NotificationSettings> settings, Action<DeviceSignal> diagnostic,
        Action<string, NotificationSettings> notify)
    {
        this.settings = settings; this.diagnostic = diagnostic; this.notify = notify;
        worker = Task.Run(ConsumeAsync);
    }

    public void Publish(DeviceSignal signal) => queue.Writer.TryWrite(signal);

    private async Task ConsumeAsync()
    {
        await foreach (var signal in queue.Reader.ReadAllAsync())
        {
            NotificationSettings current;
            try { current = settings(); } catch { continue; }
            if (current.RecordDiagnostics)
                try { diagnostic(signal); } catch { /* Other sinks remain independent. */ }
            var message = Notice(signal, current);
            if (message is null) continue;
            // Presence noise is coalesced across keys; low-retry notices repeat at most every five minutes.
            var delay = signal.Kind == DeviceSignalKind.LowRetries ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(3);
            if (lastNotice.TryGetValue(signal.Kind, out var previous) && signal.Timestamp - previous < delay) continue;
            lastNotice[signal.Kind] = signal.Timestamp;
            try { notify(message, current); } catch { }
        }
    }

    internal static string? Notice(DeviceSignal signal, NotificationSettings settings) => signal.Kind switch
    {
        DeviceSignalKind.Arrived when settings.NotifyOnDevicePresence => "A YubiKey connected. Presence does not imply a usable credential.",
        DeviceSignalKind.Removed when settings.NotifyOnDevicePresence => "A YubiKey disconnected. No association with other applications' operations is inferred.",
        DeviceSignalKind.LowRetries when settings.NotifyOnLowRetries => "An explicit status read found two or fewer PIN/PUK retries remaining. Check Devices for details; no PIN was attempted.",
        _ => null,
    };

    internal static string Format(DeviceSignal signal) => string.Join('\t',
        signal.Timestamp.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        signal.CorrelationId.ToString("N"), "YubiKeySDK", signal.Kind, signal.Outcome);

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        await worker.ConfigureAwait(false);
    }
}
