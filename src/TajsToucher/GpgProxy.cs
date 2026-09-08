namespace TajsToucher;

internal static class GpgProxy
{
    public static int Run(IReadOnlyList<string> args)
    {
        var wrapperPath = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetFullPath(processPath)
            : string.Empty;
        var state = new ConfigurationStore().Load();
        var realGpg = ExecutableLocator.FindGpg(state?.RealGpgPath, wrapperPath);
        if (realGpg is null)
        {
            Console.Error.WriteLine("TajsToucher: real gpg.exe was not found. Run 'TajsToucher.exe install'.");
            return 1;
        }

        var observation = OperationObservation.TryStart(OperationClassifier.Classify(args));
        var exitCode = ForwardProcess(realGpg, args, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.OpenStandardError(),
            () => CancelIoEx(GetStdHandle(-10), IntPtr.Zero));
        observation?.Complete(exitCode);
        return exitCode;
    }

    internal static int ForwardProcess(string realGpg, IReadOnlyList<string> args, Stream input, Stream output, Stream error,
        Action? cancelPendingInput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = realGpg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Console.Error.WriteLine("TajsToucher: could not start real gpg.exe.");
                return 1;
            }

            // A GUI-subsystem wrapper does not reliably cause .NET to inherit
            // the caller's standard handles when it starts another process.
            // Forward the streams as bytes so Git sees the same data and EOF
            // behavior it would see from a direct GPG invocation.
            using var inputCancellation = new CancellationTokenSource();
            var inputTask = ForwardAsync(input, process.StandardInput.BaseStream, closeDestination: true, inputCancellation.Token);
            var outputTask = ForwardAsync(process.StandardOutput.BaseStream, output, closeDestination: false);
            var errorTask = ForwardAsync(process.StandardError.BaseStream, error, closeDestination: false);

            process.WaitForExit();
            inputCancellation.Cancel();
            // Windows standard-input handles may be synchronous: a token alone
            // cannot interrupt a ReadFile already in progress.
            cancelPendingInput?.Invoke();
            SettleInputAsync(inputTask).GetAwaiter().GetResult();

            // Do not make a completed GPG operation wait for an interactive
            // stdin reader that the child never consumed. Closing the child
            // side lets a pending input copy unwind while output and error
            // are drained to preserve their ordering relative to process exit.
            Task.WaitAll(outputTask, errorTask);
            return process.ExitCode;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Console.Error.WriteLine($"TajsToucher: could not start real gpg.exe: {exception.Message}");
            return 1;
        }
    }

    private static async Task ForwardAsync(Stream source, Stream destination, bool closeDestination,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException)
        {
            // The caller may close a pipe while GPG is still shutting down.
        }
        catch (ObjectDisposedException)
        {
            // The child-side stream can be closed when GPG exits without
            // consuming stdin.
        }
        finally
        {
            if (closeDestination)
            {
                try
                {
                    destination.Close();
                }
                catch (ObjectDisposedException)
                {
                    // The child process may have closed its input already.
                }
            }
        }
    }

    private static async Task SettleInputAsync(Task inputTask)
    {
        try { await inputTask.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // Arbitrary streams/drivers can ignore cancellation. Never restore the
            // old early-exit hang; observe eventual completion/fault in that case.
            _ = ObserveInputAsync(inputTask);
        }
        catch { /* A completed child owns its exit status, not the abandoned input. */ }
    }

    private static async Task ObserveInputAsync(Task inputTask)
    {
        try { await inputTask.ConfigureAwait(false); } catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);
}
