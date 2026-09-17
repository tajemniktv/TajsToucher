using System.Security.Cryptography;
using System.Text;

namespace TajsToucher.Tests;

[TestClass]
public sealed class DogfoodLifecycleTests
{
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
