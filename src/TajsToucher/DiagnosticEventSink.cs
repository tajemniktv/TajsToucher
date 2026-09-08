using System.Globalization;
using System.Security.Cryptography;

namespace TajsToucher;

internal sealed class DiagnosticEventSink(string directory) : IOperationEventSink
{
    internal const int MaximumFileBytes = 64 * 1024;
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TajsToucher", "Diagnostics");

    public void Publish(OperationEvent operationEvent)
    {
        if (!operationEvent.IsValid) return;
        Append(Format(operationEvent));
    }

    internal void Append(string metadataLine)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var mutexSuffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullDirectory.ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\TajsToucher.Diagnostics." + mutexSuffix);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(1)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;

            Directory.CreateDirectory(fullDirectory);
            var path = Path.Combine(fullDirectory, "events.log");
            var line = metadataLine + "\n";
            if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > MaximumFileBytes)
            {
                File.Move(path, Path.Combine(fullDirectory, "events.previous.log"), overwrite: true);
            }
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    internal static string Format(OperationEvent operationEvent) => string.Join('\t',
        operationEvent.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        operationEvent.OperationId.ToString("N"),
        "OpenPGP", operationEvent.Operation.ToString(), operationEvent.Phase.ToString(),
        operationEvent.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
        operationEvent.ElapsedMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "-");
}
