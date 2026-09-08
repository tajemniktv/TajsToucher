namespace TajsToucher;

internal static class ApplicationHost
{
    public static int Run(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && args[0] == "--operation-event")
        {
            return OperationObservation.RunHelper(args);
        }

        if (args.Count > 0 && (args[0].Equals("--notify", StringComparison.OrdinalIgnoreCase) ||
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
            return GpgProxy.Run(args);
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
            "proxy" => GpgProxy.Run(Array.Empty<string>()),
            "help" => CliHelp.Print(),
            "version" => CliHelp.PrintVersion(),
            _ => GpgProxy.Run(args),
        };
    }
}
