namespace TajsToucher.Tests;

[TestClass]
public sealed class OperationClassifierTests
{
    [TestMethod]
    [DataRow("-bsau", true)]
    [DataRow("--detach-sign", true)]
    [DataRow("--sign", true)]
    [DataRow("--clearsign", true)]
    [DataRow("--sign=key", true)]
    [DataRow("--verify", false)]
    [DataRow("--verify-files", false)]
    [DataRow("--encrypt", false)]
    [DataRow("--decrypt", false)]
    public void ClassifiesGpgOperation(string argument, bool expected)
    {
        Assert.AreEqual(expected, OperationClassifier.IsSigning(new[] { argument }));
    }

    [TestMethod]
    public void VerificationWinsForMixedArguments()
    {
        Assert.IsFalse(OperationClassifier.IsSigning(new[] { "--detach-sign", "--verify" }));
    }

    [TestMethod]
    public void DoesNotInspectFileNamesAfterOptionTerminator()
    {
        Assert.IsFalse(OperationClassifier.IsSigning(new[] { "--", "--detach-sign" }));
    }

    [TestMethod]
    public void DetectsGitShortSigningCluster()
    {
        Assert.IsTrue(OperationClassifier.IsSigning(new[] { "--status-fd=2", "-bsau", "ABC123" }));
    }

    [TestMethod]
    public void QuotesDetachedHelperArgumentsUsingWindowsRules()
    {
        Assert.AreEqual("\"repo with spaces\"", WindowsArgumentQuoter.Quote("repo with spaces"));
        Assert.AreEqual("\"quote\\\"value\"", WindowsArgumentQuoter.Quote("quote\"value"));
        Assert.AreEqual("plain", WindowsArgumentQuoter.Quote("plain"));
    }

    [TestMethod]
    public void RendersRepositoryTemplateToken()
    {
        Assert.AreEqual("Sign from Demo.", NotificationTemplate.Render("Sign from {Repository}.", "Demo"));
        Assert.AreEqual("Sign from unknown.", NotificationTemplate.Render("Sign from {Repository}.", null));
    }

    [TestMethod]
    public void AppendsRepositoryWhenCustomTextOmitsToken()
    {
        Assert.AreEqual("Touch the key. Repository: Demo.", NotificationTemplate.Render("Touch the key.", "Demo"));
    }
}
