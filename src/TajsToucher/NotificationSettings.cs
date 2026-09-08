namespace TajsToucher;

internal sealed record NotificationSettings(string Title, string Text, string IconPath, bool PlaySound = true, int CooldownSeconds = 0)
{
    public const string DefaultTitle = "YubiKey yearns touching";
    public const string DefaultText =
        "GnuPG is requesting {Operation}. Hardware-key interaction may be required. Repository: {Repository}.";
    private const string LegacyDefaultText =
        "Git is requesting an OpenPGP signature. Please touchy touch the YubiKey while it flashes. Repository: {Repository}.";

    public bool NotifyOnSigning { get; init; } = true;
    public bool NotifyOnEncryption { get; init; }
    public bool NotifyOnDecryption { get; init; }
    public bool NotifyOnFailure { get; init; }
    public bool RecordDiagnostics { get; init; }
    public bool NotifyOnDevicePresence { get; init; }
    public bool NotifyOnLowRetries { get; init; }

    public static NotificationSettings Defaults { get; } = new(DefaultTitle, DefaultText, string.Empty);

    public NotificationSettings Normalize()
    {
        return this with
        {
            Title = string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title.Trim(),
            Text = string.IsNullOrWhiteSpace(Text) || Text.Trim() == LegacyDefaultText ? DefaultText : Text.Trim(),
            IconPath = IconPath?.Trim() ?? string.Empty,
            CooldownSeconds = Math.Clamp(CooldownSeconds, 0, 300),
        };
    }
}

internal static class NotificationTemplate
{
    private const string RepositoryToken = "{Repository}";

    public static string Render(string template, string? repositoryName, OpenPgpOperation operation = OpenPgpOperation.Signing)
    {
        var text = RenderTitle(template, repositoryName, operation);

        if (!string.IsNullOrWhiteSpace(repositoryName) &&
            !template.Contains(RepositoryToken, StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} Repository: {repositoryName}.";
        }

        return text;
    }

    public static string RenderTitle(string template, string? repositoryName, OpenPgpOperation operation) =>
        template.Replace("{Operation}", OperationEvent.Describe(operation), StringComparison.OrdinalIgnoreCase).Replace(
            RepositoryToken,
            repositoryName ?? "unknown",
            StringComparison.OrdinalIgnoreCase);
}
