namespace TajsToucher.Tests;

[TestClass]
public sealed class OperationObservationTests
{
    [TestMethod]
    public void ObserverFallbackIsOneShotAndDoesNotDuplicateDiagnosticsOrOutliveSigning()
    {
        var events = new List<OperationEvent>();
        var settings = NotificationSettings.Defaults with { RecordDiagnostics = true };
        var observation = OperationObservation.TryStart(OpenPgpOperation.Signing, settings,
            (evt, _) => events.Add(evt), suppressRequestNotification: true)!;
        observation.RestoreRequestNotification();
        observation.RestoreRequestNotification();
        observation.Complete(0);
        observation.RestoreRequestNotification();
        CollectionAssert.AreEqual(new[] { OperationPhase.Requested, OperationPhase.Succeeded }, events.Select(e => e.Phase).ToArray());
        events.Clear();
        observation = OperationObservation.TryStart(OpenPgpOperation.Signing, NotificationSettings.Defaults,
            (evt, _) => events.Add(evt), suppressRequestNotification: true)!;
        observation.Complete(0);
        observation.RestoreRequestNotification();
        Assert.AreEqual(0, events.Count);
    }
    [TestMethod]
    public void TouchObserverReplacesRequestNoticeButPreservesFailureAndDiagnostics()
    {
        var events = new List<OperationEvent>();
        var settings = NotificationSettings.Defaults with { NotifyOnFailure = true };
        var observation = OperationObservation.TryStart(OpenPgpOperation.Signing, settings,
            (evt, _) => events.Add(evt), suppressRequestNotification: true)!;
        Assert.AreEqual(0, events.Count);
        observation.Complete(2);
        Assert.AreEqual(OperationPhase.Failed, events.Single().Phase);

        events.Clear();
        observation = OperationObservation.TryStart(OpenPgpOperation.Signing, settings with { RecordDiagnostics = true },
            (evt, _) => events.Add(evt), suppressRequestNotification: true)!;
        observation.Complete(0);
        Assert.AreEqual(OperationPhase.Requested, events[0].Phase);
        Assert.AreEqual(OperationPhase.Succeeded, events[1].Phase);
        Assert.AreEqual(events[0].OperationId, events[1].OperationId);
    }

    [TestMethod]
    public void DiagnosticsOnlyDoesNotCollectRepositoryAndCorrelatesOneOutcome()
    {
        var events = new List<OperationEvent>();
        var observation = OperationObservation.TryStart(OpenPgpOperation.Decryption,
            NotificationSettings.Defaults with { RecordDiagnostics = true },
            (operationEvent, repository) =>
            {
                Assert.IsNull(repository);
                events.Add(operationEvent);
            });
        Assert.IsNotNull(observation);
        observation.Complete(17);
        observation.Complete(0);
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
            (_, _) => calls++));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void FailedContextAndOutputRemainNonfatal()
    {
        var attempts = 0;
        var observation = OperationObservation.TryStart(OpenPgpOperation.Signing,
            NotificationSettings.Defaults with { NotifyOnFailure = true },
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
            (_, repository) => { Assert.IsNull(repository); calls++; });
        Assert.IsNotNull(observation);
        observation.Complete(0);
        Assert.AreEqual(1, calls);
    }
}
