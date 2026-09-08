using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TajsToucher;

namespace TajsToucher.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        InlineSettings.ConfigureEmbedded();
        InlineEnabledFor.ConfigureEmbedded();
        InlineSettings.SettingsSaved += (_, _) => RefreshStatus();
        Loaded += (_, _) => RefreshStatus();
    }

    public event Action<AppPage>? NavigateRequested;

    private void RefreshStatus()
    {
        AppStatus status;
        try
        {
            status = AppStatus.Load();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            status = new AppStatus(false, false, false, null) { StatusReadFailed = true };
        }

        StatusDot.Foreground = status.IsReady ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        StatusTitle.Text = status.IsReady ? "Global OpenPGP wrapper ready" : "Needs attention";
        StatusDescription.Text = status.Summary;
        SigningValue.Text = status.SigningValue;
        SigningValue.Foreground = status.IsReady ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        SigningDetail.Text = status.SigningDetail;
        GpgValue.Text = status.GpgValue;
        GpgValue.Foreground = status.RealGpgAvailable ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        InstallButton.Visibility = status.CanInstall ? Visibility.Visible : Visibility.Collapsed;
        UninstallButton.Visibility = status.CanUninstall ? Visibility.Visible : Visibility.Collapsed;
        InlineEnabledFor.RefreshStatus(status);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(AppPage.Settings);

    private void OpenEnabledFor_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(AppPage.EnabledFor);

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        ActionStatus.Text = NotificationService.TryLaunch("TajsToucher", bypassCooldown: true)
            ? "Test notification launched."
            : "The notification helper could not be started.";
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = Installer.Install();
            ActionStatus.Text = result == 0
                ? "Git is now connected to TajsToucher."
                : "Install failed. Run diagnose and check Git's global configuration before retrying.";
            RefreshStatus();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ActionStatus.Text = "Install failed. Check Git's global configuration before retrying.";
            _ = ShowMessageAsync(exception.Message, "TajsToucher");
        }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
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

        try
        {
            var result = Installer.Uninstall();
            ActionStatus.Text = result == 0
                ? "TajsToucher was removed and Git was restored."
                : result == 2
                    ? "Git changed elsewhere; saved installation state was kept."
                    : "Uninstall failed. Check Git's global configuration; restoration may be incomplete.";
            RefreshStatus();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ActionStatus.Text = "Uninstall failed. Check Git's global configuration; restoration may be incomplete.";
            await ShowMessageAsync(exception.Message, "TajsToucher");
        }
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
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
