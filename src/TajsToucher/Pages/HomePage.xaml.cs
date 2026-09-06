using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TajsToucher;

namespace TajsToucher.Pages;

public sealed partial class HomePage : Page
{
    private AppStatus currentStatus = new(false, false, false, null);

    public HomePage()
    {
        InitializeComponent();
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
            status = new AppStatus(false, false, false, null);
        }

        currentStatus = status;
        StatusDot.Foreground = status.IsReady ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarningBrush"];
        StatusTitle.Text = status.IsReady ? "Ready for Git signing" : "Needs setup";
        StatusDescription.Text = status.IsReady
            ? "Git is connected and TajsToucher can reach your real GnuPG installation."
            : "Use Install for Git below to connect Git, then keep working from this dashboard.";
        SigningValue.Text = status.IsReady ? "Enabled" : "Not enabled";
        SigningValue.Foreground = status.IsReady ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarningBrush"];
        SigningDetail.Text = status.GitConfigurationMatches ? "Git is using TajsToucher" : "Git is not connected yet";
        GpgValue.Text = status.RealGpgAvailable ? "Available" : "Unavailable";
        GpgValue.Foreground = status.RealGpgAvailable ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarningBrush"];
        InstallButton.Visibility = status.IsReady ? Visibility.Collapsed : Visibility.Visible;
        UninstallButton.Visibility = status.IsInstalled && status.GitConfigurationMatches ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(AppPage.Settings);

    private void OpenEnabledFor_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(AppPage.EnabledFor);

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        ActionStatus.Text = NotificationService.TryLaunch("TajsToucher")
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
                : "Install failed; Git was left unchanged.";
            RefreshStatus();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ActionStatus.Text = "Install failed; Git was left unchanged.";
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
                : currentStatus.GitConfigurationMatches
                    ? "Uninstall failed; Git was left unchanged."
                    : "Git changed elsewhere; saved installation state was kept.";
            RefreshStatus();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ActionStatus.Text = "Uninstall failed; Git was left unchanged.";
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
