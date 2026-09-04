namespace TajsToucher;

internal static class RepositoryContext
{
    public static string? TryGetName()
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var git = ExecutableLocator.FindGit();
        if (git is null)
        {
            return null;
        }

        var result = ProcessRunner.Run(
            git,
            new[] { "rev-parse", "--show-toplevel" },
            workingDirectory,
            TimeSpan.FromSeconds(1));
        if (!result.Started || result.TimedOut || result.ExitCode != 0)
        {
            return null;
        }

        var root = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        try
        {
            return new DirectoryInfo(root).Name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
