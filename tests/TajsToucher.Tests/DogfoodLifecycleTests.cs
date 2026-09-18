using System.Security.Cryptography;
using System.Text;

namespace TajsToucher.Tests;

[TestClass]
public sealed class DogfoodLifecycleTests
{
    [TestMethod]
    public async Task ExclusiveFileLeaseBlocksOperationWithoutOwningItsSessionMutex()
    {
        var root = CreateLeaseRoot();
        var leasePath = Path.Combine(root, ".TajsToucher-dogfood", "activity.lock");
        Task<IDisposable?>? pending = null;
        IDisposable? operation = null;
        try
        {
            using (var deployment = new FileStream(leasePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                pending = Task.Run(() => DogfoodLifecycle.EnterOperation(Path.Combine(root, "current", "TajsToucher.exe"), TimeSpan.FromSeconds(5)));
                await Task.Delay(200);
                Assert.IsFalse(pending.IsCompleted, "A different-session deployment must not cause unprotected GPG startup.");
            }
            operation = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNotNull(operation);
            Assert.ThrowsExactly<IOException>(() => new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        }
        finally
        {
            if (pending is not null && operation is null) { try { operation = await pending; } catch { } }
            operation?.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ExclusiveLeaseTimeoutDoesNotFailOpen()
    {
        var root = CreateLeaseRoot();
        try
        {
            using var deployment = new FileStream(Path.Combine(root, ".TajsToucher-dogfood", "activity.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            Assert.ThrowsExactly<TimeoutException>(() => DogfoodLifecycle.EnterOperation(Path.Combine(root, "current", "TajsToucher.exe"), TimeSpan.FromMilliseconds(150)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateLeaseRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TajsToucher.slnx"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        var root = Path.Combine(directory.FullName, ".codex", "temp", "lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".TajsToucher-dogfood"));
        Directory.CreateDirectory(Path.Combine(root, "current"));
        return root;
    }
    [TestMethod]
    public void IdentityMatchesDeploymentProtocolAndIgnoresPathCase()
    {
        var path = Path.GetFullPath("TajsToucher.exe");
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        Assert.AreEqual(expected, DogfoodLifecycle.Identity(path));
        Assert.AreEqual(expected, DogfoodLifecycle.Identity(path.ToLowerInvariant()));
    }

    [TestMethod]
    public void TrayProtocolUsesPerProcessNames()
    {
        Assert.AreEqual(@"Local\TajsToucher.Dogfood.Stop.123", DogfoodLifecycle.EventName("Stop", 123));
        Assert.AreNotEqual(DogfoodLifecycle.EventName("Ready", 123), DogfoodLifecycle.EventName("Ready", 124));
    }
}
