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

    public TouchRequestLifetime(CancellationToken token) => registration = token.Register(Cancel);

    public bool Request(Action? signalCancel, Action? onRequest = null)
    {
        lock (sync)
        {
            if (finished) return false;
            cancel = signalCancel;
            if (cancelled) cancel?.Invoke();
            else onRequest?.Invoke();
            return true;
        }
    }

    public void Release()
    {
        lock (sync) cancel = null;
    }

    private void Cancel()
    {
        lock (sync)
        {
            cancelled = true;
            if (!finished) cancel?.Invoke();
        }
    }

    public void Dispose()
    {
        lock (sync) { finished = true; cancel = null; }
        registration.Dispose();
    }
}
