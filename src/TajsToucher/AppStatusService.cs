namespace TajsToucher;

internal sealed record AppStatus(
    bool IsInstalled,
    bool GitConfigurationMatches,
    bool RealGpgAvailable,
    string? RealGpgPath)
{
    public bool IsReady => IsInstalled && GitConfigurationMatches && RealGpgAvailable;

    public static AppStatus Load()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        var configurationStore = new ConfigurationStore();
        var installation = configurationStore.Load();
        var wrapperPath = installation?.WrapperPath ?? processPath;

        var gitConfigurationMatches = false;
        if (installation is not null)
        {
            var gitConfig = new GitConfigService();
            if (gitConfig.TryGetGlobalPrograms(out var programs))
            {
                gitConfigurationMatches = programs.Any(program =>
                    ExecutableLocator.AreSamePath(program, installation.WrapperPath));
            }
        }

        var realGpgPath = ExecutableLocator.FindGpg(installation?.RealGpgPath, wrapperPath);
        return new AppStatus(
            installation is not null,
            gitConfigurationMatches,
            realGpgPath is not null,
            realGpgPath);
    }
}
