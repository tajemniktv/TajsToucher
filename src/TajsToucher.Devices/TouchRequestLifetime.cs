namespace TajsToucher.Devices;

// SDK touch callbacks can outlive TryAuthenticatorSelection. Late Release/Touch
// callbacks must never register against a disposed token or revive a finished test.
internal sealed class TouchRequestLifetime : IDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenRegistration registration;
    private Action? cancel;
    private bool cancelled;
    private bool finished;
    private readonly Queue<(Action Callback, long? RequestGeneration)> callbacks = new();
    private long generation;
    private bool draining;

    public TouchRequestLifetime(CancellationToken token) => registration = token.Register(Cancel);

    public bool Request(Action? signalCancel, Action? onRequest = null)
    {
        lock (sync)
        {
            if (finished) return false;
            cancel = signalCancel;
            var callback = cancelled ? cancel : onRequest;
            if (callback is not null) callbacks.Enqueue((callback, cancelled ? null : generation));
        }
        Drain();
        return true;
    }

    public void Release()
    {
        lock (sync) { cancel = null; generation++; }
    }

    private void Cancel()
    {
        lock (sync)
        {
            cancelled = true;
            generation++;
            if (!finished && cancel is not null) callbacks.Enqueue((cancel, null));
        }
        Drain();
    }

    private void Drain()
    {
        lock (sync)
        {
            if (draining) return;
            draining = true;
        }
        // One dispatcher orders request and cancellation callbacks without holding
        // the state lock across external code. Cancellation invalidates queued
        // requests; an already executing callback finishes before SDK cancellation.
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        while (true)
        {
            Action callback;
            lock (sync)
            {
                if (callbacks.Count == 0) { draining = false; break; }
                var pending = callbacks.Dequeue();
                if (finished || (pending.RequestGeneration is long version && (cancelled || version != generation))) continue;
                callback = pending.Callback;
            }
            try { callback(); }
            catch (Exception exception)
            {
                failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            }
        }
        failure?.Throw();
    }

    public void Dispose()
    {
        lock (sync) { finished = true; cancel = null; generation++; callbacks.Clear(); }
        registration.Dispose();
    }
}
