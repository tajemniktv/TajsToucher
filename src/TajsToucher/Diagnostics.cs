namespace TajsToucher;

internal static class Diagnostics
{
    public static int Run()
    {
        var wrapperPath = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetFullPath(processPath)
            : "<unavailable>";
        var store = new ConfigurationStore();
        var state = store.Load();
        var realGpg = ExecutableLocator.FindGpg(state?.RealGpgPath, wrapperPath);
        var gitPath = ExecutableLocator.FindGit();

        Console.WriteLine("TajsToucher diagnostics");
        Console.WriteLine($"Wrapper: {wrapperPath}");
        Console.WriteLine($"Git: {(gitPath ?? "not found")}");
        Console.WriteLine($"Real GPG: {(realGpg ?? "not found")}");
        Console.WriteLine($"Saved installation state: {(state is null ? "no" : "yes")}");

        if (gitPath is not null)
        {
            var git = new GitConfigService();
            if (git.TryGetGlobalPrograms(out var programs))
            {
                var configured = programs.Count == 0
                    ? "not set"
                    : programs.Count == 1 && ExecutableLocator.AreSamePath(programs[0], wrapperPath)
                        ? "TajsToucher"
                        : "set to another program";
                Console.WriteLine($"Global gpg.openpgp.program: {configured}");
            }
            else
            {
                Console.WriteLine("Global gpg.openpgp.program: unreadable");
            }
        }

        Console.WriteLine("Signing detection: ready");
        Console.WriteLine("Notification: fail-open");
        return realGpg is null || gitPath is null ? 1 : 0;
    }
}
