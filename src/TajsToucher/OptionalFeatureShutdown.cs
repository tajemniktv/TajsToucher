namespace TajsToucher;

internal static class OptionalFeatureShutdown
{
    public static async Task<bool> DisposeAsync(IAsyncDisposable feature, TimeSpan timeout)
    {
        // A third-party DisposeAsync may block even before returning its ValueTask.
        // Keep both that call and later faults off the UI thread, including after timeout.
        var disposal = Task.Run(async () =>
        {
            try { await feature.DisposeAsync().ConfigureAwait(false); }
            catch { /* Optional cleanup cannot prevent process exit. */ }
        });
        try { await disposal.WaitAsync(timeout).ConfigureAwait(false); return true; }
        catch (TimeoutException) { return false; }
    }
}
