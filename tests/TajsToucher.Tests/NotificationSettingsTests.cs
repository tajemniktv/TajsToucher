namespace TajsToucher.Tests;

[TestClass]
public sealed class NotificationSettingsTests
{
    [TestMethod]
    public void DefaultsPreserveExistingNotificationBehavior()
    {
        Assert.IsTrue(NotificationSettings.Defaults.PlaySound);
        Assert.AreEqual(0, NotificationSettings.Defaults.CooldownSeconds);
    }

    [TestMethod]
    [DataRow(-1, 0)]
    [DataRow(30, 30)]
    [DataRow(int.MaxValue, 300)]
    public void NormalizesCooldownWithoutChangingSoundChoice(int input, int expected)
    {
        var settings = new NotificationSettings(" title ", " text ", "", false, input).Normalize();
        Assert.AreEqual(expected, settings.CooldownSeconds);
        Assert.IsFalse(settings.PlaySound);
        Assert.AreEqual("title", settings.Title);
    }

    [TestMethod]
    public void CooldownHandlesBoundaryDisabledAndClockRollback()
    {
        var now = DateTime.UtcNow.Ticks;
        Assert.IsFalse(NotificationCooldown.IsDue(now - TimeSpan.FromSeconds(9).Ticks, now, 10));
        Assert.IsTrue(NotificationCooldown.IsDue(now - TimeSpan.FromSeconds(10).Ticks, now, 10));
        Assert.IsTrue(NotificationCooldown.IsDue(now, now, 0));
        Assert.IsTrue(NotificationCooldown.IsDue(0, now, 10));
        Assert.IsTrue(NotificationCooldown.IsDue(long.MaxValue, now, 10));
    }

    [TestMethod]
    public void UpgradesOnlyLegacyStockMessageAndPreservesCustomPolicies()
    {
        const string legacy = "Git is requesting an OpenPGP signature. Please touchy touch the YubiKey while it flashes. Repository: {Repository}.";
        var upgraded = new NotificationSettings("Title", legacy, "") with
        {
            NotifyOnSigning = false, NotifyOnDecryption = true, RecordDiagnostics = true,
        };
        upgraded = upgraded.Normalize();
        Assert.AreEqual(NotificationSettings.DefaultText, upgraded.Text);
        Assert.IsFalse(upgraded.NotifyOnSigning);
        Assert.IsTrue(upgraded.NotifyOnDecryption);
        Assert.IsTrue(upgraded.RecordDiagnostics);
        Assert.AreEqual("Custom text", new NotificationSettings("Title", "Custom text", "").Normalize().Text);
    }
}
