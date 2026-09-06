namespace TajsToucher;

internal static class ApplicationHost
{
    public static int Run(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && args[0].Equals("--notify", StringComparison.OrdinalIgnoreCase))
        {
            return NotificationService.Show(args.Count > 1 ? args[1] : null);
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
            "app" => AppShellLauncher.Show(),
            "settings" => SettingsLauncher.Show(),
            "proxy" => GpgProxy.Run(Array.Empty<string>()),
            "help" => CliHelp.Print(),
            "version" => CliHelp.PrintVersion(),
            _ => GpgProxy.Run(args),
        };
    }
}
