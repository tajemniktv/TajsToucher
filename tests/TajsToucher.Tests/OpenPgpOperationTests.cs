namespace TajsToucher.Tests;

[TestClass]
public sealed class OpenPgpOperationTests
{
    [TestMethod]
    [DataRow("--clear-sign", "Signing")]
    [DataRow("--encrypt", "Encryption")]
    [DataRow("--encrypt-files", "Encryption")]
    [DataRow("--symmetric", "Encryption")]
    [DataRow("--decrypt", "Decryption")]
    [DataRow("--decrypt-files", "Decryption")]
    [DataRow("-e", "Encryption")]
    [DataRow("-c", "Encryption")]
    [DataRow("-d", "Decryption")]
    [DataRow("-sea", "SigningAndEncryption")]
    public void ClassifiesExplicitOperations(string argument, string expected)
    {
        Assert.AreEqual(expected, OperationClassifier.Classify(new[] { argument })?.ToString());
    }

    [TestMethod]
    public void PreservesCombinedOperationAndSkipsOptionValues()
    {
        Assert.AreEqual(OpenPgpOperation.SigningAndEncryption,
            OperationClassifier.Classify(new[] { "--sign", "--encrypt", "--recipient", "someone" }));
        Assert.AreEqual(OpenPgpOperation.Signing,
            OperationClassifier.Classify(new[] { "--local-user", "--decrypt", "-bsau", "-encrypted-name" }));
        Assert.AreEqual(OpenPgpOperation.Decryption,
            OperationClassifier.Classify(new[] { "--output", "--sign", "--decrypt" }));
        Assert.IsNull(OperationClassifier.Classify(new[] { "-user", "--recipient", "--sign" }));
        Assert.IsNull(OperationClassifier.Classify(new[] { "-output.gpg", "--", "--sign" }));
    }

    [TestMethod]
    [DataRow("--verify")]
    [DataRow("--verify-files")]
    [DataRow("--edit-key")]
    [DataRow("--change-pin")]
    [DataRow("--card-status")]
    [DataRow("--list-keys")]
    [DataRow("--export-secret-keys")]
    [DataRow("--quick-sign-key")]
    [DataRow("--version")]
    public void ReadOnlyAndKeyManagementCommandsStayUnobserved(string command)
    {
        Assert.IsNull(OperationClassifier.Classify(new[] { command, "--sign" }));
    }

    [TestMethod]
    public void DoesNotGuessImplicitOperationsOrConflictingCommands()
    {
        Assert.IsNull(OperationClassifier.Classify(new[] { "encrypted.gpg" }));
        Assert.IsNull(OperationClassifier.Classify(new[] { "--", "--decrypt" }));
        Assert.IsNull(OperationClassifier.Classify(new[] { "--sign", "--decrypt" }));
    }
}
