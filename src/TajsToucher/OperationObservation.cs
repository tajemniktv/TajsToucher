namespace TajsToucher;

internal sealed class OperationObservation
{
    private readonly OperationEvent started;
    private readonly bool recordDiagnostics;
    private readonly bool notifyOnFailure;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Action<OperationEvent, string?, bool> publish;
    private readonly object sync = new();
    private bool requestPublished;
    private bool completed;

    private OperationObservation(OpenPgpOperation operation, NotificationSettings settings,
        Action<OperationEvent, string?, bool> publish, bool suppressRequestNotification)
    {
        started = new OperationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, operation, OperationPhase.Requested);
        this.publish = publish;
        recordDiagnostics = settings.RecordDiagnostics;
        notifyOnFailure = settings.NotifyOnFailure && NotificationPolicy.IsOperationEnabled(operation, settings);
        if (!suppressRequestNotification) { requestPublished = true; TryPublish(started); }
    }

    public static OperationObservation? TryStart(OpenPgpOperation? operation, bool suppressRequestNotification = false)
    {
        if (operation is null) return null;
        try
        {
            var settings = new ConfigurationStore().LoadNotificationSettings();
            if (!Enum.IsDefined(operation.Value) || (!NotificationPolicy.IsOperationEnabled(operation.Value, settings) && !settings.RecordDiagnostics)) return null;
            return new OperationObservation(operation.Value, settings, LaunchHelper, suppressRequestNotification);
        }
        catch
        {
            return null;
        }
    }

    internal static OperationObservation? TryStart(OpenPgpOperation operation, NotificationSettings settings,
        Action<OperationEvent, string?> publish, bool suppressRequestNotification = false)
    {
        try
        {
            if (!Enum.IsDefined(operation)) return null;
            var notify = NotificationPolicy.IsOperationEnabled(operation, settings);
            if (!notify && !settings.RecordDiagnostics) return null;
            return new OperationObservation(operation, settings, (evt, repository, _) => publish(evt, repository), suppressRequestNotification);
        }
        catch
        {
            return null;
        }
    }

    public void Complete(int exitCode)
    {
        lock (sync)
        {
        if (completed) return;
        completed = true;
        // Defer the suppressed request record so fallback cannot duplicate diagnostics.
        if (recordDiagnostics && !requestPublished) { requestPublished = true; TryPublish(started, diagnosticsOnly: true); }
        if (!recordDiagnostics && !(exitCode != 0 && notifyOnFailure)) return;
        TryPublish(started with
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Phase = exitCode == 0 ? OperationPhase.Succeeded : OperationPhase.Failed,
            ExitCode = exitCode,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        });
        }
    }

    internal void RestoreRequestNotification()
    {
        lock (sync)
        {
            if (completed || requestPublished) return;
            requestPublished = true;
            TryPublish(started);
        }
    }

    private void TryPublish(OperationEvent operationEvent, bool diagnosticsOnly = false)
    {
        try
        {
            publish(operationEvent, null, diagnosticsOnly);
        }
        catch
        {
            // Neither optional observation nor helper startup may affect GPG's result.
        }
    }

    private static void LaunchHelper(OperationEvent operationEvent, string? repositoryName, bool diagnosticsOnly)
    {
        if (Environment.ProcessPath is { Length: > 0 } executable)
            _ = DetachedProcessLauncher.TryLaunch(executable, OperationEventCodec.Encode(operationEvent, repositoryName),
                diagnosticsOnly ? HelperDispatch.OperationDiagnostics : HelperDispatch.Operation);
    }

    public static int RunHelper(IReadOnlyList<string> args, bool diagnosticsOnly = false)
    {
        if (!OperationEventCodec.TryDecode(args, out var operationEvent, out var repository)) return 2;
        try
        {
            var settings = new ConfigurationStore().LoadNotificationSettings();
            if (!diagnosticsOnly && repository is null && NotificationPolicy.ShouldNotify(operationEvent!, settings))
            {
                try { repository = RepositoryContext.TryGetName(); }
                catch { /* Optional display context is resolved only in this detached helper. */ }
            }
            var sinks = new List<IOperationEventSink>();
            if (settings.RecordDiagnostics) sinks.Add(new DiagnosticEventSink(DiagnosticEventSink.DefaultDirectory));
            if (!diagnosticsOnly) sinks.Add(new NotificationEventSink(settings, repository));
            new OperationEventBroker(sinks.ToArray()).Publish(operationEvent!);
        }
        catch
        {
            // A helper has no authority over the operation that emitted the event.
        }
        return 0;
    }
}
