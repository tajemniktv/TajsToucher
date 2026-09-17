namespace TajsToucher;

// A bounded, opt-in busy heuristic, not a hardware touch event source.
// GETATTR reads policy through scdaemon; unlike LEARN it does not import keys.
internal sealed class GpgTouchObservation : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly string eventName = "Local\\TajsToucher.TouchWait." + Guid.NewGuid().ToString("N");
    private readonly EventWaitHandle ended;
    private readonly Task worker;
    private readonly Action<string> publish;
    private int disposed;
    internal Task Completion => worker;

    internal GpgTouchObservation(string agent, Action<string>? publish = null)
    {
        this.publish = publish ?? (name =>
        {
            if (Environment.ProcessPath is { } executable)
                DetachedProcessLauncher.TryLaunch(executable, ["--touch-wait", name], HelperDispatch.TouchWait);
        });
        ended = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        worker = Task.Run(() => ObserveAsync(agent));
    }

    internal static bool IsEligible(IReadOnlyList<string> args) =>
        OperationClassifier.Classify(args) is OpenPgpOperation.Signing or OpenPgpOperation.SigningAndEncryption &&
        // Do not probe the default agent for a caller using a different configuration.
        !args.Any(a => a.StartsWith("--homedir", StringComparison.Ordinal) ||
                       a.StartsWith("--options", StringComparison.Ordinal) ||
                       a == "--no-options");

    internal static GpgTouchObservation? TryStart(string gpg, IReadOnlyList<string> args)
    {
        try
        {
            var settings = new ConfigurationStore().LoadNotificationSettings();
            if (!settings.ObserveSigningTouchWait || !settings.NotifyOnSigning || !IsEligible(args)) return null;
            var agent = Path.Combine(Path.GetDirectoryName(gpg)!, "gpg-connect-agent.exe");
            return File.Exists(agent) ? new GpgTouchObservation(agent) : null;
        }
        catch { return null; } // Optional observation never owns signing.
    }

    private async Task ObserveAsync(string agent)
    {
        try
        {
            await Task.Delay(500, stopping.Token).ConfigureAwait(false);
            // One probe per invocation: no continuous polling, no synthetic signing.
            var start = new ProcessStartInfo(agent)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("--no-autostart");
            start.ArgumentList.Add("SCD GETATTR UIF-1");
            start.ArgumentList.Add("/bye");
            using var process = Process.Start(start);
            if (process is null) return;
            process.StandardInput.Close();
            using var cancel = stopping.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            });
            // Drain without retaining identifiers or protocol output.
            var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            var completion = process.WaitForExitAsync();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            lifetime.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                if (await Task.WhenAny(completion, Task.Delay(600, lifetime.Token)).ConfigureAwait(false) != completion)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (!completion.IsCompleted) publish(eventName);
                }
                await completion.WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                ended.Set();
                try { if (!process.HasExited) process.Kill(); } catch { }
            }
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        catch { /* Probe denial, timeout and cancellation mean unknown, never failed signing. */ }
        finally { ended.Set(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ended.Set();
        stopping.Cancel();
        // Never hold Git's streams open to wait for an optional probe.
        _ = worker.ContinueWith(_ => { ended.Dispose(); stopping.Dispose(); }, TaskScheduler.Default);
    }
}
