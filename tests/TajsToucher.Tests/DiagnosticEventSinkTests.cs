namespace TajsToucher.Tests;

[TestClass]
public sealed class DiagnosticEventSinkTests
{
    [TestMethod]
    public void BoundsDiagnosticFilesAndKeepsMetadataOnly()
    {
        var directory = Directory.CreateTempSubdirectory("TajsToucher.Diagnostics.Tests.");
        try
        {
            var operationEvent = new OperationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow,
                OpenPgpOperation.Encryption, OperationPhase.Succeeded, 0, 42);
            var sink = new DiagnosticEventSink(directory.FullName);
            for (var index = 0; index < 1300; index++) sink.Publish(operationEvent);
            var files = directory.GetFiles();
            Assert.AreEqual(2, files.Length);
            Assert.IsTrue(files.All(file => file.Length <= DiagnosticEventSink.MaximumFileBytes));
            var line = File.ReadLines(Path.Combine(directory.FullName, "events.log")).First();
            Assert.AreEqual(7, line.Split('\t').Length);
            StringAssert.Contains(line, "OpenPGP\tEncryption\tSucceeded\t0\t42");
        }
        finally
        {
            // This unique directory and its two fixed log filenames are owned by this test.
            foreach (var file in directory.GetFiles()) file.Delete();
            directory.Delete();
        }
    }

    [TestMethod]
    public void RejectsInvalidEventsWithoutCreatingFiles()
    {
        var directory = Directory.CreateTempSubdirectory("TajsToucher.Diagnostics.Tests.");
        try
        {
            new DiagnosticEventSink(directory.FullName).Publish(new OperationEvent(Guid.Empty, DateTimeOffset.UtcNow,
                OpenPgpOperation.Signing, OperationPhase.Requested));
            Assert.AreEqual(0, directory.GetFiles().Length);
        }
        finally { directory.Delete(); }
    }
}
