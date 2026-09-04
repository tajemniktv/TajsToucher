namespace TajsToucher;

internal static class OperationClassifier
{
    private static readonly HashSet<string> SigningOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sign",
        "detach-sign",
        "clearsign",
    };

    private static readonly HashSet<string> VerificationOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "verify",
        "verify-files",
    };

    public static bool IsSigning(IReadOnlyList<string> args)
    {
        var signing = false;
        var verification = false;

        foreach (var argument in args)
        {
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
                if (argument[index] is 'b' or 's')
                {
                    signing = true;
                }
            }
        }

        // Verification is an explicit operation. It wins if a caller gives
        // GnuPG a mixed or otherwise unusual option set.
        return signing && !verification;
    }
}
