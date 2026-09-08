namespace TajsToucher;

internal sealed class OperationObservation
{
    private readonly OperationEvent started;
    private readonly string? repositoryName;
    private readonly bool recordDiagnostics;
    private readonly bool notifyOnFailure;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Action<OperationEvent, string?> publish;
    private bool completed;

    private OperationObservation(OpenPgpOperation operation, NotificationSettings settings, string? repositoryName,
        Action<OperationEvent, string?> publish)
    {
        started = new OperationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, operation, OperationPhase.Requested);
        this.repositoryName = repositoryName;
        this.publish = publish;
        recordDiagnostics = settings.RecordDiagnostics;
        notifyOnFailure = settings.NotifyOnFailure && NotificationPolicy.IsOperationEnabled(operation, settings);
        TryPublish(started);
    }

    public static OperationObservation? TryStart(OpenPgpOperation? operation)
    {
        if (operation is null) return null;
        try
        {
            var settings = new ConfigurationStore().LoadNotificationSettings();
            return TryStart(operation.Value, settings, RepositoryContext.TryGetName, LaunchHelper);
        }
        catch
        {
            return null;
        }
    }

    internal static OperationObservation? TryStart(OpenPgpOperation operation, NotificationSettings settings,
        Func<string?> getRepository, Action<OperationEvent, string?> publish)
    {
        try
        {
            if (!Enum.IsDefined(operation)) return null;
            var notify = NotificationPolicy.IsOperationEnabled(operation, settings);
            if (!notify && !settings.RecordDiagnostics) return null;
            string? repository = null;
            if (notify)
            {
                try { repository = getRepository(); }
                catch { /* Optional context failure should not hide the operation. */ }
            }
            return new OperationObservation(operation, settings, repository, publish);
        }
        catch
        {
            return null;
        }
    }

    public void Complete(int exitCode)
    {
        if (completed) return;
        completed = true;
        if (!recordDiagnostics && !(exitCode != 0 && notifyOnFailure)) return;
        TryPublish(started with
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Phase = exitCode == 0 ? OperationPhase.Succeeded : OperationPhase.Failed,
            ExitCode = exitCode,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        });
    }

    private void TryPublish(OperationEvent operationEvent)
    {
        try
        {
            publish(operationEvent, repositoryName);
        }
        catch
        {
            // Neither optional observation nor helper startup may affect GPG's result.
        }
    }

    private static void LaunchHelper(OperationEvent operationEvent, string? repositoryName)
    {
        if (Environment.ProcessPath is { Length: > 0 } executable)
            _ = DetachedProcessLauncher.TryLaunch(executable, OperationEventCodec.Encode(operationEvent, repositoryName));
    }

    public static int RunHelper(IReadOnlyList<string> args)
    {
        if (!OperationEventCodec.TryDecode(args, out var operationEvent, out var repository)) return 2;
        try
        {
            var settings = new ConfigurationStore().LoadNotificationSettings();
            var sinks = new List<IOperationEventSink>();
            if (settings.RecordDiagnostics) sinks.Add(new DiagnosticEventSink(DiagnosticEventSink.DefaultDirectory));
            sinks.Add(new NotificationEventSink(settings, repository));
            new OperationEventBroker(sinks.ToArray()).Publish(operationEvent!);
        }
        catch
        {
            // A helper has no authority over the operation that emitted the event.
        }
        return 0;
    }
}
