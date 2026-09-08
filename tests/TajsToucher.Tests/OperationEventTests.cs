namespace TajsToucher.Tests;

[TestClass]
public sealed class OperationEventTests
{
    private static OperationEvent Requested(OpenPgpOperation operation = OpenPgpOperation.Signing) =>
        new(Guid.NewGuid(), DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), operation, OperationPhase.Requested);

    [TestMethod]
    public void CodecRoundTripsMetadataAndSeparatesDisplayContext()
    {
        var original = Requested(OpenPgpOperation.Decryption) with
        {
            Phase = OperationPhase.Failed, ExitCode = 2, ElapsedMilliseconds = 123,
        };
        var encoded = OperationEventCodec.Encode(original, "repo \"工具\" with spaces");
        Assert.IsTrue(OperationEventCodec.TryDecode(encoded, out var decoded, out var repository));
        Assert.AreEqual(original, decoded);
        Assert.AreEqual("repo \"工具\" with spaces", repository);
        Assert.IsFalse(DiagnosticEventSink.Format(decoded!).Contains(repository!, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(0, "--different-helper")]
    [DataRow(1, "bad-id")]
    [DataRow(2, "9223372036854775807")]
    [DataRow(3, "999")]
    [DataRow(4, "999")]
    [DataRow(5, "0")]
    [DataRow(6, "-1")]
    public void RejectsMalformedOrInconsistentHelperEvents(int index, string value)
    {
        var args = OperationEventCodec.Encode(Requested(), null);
        args[index] = value;
        Assert.IsFalse(OperationEventCodec.TryDecode(args, out _, out _));
    }

    [TestMethod]
    public void RejectsTruncationAndInvalidOutcomeRelationships()
    {
        var args = OperationEventCodec.Encode(Requested(), null);
        Assert.IsFalse(OperationEventCodec.TryDecode(args[..^1], out _, out _));
        Assert.IsFalse((Requested() with { Phase = OperationPhase.Failed, ExitCode = 0, ElapsedMilliseconds = 1 }).IsValid);
        Assert.IsFalse((Requested() with { Phase = OperationPhase.Succeeded, ExitCode = 1, ElapsedMilliseconds = 1 }).IsValid);
        Assert.IsFalse((Requested() with { OperationId = Guid.Empty }).IsValid);
    }

    [TestMethod]
    public void DefaultsNotifySigningOnlyAndRulesNeverNotifySuccess()
    {
        var settings = NotificationSettings.Defaults;
        Assert.IsTrue(NotificationPolicy.ShouldNotify(Requested(), settings));
        Assert.IsTrue(NotificationPolicy.ShouldNotify(Requested(OpenPgpOperation.SigningAndEncryption), settings));
        Assert.IsFalse(NotificationPolicy.ShouldNotify(Requested(OpenPgpOperation.Encryption), settings));
        Assert.IsFalse(NotificationPolicy.ShouldNotify(Requested(OpenPgpOperation.Decryption), settings));
        Assert.IsFalse(NotificationPolicy.ShouldNotify(Requested() with
        {
            Phase = OperationPhase.Succeeded, ExitCode = 0, ElapsedMilliseconds = 1,
        }, settings with { NotifyOnFailure = true }));
        Assert.IsFalse(settings.RecordDiagnostics);
    }

    [TestMethod]
    public void FailuresRequireBothOperationAndFailureOptIn()
    {
        var failed = Requested(OpenPgpOperation.Decryption) with
        {
            Phase = OperationPhase.Failed, ExitCode = 2, ElapsedMilliseconds = 100,
        };
        Assert.IsFalse(NotificationPolicy.ShouldNotify(failed, NotificationSettings.Defaults with { NotifyOnFailure = true }));
        Assert.IsFalse(NotificationPolicy.ShouldNotify(failed, NotificationSettings.Defaults with { NotifyOnDecryption = true }));
        Assert.IsTrue(NotificationPolicy.ShouldNotify(failed,
            NotificationSettings.Defaults with { NotifyOnDecryption = true, NotifyOnFailure = true }));
    }

    [TestMethod]
    public void BrokenSinkDoesNotStopOtherOutputsAndInvalidEventsAreDropped()
    {
        var received = new List<OperationEvent>();
        var broker = new OperationEventBroker(new CallbackSink(_ => throw new IOException("unavailable")), new CallbackSink(received.Add));
        var operationEvent = Requested();
        broker.Publish(operationEvent);
        broker.Publish(operationEvent with { Operation = (OpenPgpOperation)999 });
        CollectionAssert.AreEqual(new[] { operationEvent }, received);
    }

    [TestMethod]
    public void PresentationUsesOperationTokenWithoutReinterpretingRepositoryText()
    {
        var settings = new NotificationSettings("{Operation}", "Requested {Operation}", "");
        var presentation = NotificationService.FormatEvent(Requested(OpenPgpOperation.Decryption), settings, "{Operation}");
        Assert.AreEqual("OpenPGP decryption", presentation.Title);
        Assert.AreEqual("Requested OpenPGP decryption Repository: {Operation}.", presentation.Message);
        var failed = NotificationService.FormatEvent(Requested() with
        {
            Phase = OperationPhase.Failed, ExitCode = 2, ElapsedMilliseconds = 1,
        }, settings, null);
        StringAssert.Contains(failed.Title, "failed");
        StringAssert.Contains(failed.Message, "code 2");
    }

    private sealed class CallbackSink(Action<OperationEvent> callback) : IOperationEventSink
    {
        public void Publish(OperationEvent operationEvent) => callback(operationEvent);
    }
}
