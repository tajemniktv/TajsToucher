namespace TajsToucher;

internal static class Diagnostics
{
    public static int Run()
    {
        var wrapperPath = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetFullPath(processPath)
            : "<unavailable>";
        var status = AppStatus.Load();

        Console.WriteLine("TajsToucher diagnostics");
        Console.WriteLine($"Wrapper: {wrapperPath}");
        Console.WriteLine($"Real GPG: {(status.RealGpgPath ?? "not found")}");
        Console.WriteLine($"Saved installation state: {(status.IsInstalled ? "yes" : "no")}");
        Console.WriteLine($"Installed wrapper available: {(status.WrapperAvailable ? "yes" : "no")}");
        Console.WriteLine($"Global Git configuration readable: {(status.GitConfigurationReadable ? "yes" : "no")}");
        Console.WriteLine($"Global gpg.openpgp.program matches installation: {(status.GitConfigurationMatches ? "yes" : "no")}");
        Console.WriteLine($"Status: {status.Summary}");

        Console.WriteLine("Signing detection: ready");
        Console.WriteLine("Notification: fail-open");
        var settings = new ConfigurationStore().LoadNotificationSettings();
        Console.WriteLine($"Notification rules: signing={settings.NotifyOnSigning}, encryption={settings.NotifyOnEncryption}, decryption={settings.NotifyOnDecryption}, failure alerts={settings.NotifyOnFailure}");
        Console.WriteLine($"Operation diagnostics: {(settings.RecordDiagnostics ? "enabled" : "disabled")}");
        Console.WriteLine($"Diagnostic folder: {DiagnosticEventSink.DefaultDirectory}");
        Console.WriteLine("Scope: explicit OpenPGP operations routed through this executable; not global hardware-key monitoring.");
        return status.IsReady ? 0 : 1;
    }
}
