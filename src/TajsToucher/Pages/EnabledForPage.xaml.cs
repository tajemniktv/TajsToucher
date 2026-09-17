using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace TajsToucher.Pages;

public sealed partial class EnabledForPage : Page
{
    private bool refreshing;
    private bool changingSetup;
    public EnabledForPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStatus();
    }

    internal async void RefreshStatus(AppStatus? status = null)
    {
        if (refreshing || changingSetup) return;
        refreshing = true;
        SetActionsEnabled(false);
        DiagnosticsPathText.Text = DiagnosticEventSink.DefaultDirectory;
        try
        {
            status ??= await Task.Run(AppStatus.Load);
            GitStatusText.Text = status.Summary;
            InstallButton.Visibility = status.CanInstall ? Visibility.Visible : Visibility.Collapsed;
            UninstallButton.Visibility = status.CanUninstall ? Visibility.Visible : Visibility.Collapsed;
            var settings = new ConfigurationStore().LoadNotificationSettings();
            OperationRulesText.Text = $"Signing: {State(settings.NotifyOnSigning)} · Encryption: {State(settings.NotifyOnEncryption)} · Decryption: {State(settings.NotifyOnDecryption)} · Failure alerts: {State(settings.NotifyOnFailure)}";
            DiagnosticsStatusText.Text = settings.RecordDiagnostics
                ? "Recording metadata only. Two files, at most 64 KiB each."
                : "Recording is off. Existing logs, if any, are retained.";
        }
        catch (Exception)
        {
            InstallButton.Visibility = UninstallButton.Visibility = Visibility.Collapsed;
            GitStatusText.Text = "Configuration could not be read. Run diagnose for setup details.";
            OperationRulesText.Text = "Notification rules are unavailable.";
            DiagnosticsStatusText.Text = "Recording status is unavailable.";
        }
        finally { refreshing = false; SetActionsEnabled(true); }
    }

    internal void ConfigureEmbedded()
    {
        PageContent.Padding = new Thickness(0);
        PageScroll.VerticalScrollMode = ScrollMode.Disabled;
        PageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private async void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        DiagnoseButton.IsEnabled = false;
        try
        {
            var report = await Task.Run(() => Diagnostics.CaptureReport());
            RefreshStatus();
            await ShowMessageAsync(report, "Setup diagnostics");
        }
        // This async-void UI boundary must also contain unexpected report/display failures.
        catch (Exception exception)
        {
            ActionStatus.Text = $"Diagnostics could not be displayed: {exception.Message}";
        }
        finally { DiagnoseButton.IsEnabled = true; }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (refreshing || changingSetup) return;
        changingSetup = true;
        SetActionsEnabled(false);
        try
        {
            var result = await Task.Run(() => Installer.Install());
            ActionStatus.Text = result == 0
                ? "Git is now connected to TajsToucher."
                : "Install failed. Run diagnose and check Git's global configuration before retrying.";
        }
        catch (Exception)
        {
            ActionStatus.Text = "Install failed. Check Git's global configuration before retrying.";
        }
        finally { changingSetup = false; RefreshStatus(); }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (refreshing || changingSetup) return;
        changingSetup = true;
        SetActionsEnabled(false);
        try
        {
        var dialog = new ContentDialog
        {
            Title = "Uninstall TajsToucher",
            Content = "Remove TajsToucher from Git's global signing configuration?",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

            var result = await Task.Run(() => Installer.Uninstall());
            ActionStatus.Text = result == 0
                ? "TajsToucher was removed and Git was restored."
                : result == 2
                    ? "Git changed elsewhere; saved installation state was kept."
                    : "Uninstall failed. Check Git's global configuration; restoration may be incomplete.";
        }
        catch (Exception)
        {
            ActionStatus.Text = "Uninstall failed. Check Git's global configuration; restoration may be incomplete.";
        }
        finally { changingSetup = false; RefreshStatus(); }
    }

    private void SetActionsEnabled(bool enabled)
    {
        InstallButton.IsEnabled = UninstallButton.IsEnabled = DiagnoseButton.IsEnabled = enabled;
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 440,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            },
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
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
