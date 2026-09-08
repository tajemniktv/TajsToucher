using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace TajsToucher.Pages;

public sealed partial class EnabledForPage : Page
{
    public EnabledForPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStatus();
    }

    internal void RefreshStatus(AppStatus? status = null)
    {
        DiagnosticsPathText.Text = DiagnosticEventSink.DefaultDirectory;
        try
        {
            GitStatusText.Text = (status ?? AppStatus.Load()).Summary;
            var settings = new ConfigurationStore().LoadNotificationSettings();
            OperationRulesText.Text = $"Signing: {State(settings.NotifyOnSigning)} · Encryption: {State(settings.NotifyOnEncryption)} · Decryption: {State(settings.NotifyOnDecryption)} · Failure alerts: {State(settings.NotifyOnFailure)}";
            DiagnosticsStatusText.Text = settings.RecordDiagnostics
                ? "Recording metadata only. Two files, at most 64 KiB each."
                : "Recording is off. Existing logs, if any, are retained.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            GitStatusText.Text = "Configuration could not be read. Run diagnose for setup details.";
            OperationRulesText.Text = "Notification rules are unavailable.";
            DiagnosticsStatusText.Text = "Recording status is unavailable.";
        }
    }

    internal void ConfigureEmbedded()
    {
        PageContent.Padding = new Thickness(0);
        PageScroll.VerticalScrollMode = ScrollMode.Disabled;
        PageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private static string State(bool enabled) => enabled ? "on" : "off";

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(DiagnosticEventSink.DefaultDirectory))
            {
                DiagnosticsStatusText.Text = "No diagnostics folder yet. Enable recording, save settings, then run an operation through TajsToucher.";
                return;
            }
            Process.Start(new ProcessStartInfo(DiagnosticEventSink.DefaultDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DiagnosticsStatusText.Text = "The diagnostics folder could not be opened.";
        }
    }
}
