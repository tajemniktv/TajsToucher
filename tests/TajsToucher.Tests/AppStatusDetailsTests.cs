namespace TajsToucher.Tests;

[TestClass]
public sealed class AppStatusDetailsTests
{
    [TestMethod]
    public void MismatchIncludesExpectedRunningAndAllActualValues()
    {
        var status = new AppStatus(true, false, true, "gpg.exe")
        {
            GitConfigurationReadable = true,
            SavedWrapperPath = "expected.exe",
            RunningWrapperPath = "running.exe",
            GitExecutablePath = "git.exe",
            GitPrograms = new[] { "other.exe", "", " trailing \n" },
        };
        StringAssert.Contains(status.ComparisonDetails, "WrapperPath): \"expected.exe\"");
        StringAssert.Contains(status.ComparisonDetails, "Running executable: \"running.exe\"");
        StringAssert.Contains(status.ComparisonDetails, "Git executable queried: \"git.exe\"");
        StringAssert.Contains(status.ComparisonDetails, "(3 value(s))");
        StringAssert.Contains(status.ComparisonDetails, "[1] \"other.exe\"");
        StringAssert.Contains(status.ComparisonDetails, "[2] \"\"");
        StringAssert.Contains(status.ComparisonDetails, "[3] \" trailing \\n\"");
    }

    [TestMethod]
    public void MissingAndUnreadableGitValuesAreDistinct()
    {
        var status = new AppStatus(false, false, false, null);
        StringAssert.Contains(status.ComparisonDetails, "could not read; not a confirmed mismatch");
        StringAssert.Contains((status with { GitConfigurationReadable = true }).ComparisonDetails, "<not set>");
        StringAssert.Contains(status.ComparisonDetails, "WrapperPath): <unavailable>");
    }
}
