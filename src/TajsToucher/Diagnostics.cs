namespace TajsToucher;

internal static class Diagnostics
{
    public static int Run() => Run(Console.Out);

    internal static string CaptureReport(Action<TextWriter>? writeReport = null)
    {
        using var output = new StringWriter();
        try { (writeReport ?? (writer => Run(writer)))(output); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            output.WriteLine("Setup diagnostics could not complete. No configuration was changed.");
            output.WriteLine(exception.Message);
        }
        return output.ToString();
    }

    private static int Run(TextWriter output)
    {
        var wrapperPath = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetFullPath(processPath)
            : "<unavailable>";
        var status = AppStatus.Load();

        output.WriteLine("TajsToucher diagnostics");
        output.WriteLine($"Wrapper: {wrapperPath}");
        output.WriteLine($"Real GPG: {(status.RealGpgPath ?? "not found")}");
        output.WriteLine($"Saved installation state: {(status.IsInstalled ? "yes" : "no")}");
        output.WriteLine($"Installed wrapper available: {(status.WrapperAvailable ? "yes" : "no")}");
        output.WriteLine($"Global Git configuration readable: {(status.GitConfigurationReadable ? "yes" : "no")}");
        output.WriteLine($"Global gpg.openpgp.program matches installation: {(status.GitConfigurationMatches ? "yes" : "no")}");
        output.WriteLine($"Status: {status.Summary}");

        output.WriteLine("Signing detection: ready");
        output.WriteLine("Notification: fail-open");
        var settings = new ConfigurationStore().LoadNotificationSettings();
        output.WriteLine($"Notification rules: signing={settings.NotifyOnSigning}, encryption={settings.NotifyOnEncryption}, decryption={settings.NotifyOnDecryption}, failure alerts={settings.NotifyOnFailure}");
        output.WriteLine($"Operation diagnostics: {(settings.RecordDiagnostics ? "enabled" : "disabled")}");
        output.WriteLine($"Diagnostic folder: {DiagnosticEventSink.DefaultDirectory}");
        output.WriteLine("Scope: explicit OpenPGP operations routed through this executable; not global hardware-key monitoring.");
        return status.IsReady ? 0 : 1;
    }
}
