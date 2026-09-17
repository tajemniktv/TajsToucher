using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TajsToucher;

namespace TajsToucher.Pages;

public sealed partial class HomePage : Page
{
    private bool refreshing;
    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStatus();
    }

    public event Action<AppPage>? NavigateRequested;

    internal async void RefreshStatus()
    {
        if (refreshing) return;
        refreshing = true;
        AppStatus status;
        try
        {
            status = await Task.Run(AppStatus.Load);
        }
        catch (Exception)
        {
            status = new AppStatus(false, false, false, null) { StatusReadFailed = true };
        }
        finally { refreshing = false; }

        StatusDot.Foreground = status.IsReady ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        StatusTitle.Text = status.IsReady ? "Global OpenPGP wrapper ready" : "Needs attention";
        StatusDescription.Text = status.Summary;
        StatusComparison.Text = status.ComparisonDetails;
        StatusComparison.Visibility = status.IsReady || status.StatusReadFailed ? Visibility.Collapsed : Visibility.Visible;
        SigningValue.Text = status.SigningValue;
        SigningValue.Foreground = status.IsReady ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        SigningDetail.Text = status.SigningDetail;
        GpgValue.Text = status.GpgValue;
        GpgValue.Foreground = status.RealGpgAvailable ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
    }

    private void RefreshStatus_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void OpenEnabledFor_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(AppPage.EnabledFor);

}
