namespace TajsToucher;

internal static class ExecutableLocator
{
    public static string? FindGit()
    {
        var candidates = new List<string>();
        AddProgramFilesCandidates(candidates, "Git", "cmd", "git.exe");
        AddProgramFilesCandidates(candidates, "Git", "bin", "git.exe");
        AddPathCandidates(candidates, "git.exe");
        return FirstExisting(candidates);
    }

    public static string? FindGpgConf()
    {
        var candidates = new List<string>();
        AddProgramFilesCandidates(candidates, "GnuPG", "bin", "gpgconf.exe");
        AddPathCandidates(candidates, "gpgconf.exe");
        return FirstExisting(candidates);
    }

    public static string? FindGpg(string? preferredPath, string wrapperPath)
    {
        var preferred = ValidateGpgPath(preferredPath, wrapperPath);
        if (preferred is not null)
        {
            return preferred;
        }

        var candidates = new List<string>();

        var gpgConf = FindGpgConf();
        if (gpgConf is not null)
        {
            var gpgConfDirectory = Path.GetDirectoryName(gpgConf);
            if (gpgConfDirectory is not null)
            {
                candidates.Add(Path.Combine(gpgConfDirectory, "gpg.exe"));
            }

            var bindir = ProcessRunner.Run(
                gpgConf,
                new[] { "--list-dirs", "bindir" },
                null,
                TimeSpan.FromSeconds(2));

            if (bindir.Started && !bindir.TimedOut && bindir.ExitCode == 0)
            {
                foreach (var line in bindir.StandardOutput.Split(
                             new[] { '\r', '\n' },
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    candidates.Add(Path.Combine(line, "gpg.exe"));
                }
            }
        }

        AddProgramFilesCandidates(candidates, "GnuPG", "bin", "gpg.exe");
        AddPathCandidates(candidates, "gpg.exe");

        foreach (var candidate in candidates)
        {
            var valid = ValidateGpgPath(candidate, wrapperPath);
            if (valid is not null)
            {
                return valid;
            }
        }

        return null;
    }

    private static string? ValidateGpgPath(string? candidate, string wrapperPath)
    {
        var normalized = NormalizeExistingPath(candidate);
        if (normalized is null || !string.Equals(Path.GetFileName(normalized), "gpg.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return AreSamePath(normalized, wrapperPath) ? null : normalized;
    }

    public static string? NormalizeExistingPath(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(candidate);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool AreSamePath(string left, string right)
    {
        var leftPath = NormalizeForComparison(left);
        var rightPath = NormalizeForComparison(right);
        return leftPath is not null && rightPath is not null &&
               string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeForComparison(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static void AddProgramFilesCandidates(List<string> candidates, params string[] parts)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(new[] { programFiles }.Concat(parts).ToArray()));
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86) &&
            !string.Equals(programFilesX86, programFiles, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(new[] { programFilesX86 }.Concat(parts).ToArray()));
        }
    }

    private static void AddPathCandidates(List<string> candidates, string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                candidates.Add(Path.Combine(directory, fileName));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Ignore malformed PATH entries.
            }
        }
    }

    private static string? FirstExisting(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeExistingPath(candidate);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return null;
    }
}
