namespace TajsToucher;

internal static class Installer
{
    public static int Install()
    {
        var wrapperPath = CurrentExecutablePath();
        var store = new ConfigurationStore();
        var existingState = store.Load();
        var realGpgPath = ExecutableLocator.FindGpg(existingState?.RealGpgPath, wrapperPath);
        if (realGpgPath is null)
        {
            Console.Error.WriteLine("TajsToucher: could not find a real gpg.exe. Install GnuPG and try again.");
            return 1;
        }

        var git = new GitConfigService();
        if (!git.TryGetGlobalPrograms(out var currentPrograms))
        {
            Console.Error.WriteLine("TajsToucher: could not read Git's global gpg.openpgp.program setting.");
            return 1;
        }

        var currentIsThisInstallation = existingState is not null &&
                                        IsWrapperConfiguration(currentPrograms, existingState.WrapperPath);
        if (!currentIsThisInstallation &&
            IsWrapperConfiguration(currentPrograms, wrapperPath) &&
            existingState is null)
        {
            Console.Error.WriteLine(
                "TajsToucher: Git already points to this wrapper, but no saved configuration exists; refusing to overwrite unknown state.");
            return 1;
        }

        var previousPrograms = currentIsThisInstallation
            ? existingState!.PreviousOpenPgpPrograms
            : currentPrograms;

        if (!git.TrySetGlobalProgram(wrapperPath))
        {
            Console.Error.WriteLine("TajsToucher: could not set Git's global gpg.openpgp.program setting.");
            return 1;
        }

        try
        {
            store.Save(new InstallationState(wrapperPath, realGpgPath, previousPrograms));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            // Do not leave Git pointing at an untracked wrapper if its
            // rollback state cannot be persisted.
            _ = git.TryRestoreGlobalPrograms(currentPrograms);
            Console.Error.WriteLine($"TajsToucher: could not save installation state: {exception.Message}");
            return 1;
        }

        Console.WriteLine($"Installed TajsToucher as Git's global {"gpg.openpgp.program"}.");
        Console.WriteLine($"Real GPG: {realGpgPath}");
        return 0;
    }

    public static int Uninstall()
    {
        var store = new ConfigurationStore();
        var state = store.Load();
        if (state is null)
        {
            Console.Error.WriteLine("TajsToucher: no saved installation state was found.");
            return 1;
        }

        var git = new GitConfigService();
        if (!git.TryGetGlobalPrograms(out var currentPrograms))
        {
            Console.Error.WriteLine("TajsToucher: could not read Git's global gpg.openpgp.program setting.");
            return 1;
        }

        if (!IsWrapperConfiguration(currentPrograms, state.WrapperPath))
        {
            Console.Error.WriteLine(
                "TajsToucher: Git's setting has changed since installation; leaving it untouched and retaining saved state.");
            return 2;
        }

        if (!git.TryRestoreGlobalPrograms(state.PreviousOpenPgpPrograms))
        {
            Console.Error.WriteLine("TajsToucher: could not restore Git's previous gpg.openpgp.program setting.");
            return 1;
        }

        try
        {
            store.ClearInstallationState();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Console.Error.WriteLine($"TajsToucher: Git was restored, but saved state could not be removed: {exception.Message}");
            return 1;
        }

        Console.WriteLine("Uninstalled TajsToucher and restored Git's previous gpg.openpgp.program setting.");
        return 0;
    }

    private static bool IsWrapperConfiguration(IReadOnlyList<string> programs, string wrapperPath)
    {
        return programs.Count == 1 && ExecutableLocator.AreSamePath(programs[0], wrapperPath);
    }

    private static string CurrentExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("The current executable path is unavailable.");
        }

        return Path.GetFullPath(processPath);
    }
}
