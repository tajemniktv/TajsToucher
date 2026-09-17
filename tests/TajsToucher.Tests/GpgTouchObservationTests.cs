using System.Diagnostics;

namespace TajsToucher.Tests;

[TestClass]
public sealed class GpgTouchObservationTests
{
    [TestMethod]
    public async Task HungProbeExpiresWithoutSigningCompletion()
    {
        var prompt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        using var child = new ProbeChild();
        using var observer = new GpgTouchObservation(executable, name => prompt.TrySetResult(name),
            maximumWait: TimeSpan.FromSeconds(3), probeStarted: child.Capture);
        var name = await prompt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var ended = EventWaitHandle.OpenExisting(name);
        Assert.IsFalse(ended.WaitOne(0));
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await child.AssertExited();
        Assert.IsTrue(ended.WaitOne(0), "Expiry ends waiting; it does not confirm a touch.");
    }

    [TestMethod]
    public async Task NotificationFailureReleasesWaitAndTerminatesProbe()
    {
        EventWaitHandle? ended = null;
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        using var child = new ProbeChild();
        var fallbacks = 0;
        using var observer = new GpgTouchObservation(executable, name =>
        {
            ended = EventWaitHandle.OpenExisting(name);
            throw new IOException("Simulated notification sink failure");
        }, unavailable: () => Interlocked.Increment(ref fallbacks), probeStarted: child.Capture);
        try
        {
            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsNotNull(ended);
            Assert.IsTrue(ended.WaitOne(0));
            await child.AssertExited();
            Assert.AreEqual(1, fallbacks);
        }
        finally { ended?.Dispose(); }
    }

    [TestMethod]
    public async Task MissingProbeFailsOpenWithoutNotification()
    {
        var calls = 0;
        var fallbacks = 0;
        using var observer = new GpgTouchObservation(
            Path.Combine(AppContext.BaseDirectory, Guid.NewGuid() + ".exe"),
            _ => { Interlocked.Increment(ref calls); return true; },
            unavailable: () => Interlocked.Increment(ref fallbacks));
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, calls);
        Assert.AreEqual(1, fallbacks);
    }

    [TestMethod]
    public async Task SigningEndReleasesPromptAndTerminatesOnlyOwnedHungProbe()
    {
        var prompt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        using var child = new ProbeChild();
        var observer = new GpgTouchObservation(executable, name => prompt.TrySetResult(name), probeStarted: child.Capture);
        try
        {
            var name = await prompt.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var ended = EventWaitHandle.OpenExisting(name);
            Assert.IsFalse(ended.WaitOne(0));
            observer.Dispose();
            Assert.IsTrue(ended.WaitOne(TimeSpan.FromSeconds(1)));
            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await child.AssertExited();
        }
        catch { observer.Dispose(); throw; }
    }

    [TestMethod]
    public async Task FastSigningNeverLaunchesStaleWaitNotification()
    {
        var calls = 0;
        using var child = new ProbeChild();
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        var observer = new GpgTouchObservation(executable, _ => { Interlocked.Increment(ref calls); return true; },
            probeStarted: child.Capture);
        await child.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        observer.Dispose(); // Signing ends with a real probe alive, before the busy threshold.
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await child.AssertExited();
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task FailedHelperLaunchFallsBackOnce()
    {
        var fallbacks = 0;
        using var child = new ProbeChild();
        var executable = Path.ChangeExtension(typeof(ProxyChild).Assembly.Location, ".exe");
        using var observer = new GpgTouchObservation(executable, _ => false,
            unavailable: () => Interlocked.Increment(ref fallbacks), maximumWait: TimeSpan.FromSeconds(2), probeStarted: child.Capture);
        await observer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await child.AssertExited();
        Assert.AreEqual(1, fallbacks);
    }

    private sealed class ProbeChild : IDisposable
    {
        private Process? process;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Capture(Process child)
        {
            process = Process.GetProcessById(child.Id); // Independent handle survives observer disposal.
            Started.TrySetResult();
        }
        internal async Task AssertExited()
        {
            Assert.IsNotNull(process);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        public void Dispose()
        {
            if (process is null) return;
            if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
            process.Dispose();
        }
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
