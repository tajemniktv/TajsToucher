namespace TajsToucher;

internal sealed class GitConfigService
{
    private const string OpenPgpProgramKey = "gpg.openpgp.program";

    public bool TryGetGlobalPrograms(out IReadOnlyList<string> programs)
    {
        programs = Array.Empty<string>();
        var git = ExecutableLocator.FindGit();
        if (git is null)
        {
            return false;
        }

        var result = ProcessRunner.Run(
            git,
            new[] { "config", "--global", "--get-all", OpenPgpProgramKey },
            null,
            TimeSpan.FromSeconds(3));

        // Git returns non-zero when the key is absent. That is a valid empty
        // configuration, but a timeout/start failure is not.
        if (!result.Started || result.TimedOut)
        {
            return false;
        }

        programs = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0)
            .ToArray();
        return result.ExitCode == 0 ||
               (result.ExitCode == 1 && programs.Count == 0 && string.IsNullOrWhiteSpace(result.StandardError));
    }

    public bool TrySetGlobalProgram(string wrapperPath)
    {
        return RunConfigCommand("--replace-all", wrapperPath).ExitCode == 0;
    }

    public bool TryUnsetGlobalPrograms()
    {
        var result = RunConfigCommand("--unset-all");
        return result.ExitCode == 0 || result.ExitCode == 5;
    }

    public bool TryAddGlobalProgram(string program)
    {
        return RunConfigCommand("--add", program).ExitCode == 0;
    }

    public bool TryRestoreGlobalPrograms(IReadOnlyList<string> programs)
    {
        if (!TryUnsetGlobalPrograms())
        {
            return false;
        }

        foreach (var program in programs)
        {
            if (!TryAddGlobalProgram(program))
            {
                return false;
            }
        }

        return true;
    }

    private static ProcessResult RunConfigCommand(string operation, string? value = null)
    {
        var git = ExecutableLocator.FindGit();
        if (git is null)
        {
            return new ProcessResult(false, -1, string.Empty, string.Empty, false, "Git was not found.");
        }

        var arguments = new List<string> { "config", "--global", operation, OpenPgpProgramKey };
        if (value is not null)
        {
            arguments.Add(value);
        }

        return ProcessRunner.Run(git, arguments, null, TimeSpan.FromSeconds(3));
    }
}
