namespace TajsToucher;

internal interface IOperationEventSink
{
    void Publish(OperationEvent operationEvent);
}

// Runs in a detached helper. No daemon, reflection, subscriptions, or payload bus.
internal sealed class OperationEventBroker(params IOperationEventSink[] sinks)
{
    public void Publish(OperationEvent operationEvent)
    {
        if (!operationEvent.IsValid) return;
        foreach (var sink in sinks)
        {
            try
            {
                sink.Publish(operationEvent);
            }
            catch
            {
                // Each optional output fails independently, never the underlying operation.
            }
        }
    }
}

internal static class NotificationPolicy
{
    public static bool IsOperationEnabled(OpenPgpOperation operation, NotificationSettings settings) => operation switch
    {
        OpenPgpOperation.Signing => settings.NotifyOnSigning,
        OpenPgpOperation.Encryption => settings.NotifyOnEncryption,
        OpenPgpOperation.Decryption => settings.NotifyOnDecryption,
        OpenPgpOperation.SigningAndEncryption => settings.NotifyOnSigning || settings.NotifyOnEncryption,
        _ => false,
    };

    public static bool ShouldNotify(OperationEvent operationEvent, NotificationSettings settings) =>
        operationEvent.IsValid && IsOperationEnabled(operationEvent.Operation, settings) &&
        (operationEvent.Phase == OperationPhase.Requested ||
         (operationEvent.Phase == OperationPhase.Failed && settings.NotifyOnFailure));
}

internal sealed class NotificationEventSink(NotificationSettings settings, string? repositoryName) : IOperationEventSink
{
    public void Publish(OperationEvent operationEvent)
    {
        if (NotificationPolicy.ShouldNotify(operationEvent, settings))
        {
            NotificationService.ShowEvent(operationEvent, settings, repositoryName);
        }
    }
}
