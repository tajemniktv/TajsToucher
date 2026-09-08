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
    public bool IsReady => IsInstalled && GitConfigurationReadable && GitConfigurationMatches && WrapperAvailable && RealGpgAvailable;

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
        };
    }
}
