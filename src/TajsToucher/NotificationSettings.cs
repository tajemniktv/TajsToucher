namespace TajsToucher;

internal sealed record NotificationSettings(string Title, string Text, string IconPath)
{
    public const string DefaultTitle = "YubiKey yearns touching";
    public const string DefaultText =
        "Git is requesting an OpenPGP signature. Please touchy touch the YubiKey while it flashes. Repository: {Repository}.";

    public static NotificationSettings Defaults { get; } = new(DefaultTitle, DefaultText, string.Empty);

    public NotificationSettings Normalize()
    {
        return new NotificationSettings(
            string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title.Trim(),
            string.IsNullOrWhiteSpace(Text) ? DefaultText : Text.Trim(),
            IconPath?.Trim() ?? string.Empty);
    }
}

internal static class NotificationTemplate
{
    private const string RepositoryToken = "{Repository}";

    public static string Render(string template, string? repositoryName)
    {
        var text = template.Replace(
            RepositoryToken,
            repositoryName ?? "unknown",
            StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(repositoryName) &&
            !template.Contains(RepositoryToken, StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} Repository: {repositoryName}.";
        }

        return text;
    }
}
