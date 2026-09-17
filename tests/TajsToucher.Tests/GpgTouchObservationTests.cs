namespace TajsToucher.Tests;

[TestClass]
public sealed class GpgTouchObservationTests
{
    [TestMethod]
    public async Task HungProbeExpiresWithoutSigningCompletion()
    {
        var prompt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        using var observer = new GpgTouchObservation(executable, name => prompt.TrySetResult(name));
        var name = await prompt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var ended = EventWaitHandle.OpenExisting(name);
        Assert.IsFalse(ended.WaitOne(0));
        // Exercise the real production 30-second bound, not a shorter fake timeout.
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(40));
        Assert.IsTrue(ended.WaitOne(0), "Expiry ends waiting; it does not confirm a touch.");
    }

    [TestMethod]
    public async Task NotificationFailureReleasesWaitAndTerminatesProbe()
    {
        EventWaitHandle? ended = null;
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        using var observer = new GpgTouchObservation(executable, name =>
        {
            ended = EventWaitHandle.OpenExisting(name);
            throw new IOException("Simulated notification sink failure");
        });
        try
        {
            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsNotNull(ended);
            Assert.IsTrue(ended.WaitOne(0));
        }
        finally { ended?.Dispose(); }
    }

    [TestMethod]
    public async Task MissingProbeFailsOpenWithoutNotification()
    {
        var calls = 0;
        using var observer = new GpgTouchObservation(
            Path.Combine(AppContext.BaseDirectory, Guid.NewGuid() + ".exe"),
            _ => Interlocked.Increment(ref calls));
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task SigningEndReleasesPromptAndTerminatesOnlyOwnedHungProbe()
    {
        var prompt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        var observer = new GpgTouchObservation(executable, name => prompt.TrySetResult(name));
        try
        {
            var name = await prompt.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var ended = EventWaitHandle.OpenExisting(name);
            Assert.IsFalse(ended.WaitOne(0));
            observer.Dispose();
            Assert.IsTrue(ended.WaitOne(TimeSpan.FromSeconds(1)));
            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { observer.Dispose(); throw; }
    }

    [TestMethod]
    public async Task FastSigningNeverLaunchesStaleWaitNotification()
    {
        var calls = 0;
        var observer = new GpgTouchObservation("not-started.exe", _ => Interlocked.Increment(ref calls));
        observer.Dispose();
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void DifferentAgentConfigurationAndNonSigningAreExcluded()
    {
        Assert.IsTrue(GpgTouchObservation.IsEligible(["-bsau", "key"]));
        Assert.IsFalse(GpgTouchObservation.IsEligible(["--verify", "signature"]));
        Assert.IsFalse(GpgTouchObservation.IsEligible(["--homedir", "elsewhere", "--sign"]));
        Assert.IsFalse(GpgTouchObservation.IsEligible(["--options=elsewhere", "--sign"]));
        Assert.IsFalse(GpgTouchObservation.IsEligible(["--no-options", "--sign"]));
    }
}
