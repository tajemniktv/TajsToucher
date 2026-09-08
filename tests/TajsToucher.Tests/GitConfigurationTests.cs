namespace TajsToucher.Tests;

[TestClass]
public sealed class GitConfigurationTests
{
    [TestMethod]
    public void ReadsExactValuesFromRealGitWithoutTouchingUserConfiguration()
    {
        var git = ExecutableLocator.FindGit();
        if (git is null) Assert.Inconclusive("Git is required for the isolated configuration integration test.");
        var path = Path.GetTempFileName();
        try
        {
            var expected = new[] { "", "  spaced value  ", "embedded\nnewline", "C:\\工具\\gpg.exe" };
            foreach (var value in expected)
            {
                var add = ProcessRunner.Run(git!, new[] { "config", "--file", path, "--add", "gpg.openpgp.program", value },
                    null, TimeSpan.FromSeconds(3));
                Assert.AreEqual(0, add.ExitCode, add.StandardError);
            }

            var read = ProcessRunner.Run(git!, new[] { "config", "--file", path, "--null", "--get-all", "gpg.openpgp.program" },
                null, TimeSpan.FromSeconds(3));
            Assert.IsTrue(GitConfigService.TryParsePrograms(read, out var programs));
            CollectionAssert.AreEqual(expected, programs.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void PreservesExactValuesForRestoration()
    {
        var expected = new[] { "", "  spaced value  ", "line1\r\nline2", "C:\\工具\\gpg.exe" };
        var result = new ProcessResult(true, 0, string.Join('\0', expected) + '\0', "", false, null);
        Assert.IsTrue(GitConfigService.TryParsePrograms(result, out var programs));
        CollectionAssert.AreEqual(expected, programs.ToArray());
    }

    [TestMethod]
    [DataRow(1, "", "", false, true)]
    [DataRow(1, "", "invalid config", false, false)]
    [DataRow(1, "unexpected", "", false, false)]
    [DataRow(0, "unterminated", "", false, false)]
    [DataRow(0, "", "", false, false)]
    [DataRow(128, "value\0", "", false, false)]
    [DataRow(0, "value\0", "", true, false)]
    public void RejectsFailedOrIncompleteReads(int exitCode, string output, string error, bool timedOut, bool expected)
    {
        var result = new ProcessResult(true, exitCode, output, error, timedOut, null);
        Assert.AreEqual(expected, GitConfigService.TryParsePrograms(result, out var programs));
        Assert.AreEqual(0, programs.Count);
    }

    [TestMethod]
    public void RejectsStartFailure()
    {
        Assert.IsFalse(GitConfigService.TryParsePrograms(
            new ProcessResult(false, 0, "value\0", "", false, "unavailable"), out _));
    }

    [TestMethod]
    public void RequiresOneUnambiguousWrapperValue()
    {
        const string wrapper = @"C:\Tools\TajsToucher.exe";
        Assert.IsTrue(GitConfigService.IsWrapperConfiguration(new[] { wrapper.ToUpperInvariant() }, wrapper));
        Assert.IsFalse(GitConfigService.IsWrapperConfiguration(new[] { wrapper, @"C:\GnuPG\gpg.exe" }, wrapper));
        Assert.IsFalse(GitConfigService.IsWrapperConfiguration(new[] { wrapper, wrapper }, wrapper));
        Assert.IsFalse(GitConfigService.IsWrapperConfiguration(Array.Empty<string>(), wrapper));
    }

    [TestMethod]
    public void ReadinessRequiresEveryInstallationCheck()
    {
        var ready = new AppStatus(true, true, true, @"C:\GnuPG\gpg.exe")
        {
            WrapperAvailable = true,
            GitConfigurationReadable = true,
        };
        Assert.IsTrue(ready.IsReady);
        Assert.IsFalse((ready with { IsInstalled = false }).IsReady);
        Assert.IsFalse((ready with { GitConfigurationMatches = false }).IsReady);
        Assert.IsFalse((ready with { RealGpgAvailable = false }).IsReady);
        Assert.IsFalse((ready with { WrapperAvailable = false }).IsReady);
        Assert.IsFalse((ready with { GitConfigurationReadable = false }).IsReady);
        StringAssert.Contains((ready with { WrapperAvailable = false }).Summary, "missing");
        StringAssert.Contains((ready with { GitConfigurationReadable = false }).Summary, "could not be read");
    }
}
