using TajsToucher.Devices;
using System.Xml.Linq;

namespace TajsToucher.Tests;

[TestClass]
public sealed class ReviewRegressionTests
{
    [TestMethod]
    public void NavigationPagesHaveCompiledActivationMetadata()
    {
        var provider = new TajsToucher.TajsToucher_XamlTypeInfo.XamlMetaDataProvider();
        foreach (var page in new[] { typeof(Pages.HomePage), typeof(Pages.SettingsPage), typeof(Pages.EnabledForPage), typeof(Pages.DevicesPage) })
        {
            var metadata = provider.GetXamlType(page);
            Assert.IsNotNull(metadata, $"{page.Name} is missing from the compiled XAML type table.");
            Assert.IsTrue(metadata.IsConstructible, $"Frame.Navigate cannot activate {page.Name}.");
        }
    }

    [TestMethod]
    public void PageBrushReferencesFollowTheNativeTheme()
    {
        var assembly = typeof(ReviewRegressionTests).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".xaml")))
        {
            using var source = assembly.GetManifestResourceStream(name)!;
            var document = XDocument.Load(source);
            foreach (var attribute in document.Descendants().Attributes().Where(a => a.Name.LocalName is "Foreground" or "Background" && a.Value.StartsWith('{')))
                StringAssert.StartsWith(attribute.Value, "{ThemeResource ", $"{name}: brush must follow the active theme.");
        }
    }

    [TestMethod]
    public void TraySelectionDispatchesExitAndIgnoresDismissal()
    {
        using var tray = new TajsToucher.Services.WindowsSystemTrayService();
        var exits = 0;
        var opens = 0;
        var settings = 0;
        var enabled = 0;
        tray.ExitRequested += (_, _) => exits++;
        tray.OpenDashboardRequested += (_, _) => opens++;
        tray.OpenSettingsRequested += (_, _) => settings++;
        tray.OpenEnabledForRequested += (_, _) => enabled++;
        foreach (var selected in new uint[] { 1001, 1002, 1003, 1004, 0 })
            TajsToucher.Services.WindowsSystemTrayService.DispatchMenuSelection(selected, tray.ProcessMenuCommand);
        Assert.AreEqual(1, exits);
        Assert.AreEqual(1, opens);
        Assert.AreEqual(1, settings);
        Assert.AreEqual(1, enabled);
        TajsToucher.Services.WindowsSystemTrayService.DispatchMenuSelection(0, _ => Assert.Fail("Dismissal is not a command."));
    }

    [TestMethod]
    public void NavigationIconsAreValidWinUiSymbols()
    {
        using var source = typeof(ReviewRegressionTests).Assembly.GetManifestResourceStream("MainWindow.xaml")!;
        var document = System.Xml.Linq.XDocument.Load(source);
        var icons = document.Descendants().Attributes("Icon").Select(attribute => attribute.Value).ToArray();
        Assert.IsTrue(icons.Length > 0);
        foreach (var icon in icons)
            Assert.IsTrue(Enum.TryParse<Microsoft.UI.Xaml.Controls.Symbol>(icon, out var symbol) && Enum.IsDefined(symbol),
                $"Navigation icon '{icon}' is not a WinUI Symbol and will fail during XAML loading.");
    }

    [TestMethod]
    public void PublicHelperShapedArgumentsAreForwardedWithoutPrivateLaunchMode()
    {
        var encoded = OperationEventCodec.Encode(new OperationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow,
            OpenPgpOperation.Signing, OperationPhase.Requested), null);
        foreach (var args in new[] { encoded, new[] { "--operation-event" }, new[] { "--operation-event", "bad" }, new[] { "--notify" } })
        {
            Assert.AreEqual(37, ApplicationHost.Run(args, null, forwarded => { Assert.AreSame(args, forwarded); return 37; }, _ => throw new AssertFailedException()));
        }
        Assert.AreEqual(9, ApplicationHost.Run(encoded, HelperDispatch.Operation, _ => throw new AssertFailedException(), _ => 9));
        var previous = Environment.GetEnvironmentVariable(HelperDispatch.EnvironmentVariable);
        StringAssert.Contains(HelperDispatch.CreateEnvironmentBlock(HelperDispatch.Operation), HelperDispatch.EnvironmentVariable + "=" + HelperDispatch.Operation + "\0");
        Assert.AreEqual(previous, Environment.GetEnvironmentVariable(HelperDispatch.EnvironmentVariable));
    }

    [TestMethod]
    [DataRow("-Rdescriptive-recipient")]
    [DataRow("-Rsigner@example.test")]
    public void AttachedHiddenRecipientIsNotParsedAsCommands(string recipient) =>
        Assert.AreEqual(OpenPgpOperation.Encryption, OperationClassifier.Classify(["--encrypt", recipient]));

    [TestMethod]
    public void SeparateHiddenRecipientIsSkipped() =>
        Assert.AreEqual(OpenPgpOperation.Encryption, OperationClassifier.Classify(["--encrypt", "-R", "--sign"]));

    [TestMethod]
    public void FailedStatusLoadDoesNotInventGitOrInstallationDiagnosis()
    {
        var status = new AppStatus(false, false, false, null) { StatusReadFailed = true };
        StringAssert.Contains(status.Summary, "Setup status could not be read");
        Assert.IsFalse(status.IsReady);
        Assert.AreEqual("Unknown", status.SigningValue);
        Assert.AreEqual("Unknown", status.GpgValue);
        StringAssert.Contains(status.SigningDetail, "could not be read");
        Assert.IsFalse(status.CanInstall);
        Assert.IsFalse(status.CanUninstall);
    }

    [TestMethod]
    public async Task BrokerDiscardsBufferedDiagnosticsAtShutdown()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var broker = new DeviceEventBroker(() => NotificationSettings.Defaults with { RecordDiagnostics = true },
            _ => { Interlocked.Increment(ref calls); entered.TrySetResult(); release.Wait(); }, (_, _) => { });
        var signal = new DeviceSignal(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DeviceSignalKind.Arrived, DeviceOutcome.Ready);
        try
        {
            broker.Publish(signal);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var i = 0; i < 64; i++) broker.Publish(signal);
            var disposal = broker.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted); // The one active callback may finish.
            release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.AreEqual(1, calls);
        }
        finally { release.Set(); await broker.DisposeAsync(); }
    }

    [TestMethod]
    public async Task ClockRollbackStartsANewNoticeWindow()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var broker = new DeviceEventBroker(() => NotificationSettings.Defaults with { NotifyOnDevicePresence = true }, _ => { },
            (_, _) => { if (Interlocked.Increment(ref count) == 3) done.TrySetResult(); });
        var stamp = DateTimeOffset.UtcNow;
        var signal = new DeviceSignal(Guid.NewGuid(), Guid.NewGuid(), stamp.AddHours(1), DeviceSignalKind.Arrived, DeviceOutcome.Ready);
        broker.Publish(signal);
        broker.Publish(signal with { Timestamp = stamp });
        broker.Publish(signal with { Timestamp = stamp.AddSeconds(1) });
        broker.Publish(signal with { Timestamp = stamp.AddSeconds(4) });
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { await broker.DisposeAsync(); }
        Assert.AreEqual(3, count);
    }

    [TestMethod]
    public async Task ShutdownBoundsEvenSynchronousThirdPartyDisposal()
    {
        using var release = new ManualResetEventSlim();
        var feature = new StuckFeature(release);
        try
        {
            Assert.IsFalse(await OptionalFeatureShutdown.DisposeAsync(feature, TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { release.Set(); }
        await feature.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    private sealed class StuckFeature(ManualResetEventSlim release) : IAsyncDisposable
    {
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask DisposeAsync() { release.Wait(); Finished.TrySetResult(); return ValueTask.CompletedTask; }
    }
}
