namespace TajsToucher;

internal static class OperationClassifier
{
    private static readonly HashSet<string> SigningOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sign",
        "detach-sign",
        "clearsign",
        "clear-sign",
    };

    private static readonly HashSet<string> VerificationOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "verify",
        "verify-files",
    };

    private static readonly HashSet<string> UnobservedCommands = new(StringComparer.Ordinal)
    {
        "help", "version", "dump-options", "list-keys", "list-public-keys", "list-secret-keys",
        "list-sigs", "list-signatures", "check-sigs", "check-signatures", "fingerprint", "list-packets",
        "show-keys", "import", "export", "export-secret-keys", "export-secret-subkeys", "export-ssh-key",
        "card-status", "card-edit", "edit-card", "change-pin", "edit-key", "delete-keys", "delete-key",
        "delete-secret-keys", "delete-secret-key", "delete-secret-and-public-key", "generate-key", "gen-key",
        "full-generate-key", "full-gen-key", "quick-generate-key", "quick-gen-key", "quick-add-key",
        "quick-add-uid", "quick-revoke-uid", "quick-set-expire", "sign-key", "lsign-key", "quick-sign-key",
        "quick-lsign-key", "gen-revoke", "generate-revocation",
    };

    // Values must not be mistaken for operations, especially short-option
    // values such as -uSomebody or an output filename containing "--sign".
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "local-user", "recipient", "hidden-recipient", "recipient-file", "hidden-recipient-file",
        "output", "homedir", "options", "default-key", "default-recipient", "keyring", "secret-keyring",
        "status-fd", "status-file", "logger-fd", "logger-file", "attribute-fd", "attribute-file",
        "command-fd", "command-file", "passphrase", "passphrase-fd", "passphrase-file", "pinentry-mode",
        "trust-model", "digest-algo", "cipher-algo", "compress-algo", "compress-level", "bzip2-compress-level",
        "s2k-mode", "s2k-digest-algo", "s2k-cipher-algo", "s2k-count", "keyid-format", "display-charset",
        "comment", "set-filename", "set-notation", "sig-notation", "cert-notation", "faked-system-time",
        "group", "gpg-agent-info", "list-options", "verify-options", "personal-digest-preferences",
        "personal-cipher-preferences", "personal-compress-preferences",
    };

    public static bool IsSigning(IReadOnlyList<string> args) =>
        Classify(args) is OpenPgpOperation.Signing or OpenPgpOperation.SigningAndEncryption;

    public static OpenPgpOperation? Classify(IReadOnlyList<string> args)
    {
        var signing = false;
        var verification = false;
        var encryption = false;
        var decryption = false;
        var unobserved = false;

        for (var argumentIndex = 0; argumentIndex < args.Count; argumentIndex++)
        {
            var argument = args[argumentIndex];
            if (argument == "--")
            {
                break;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                var option = argument[2..];
                var equals = option.IndexOf('=');
                if (equals >= 0)
                {
                    option = option[..equals];
                }

                if (SigningOptions.Contains(option))
                {
                    signing = true;
                }

                if (VerificationOptions.Contains(option))
                {
                    verification = true;
                }

                encryption |= option is "encrypt" or "encrypt-files" or "symmetric";
                decryption |= option is "decrypt" or "decrypt-files";
                unobserved |= UnobservedCommands.Contains(option);
                if (equals < 0 && ValueOptions.Contains(option)) argumentIndex++;

                continue;
            }

            if (argument.Length < 2 || argument[0] != '-')
            {
                continue;
            }

            // GnuPG's Git signing form is commonly "-bsau". Both -b
            // (detach-sign) and -s (sign) are meaningful signing options.
            for (var index = 1; index < argument.Length; index++)
            {
                if (argument[index] is 'u' or 'r' or 'o' or 'z')
                {
                    if (index == argument.Length - 1) argumentIndex++;
                    break;
                }
                if (argument[index] is 'b' or 's')
                {
                    signing = true;
                }
                encryption |= argument[index] is 'e' or 'c';
                decryption |= argument[index] == 'd';
                unobserved |= argument[index] is 'k' or 'K';
            }
        }

        // Verification is an explicit operation. It wins if a caller gives
        // GnuPG a mixed or otherwise unusual option set.
        if (verification || unobserved || (decryption && (signing || encryption))) return null;
        if (signing && encryption) return OpenPgpOperation.SigningAndEncryption;
        if (signing) return OpenPgpOperation.Signing;
        if (encryption) return OpenPgpOperation.Encryption;
        return decryption ? OpenPgpOperation.Decryption : null;
    }
}
