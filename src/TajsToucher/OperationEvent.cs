namespace TajsToucher;

internal enum OpenPgpOperation
{
    Signing = 1,
    Encryption,
    Decryption,
    SigningAndEncryption,
}

internal enum OperationPhase
{
    Requested = 1,
    Succeeded,
    Failed,
}

// Deliberately excludes arguments, key IDs, filenames, repositories, and payloads.
internal sealed record OperationEvent(
    Guid OperationId,
    DateTimeOffset OccurredAtUtc,
    OpenPgpOperation Operation,
    OperationPhase Phase,
    int? ExitCode = null,
    long? ElapsedMilliseconds = null)
{
    public bool IsValid => OperationId != Guid.Empty && Enum.IsDefined(Operation) && Enum.IsDefined(Phase) &&
        (Phase == OperationPhase.Requested
            ? ExitCode is null && ElapsedMilliseconds is null
            : ElapsedMilliseconds is >= 0 && ExitCode.HasValue &&
              (Phase == OperationPhase.Succeeded ? ExitCode == 0 : ExitCode != 0));

    public static string Describe(OpenPgpOperation operation) => operation switch
    {
        OpenPgpOperation.Signing => "OpenPGP signing",
        OpenPgpOperation.Encryption => "OpenPGP encryption",
        OpenPgpOperation.Decryption => "OpenPGP decryption",
        OpenPgpOperation.SigningAndEncryption => "OpenPGP signing and encryption",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
