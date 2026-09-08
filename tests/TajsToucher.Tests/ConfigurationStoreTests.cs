using Microsoft.Win32;

namespace TajsToucher.Tests;

[TestClass]
public sealed class ConfigurationStoreTests
{
    private string registryPath = null!;
    private ConfigurationStore store = null!;

    [TestInitialize]
    public void Initialize()
    {
        registryPath = @"Software\TajsToucher.Tests." + Guid.NewGuid().ToString("N");
        store = new ConfigurationStore(registryPath);
    }

    [TestCleanup]
    public void Cleanup() => Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);

    [TestMethod]
    public void RoundTripsSettingsAndUninstallPreservesThem()
    {
        var settings = new NotificationSettings("Title", "Text", "", false, 30)
        {
            NotifyOnSigning = false, NotifyOnEncryption = true, NotifyOnDecryption = true,
            NotifyOnFailure = true, RecordDiagnostics = true,
            NotifyOnDevicePresence = true, NotifyOnLowRetries = true,
        };
        store.SaveNotificationSettings(settings);
        var previous = new[] { "", "  spaced  ", "embedded\nnewline" };
        store.Save(new InstallationState(@"C:\Tools\TajsToucher.exe", @"C:\GnuPG\gpg.exe", previous));
        CollectionAssert.AreEqual(previous, store.Load()!.PreviousOpenPgpPrograms.ToArray());
        store.ClearInstallationState();
        Assert.IsNull(store.Load());
        Assert.AreEqual(settings, store.LoadNotificationSettings());
    }

    [TestMethod]
    public void OldSettingsUseCompatibleDefaultsAndInvalidCooldownIsBounded()
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryPath);
        key.SetValue("NotificationTitle", "Existing title");
        var legacy = store.LoadNotificationSettings();
        Assert.IsTrue(legacy.PlaySound);
        Assert.AreEqual(0, legacy.CooldownSeconds);
        Assert.IsTrue(legacy.NotifyOnSigning);
        Assert.IsFalse(legacy.NotifyOnEncryption);
        Assert.IsFalse(legacy.NotifyOnDecryption);
        Assert.IsFalse(legacy.NotifyOnFailure);
        Assert.IsFalse(legacy.RecordDiagnostics);
        Assert.IsFalse(legacy.NotifyOnDevicePresence);
        Assert.IsFalse(legacy.NotifyOnLowRetries);
        key.SetValue("NotificationCooldownSeconds", int.MaxValue);
        Assert.AreEqual(300, store.LoadNotificationSettings().CooldownSeconds);
    }

    [TestMethod]
    public void OversizedBackupDoesNotOverwriteSavedInstallation()
    {
        var original = new InstallationState(@"C:\Tools\TajsToucher.exe", @"C:\GnuPG\gpg.exe", new[] { "old" });
        store.Save(original);
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Save(original with
        {
            PreviousOpenPgpPrograms = Enumerable.Repeat("value", ConfigurationStore.MaximumPreviousPrograms + 1).ToArray(),
        }));
        CollectionAssert.AreEqual(original.PreviousOpenPgpPrograms.ToArray(), store.Load()!.PreviousOpenPgpPrograms.ToArray());
    }

    [TestMethod]
    public void ConcurrentCooldownAttemptsAllowOnlyOnePrompt()
    {
        var mutexName = @"Local\TajsToucher.Tests." + Guid.NewGuid().ToString("N");
        var accepted = 0;
        Parallel.For(0, 16, _ =>
        {
            if (NotificationCooldown.TryAcquire(300, registryPath, mutexName))
                Interlocked.Increment(ref accepted);
        });
        Assert.AreEqual(1, accepted);
        Assert.IsFalse(NotificationCooldown.TryAcquire(300, registryPath, mutexName));
        Assert.IsTrue(NotificationCooldown.TryAcquire(0, registryPath, mutexName));
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true)!;
        key.SetValue("LastNotificationUtcTicks", long.MaxValue, RegistryValueKind.QWord);
        Assert.IsTrue(NotificationCooldown.TryAcquire(300, registryPath, mutexName));
    }
}
