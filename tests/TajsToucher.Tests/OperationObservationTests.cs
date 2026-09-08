namespace TajsToucher.Tests;

[TestClass]
public sealed class OperationObservationTests
{
    [TestMethod]
    public void DiagnosticsOnlyDoesNotCollectRepositoryAndCorrelatesOneOutcome()
    {
        var events = new List<OperationEvent>();
        var repositoryLookups = 0;
        var observation = OperationObservation.TryStart(OpenPgpOperation.Decryption,
            NotificationSettings.Defaults with { RecordDiagnostics = true },
            () => { repositoryLookups++; return "private repository"; },
            (operationEvent, repository) =>
            {
                Assert.IsNull(repository);
                events.Add(operationEvent);
            });
        Assert.IsNotNull(observation);
        observation.Complete(17);
        observation.Complete(0);
        Assert.AreEqual(0, repositoryLookups);
        Assert.AreEqual(2, events.Count);
        Assert.AreEqual(events[0].OperationId, events[1].OperationId);
        Assert.AreEqual(OperationPhase.Requested, events[0].Phase);
        Assert.AreEqual(OperationPhase.Failed, events[1].Phase);
        Assert.AreEqual(17, events[1].ExitCode);
        Assert.IsTrue(events[1].ElapsedMilliseconds >= 0);
    }

    [TestMethod]
    public void DisabledOperationDoesNotReadContextOrLaunchAnything()
    {
        var calls = 0;
        Assert.IsNull(OperationObservation.TryStart(OpenPgpOperation.Encryption, NotificationSettings.Defaults,
            () => { calls++; return null; }, (_, _) => calls++));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void FailedContextAndOutputRemainNonfatal()
    {
        var attempts = 0;
        var observation = OperationObservation.TryStart(OpenPgpOperation.Signing,
            NotificationSettings.Defaults with { NotifyOnFailure = true },
            () => throw new IOException("unavailable context"),
            (_, _) => { attempts++; throw new IOException("unavailable output"); });
        Assert.IsNotNull(observation);
        observation.Complete(2);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void OrdinarySigningDoesNotLaunchSuccessHelper()
    {
        var calls = 0;
        var observation = OperationObservation.TryStart(OpenPgpOperation.Signing, NotificationSettings.Defaults,
            () => null, (_, _) => calls++);
        Assert.IsNotNull(observation);
        observation.Complete(0);
        Assert.AreEqual(1, calls);
    }
}
