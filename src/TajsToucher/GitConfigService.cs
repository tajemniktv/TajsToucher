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
            new[] { "config", "--global", "--null", "--get-all", OpenPgpProgramKey },
            null,
            TimeSpan.FromSeconds(3));

        return TryParsePrograms(result, out programs);
    }

    internal static bool TryParsePrograms(ProcessResult result, out IReadOnlyList<string> programs)
    {
        programs = Array.Empty<string>();
        if (!result.Started || result.TimedOut)
        {
            return false;
        }

        if (result.ExitCode == 1)
        {
            return result.StandardOutput.Length == 0 && string.IsNullOrWhiteSpace(result.StandardError);
        }

        if (result.ExitCode != 0 || !result.StandardOutput.EndsWith('\0'))
        {
            return false;
        }

        // Preserve empty values, whitespace, and embedded newlines for exact restoration.
        programs = result.StandardOutput[..^1].Split('\0');
        return true;
    }

    internal static bool IsWrapperConfiguration(IReadOnlyList<string> programs, string wrapperPath)
    {
        return programs.Count == 1 && ExecutableLocator.AreSamePath(programs[0], wrapperPath);
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
