using System.Security.Cryptography;

namespace TajsToucher;

// Coordination is scoped to the installed path, not the process name. No configuration is written.
internal static class DogfoodLifecycle
{
    internal static string Identity(string path) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));

    internal static IDisposable? EnterOperation()
    {
        var executable = Environment.ProcessPath!;
        var directory = Path.GetDirectoryName(executable)!;
        // Only managed dogfood installations opt into the update protocol.
        var leasePath = Path.Combine(Path.GetDirectoryName(directory)!, ".TajsToucher-dogfood", "activity.lock");
        if (!File.Exists(leasePath)) return null;
        using var gate = new Mutex(false, @"Local\TajsToucher.Deploy." + Identity(executable));
        try { gate.WaitOne(); }
        catch (AbandonedMutexException) { }
        try
        {
            return new FileStream(leasePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException) { return null; } // Fail open; deploy also refuses non-tray processes.
        catch (UnauthorizedAccessException) { return null; }
        finally { gate.ReleaseMutex(); }
    }

    internal static string EventName(string kind, int pid) => $@"Local\TajsToucher.Dogfood.{kind}.{pid}";
}
