namespace TajsToucher;

internal sealed record AppStatus(
    bool IsInstalled,
    bool GitConfigurationMatches,
    bool RealGpgAvailable,
    string? RealGpgPath)
{
    public bool WrapperAvailable { get; init; }
    public bool GitConfigurationReadable { get; init; }
    public bool StatusReadFailed { get; init; }
    public string? SavedWrapperPath { get; init; }
    public string? RunningWrapperPath { get; init; }
    public string? GitExecutablePath { get; init; }
    public IReadOnlyList<string> GitPrograms { get; init; } = Array.Empty<string>();
    // Quote values so empty strings, whitespace and embedded newlines remain distinguishable.
    private static string DescribePath(string? value) => value is null ? "<unavailable>" : System.Text.Json.JsonSerializer.Serialize(value);
    public string ComparisonDetails => string.Join(Environment.NewLine,
        $"Expected (HKCU\\Software\\TajsToucher\\WrapperPath): {DescribePath(SavedWrapperPath)}",
        $"Running executable: {DescribePath(RunningWrapperPath)}",
        $"Git executable queried: {DescribePath(GitExecutablePath)}",
        !GitConfigurationReadable ? "Actual global gpg.openpgp.program: <could not read; not a confirmed mismatch>"
            : GitPrograms.Count == 0 ? "Actual global gpg.openpgp.program: <not set>"
            : $"Actual global gpg.openpgp.program ({GitPrograms.Count} value(s)):" + Environment.NewLine +
              string.Join(Environment.NewLine, GitPrograms.Select((value, index) => $"  [{index + 1}] {DescribePath(value)}")));
    public bool IsReady => !StatusReadFailed && IsInstalled && GitConfigurationReadable && GitConfigurationMatches && WrapperAvailable && RealGpgAvailable;
    public string SigningValue => StatusReadFailed || !GitConfigurationReadable ? "Unknown" : IsReady ? "Enabled" : "Not enabled";
    public string SigningDetail => StatusReadFailed ? "Setup status could not be read; run diagnose before changing configuration."
        : !GitConfigurationReadable ? "Global Git configuration could not be read; run diagnose before changing configuration."
        : GitConfigurationMatches ? "Global Git setting points to TajsToucher" : "Global Git setting does not match";
    public string GpgValue => StatusReadFailed ? "Unknown" : RealGpgAvailable ? "Available" : "Unavailable";
    public bool CanInstall => !StatusReadFailed && !IsReady;
    public bool CanUninstall => !StatusReadFailed && IsInstalled && GitConfigurationMatches;

    public string Summary => StatusReadFailed
        ? "Setup status could not be read. Run diagnose for details."
        : !GitConfigurationReadable
        ? "Git's global configuration could not be read. Check that Git is installed and run diagnose."
        : !IsInstalled ? "No saved installation. Use Install for Git to connect the wrapper."
        : !WrapperAvailable ? "The installed wrapper is missing. Install again from the executable's final location."
        : !GitConfigurationMatches ? "Git's global program setting differs or has multiple values. Review it before reinstalling."
        : !RealGpgAvailable ? "Real GnuPG was not found. Install GnuPG, then retry setup."
        : "The global OpenPGP wrapper is ready. Repository settings and gpg.format can override it.";

    public static AppStatus Load()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        var configurationStore = new ConfigurationStore();
        var installation = configurationStore.Load();
        var wrapperPath = installation?.WrapperPath ?? processPath;

        var gitConfigurationMatches = false;
        var gitConfig = new GitConfigService();
        var readable = gitConfig.TryGetGlobalPrograms(out var programs);
        if (installation is not null && readable)
        {
            gitConfigurationMatches = GitConfigService.IsWrapperConfiguration(programs, installation.WrapperPath);
        }

        var realGpgPath = ExecutableLocator.FindGpg(installation?.RealGpgPath, wrapperPath);
        return new AppStatus(
            installation is not null,
            gitConfigurationMatches,
            realGpgPath is not null,
            realGpgPath)
        {
            WrapperAvailable = installation is not null && File.Exists(installation.WrapperPath),
            GitConfigurationReadable = readable,
            SavedWrapperPath = installation?.WrapperPath,
            RunningWrapperPath = processPath,
            GitExecutablePath = ExecutableLocator.FindGit(),
            GitPrograms = programs.ToArray(),
        };
    }
}
