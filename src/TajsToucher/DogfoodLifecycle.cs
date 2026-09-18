using System.Security.Cryptography;

namespace TajsToucher;

// Coordination is scoped to the installed path, not the process name. No configuration is written.
internal static class DogfoodLifecycle
{
    internal static string Identity(string path) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));

    internal static IDisposable? EnterOperation() => EnterOperation(Environment.ProcessPath!, TimeSpan.FromSeconds(60));

    internal static IDisposable? EnterOperation(string executable, TimeSpan timeout)
    {
        var directory = Path.GetDirectoryName(executable)!;
        // Only managed dogfood installations opt into the update protocol.
        var leasePath = Path.Combine(Path.GetDirectoryName(directory)!, ".TajsToucher-dogfood", "activity.lock");
        if (!File.Exists(leasePath)) return null;
        using var gate = new Mutex(false, @"Local\TajsToucher.Deploy." + Identity(executable));
        var elapsed = Stopwatch.StartNew();
        try { if (!gate.WaitOne(timeout)) throw new TimeoutException("Deployment is still active; retry signing after the update finishes."); }
        catch (AbandonedMutexException) { }
        try
        {
            while (true)
            {
                try { return new FileStream(leasePath, FileMode.Open, FileAccess.Read, FileShare.Read); }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
                {
                    // The file lease is shared across Windows sessions; Local mutexes are not.
                    // Never start GPG without protection while a deployment owns this lease.
                    if (elapsed.Elapsed >= timeout)
                        throw new TimeoutException("Deployment is still active; retry signing after the update finishes.", ex);
                    Thread.Sleep(50);
                }
            }
        }
        catch (IOException) { return null; } // Unavailable coordination, not an active exclusive owner.
        catch (UnauthorizedAccessException) { return null; }
        finally { gate.ReleaseMutex(); }
    }

    internal static string EventName(string kind, int pid) => $@"Local\TajsToucher.Dogfood.{kind}.{pid}";
}
