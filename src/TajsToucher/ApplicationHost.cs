namespace TajsToucher;

internal static class ApplicationHost
{
    public static int Run(IReadOnlyList<string> args)
    {
        var marker = Environment.GetEnvironmentVariable(HelperDispatch.EnvironmentVariable);
        Environment.SetEnvironmentVariable(HelperDispatch.EnvironmentVariable, null);
        return Run(args, marker, GpgProxy.Run, OperationObservation.RunHelper);
    }

    internal static int Run(IReadOnlyList<string> args, string? marker,
        Func<IReadOnlyList<string>, int> forward, Func<IReadOnlyList<string>, int> operationHelper)
    {
        if (marker == HelperDispatch.Operation && args.Count > 0 && args[0] == "--operation-event")
        {
            return operationHelper(args);
        }

        if (marker == HelperDispatch.Notification && args.Count > 0 && (args[0].Equals("--notify", StringComparison.OrdinalIgnoreCase) ||
                               args[0].Equals("--notify-test", StringComparison.OrdinalIgnoreCase)))
        {
            return NotificationService.Show(args.Count > 1 ? args[1] : null,
                bypassCooldown: args[0].Equals("--notify-test", StringComparison.OrdinalIgnoreCase));
        }

        if (args.Count == 0)
        {
            return AppShellLauncher.Show();
        }

        // Only exact, one-argument bare subcommands are reserved. This keeps
        // all normal GPG invocations, including a filename named "help" or
        // "version", transparent.
        if (args.Count != 1 || args[0].StartsWith("-", StringComparison.Ordinal))
        {
            return forward(args);
        }

        return args[0].ToLowerInvariant() switch
        {
            "install" => Installer.Install(),
            "uninstall" => Installer.Uninstall(),
            "diagnose" => Diagnostics.Run(),
            "diagnose-devices" => DeviceDiagnostics.Run(),
            "devices" => AppShellLauncher.Show(AppPage.Devices),
            "app" => AppShellLauncher.Show(),
            "settings" => SettingsLauncher.Show(),
            "proxy" => forward(Array.Empty<string>()),
            "help" => CliHelp.Print(),
            "version" => CliHelp.PrintVersion(),
            _ => forward(args),
        };
    }
}
