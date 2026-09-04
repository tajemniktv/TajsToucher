namespace TajsToucher;

internal sealed record ProcessResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    string? StartError);

internal static class ProcessRunner
{
    public static ProcessResult Run(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new ProcessResult(false, -1, string.Empty, string.Empty, false, exception.Message);
        }

        if (process is null)
        {
            return new ProcessResult(false, -1, string.Empty, string.Empty, false, "The process could not be started.");
        }

        using (process)
        {
            process.StandardInput.Close();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            var timeoutMilliseconds = Math.Max(1, (int)timeout.TotalMilliseconds);

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between WaitForExit and Kill.
                }

                return new ProcessResult(
                    true,
                    -1,
                    standardOutput.GetAwaiter().GetResult(),
                    standardError.GetAwaiter().GetResult(),
                    true,
                    null);
            }

            return new ProcessResult(
                true,
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult(),
                false,
                null);
        }
    }
}
