using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TajsToucher.Pages;

namespace TajsToucher;

public sealed partial class MainWindow : Window
{
    private bool updatingNavigation;

    public MainWindow(AppPage initialPage)
    {
        InitializeComponent();
        AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(AppIcon.Handle));
        ContentFrame.Navigated += OnContentFrameNavigated;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated && ContentFrame.Content is HomePage home)
                home.RefreshStatus();
        };
        Navigate(initialPage);
    }

    internal void Navigate(AppPage page)
    {
        object item = page switch
        {
            AppPage.Devices => AppNavigation.MenuItems.OfType<NavigationViewItem>().First(item => Equals(item.Tag, nameof(AppPage.Devices))),
            AppPage.Settings => AppNavigation.SettingsItem,
            AppPage.EnabledFor => AppNavigation.MenuItems.OfType<NavigationViewItem>().First(item => Equals(item.Tag, nameof(AppPage.EnabledFor))),
            _ => AppNavigation.MenuItems.OfType<NavigationViewItem>().First(item => Equals(item.Tag, nameof(AppPage.Home))),
        };
        updatingNavigation = true;
        try
        {
            AppNavigation.SelectedItem = item;
        }
        finally
        {
            updatingNavigation = false;
        }

        NavigateContent(page);
    }

    private void NavigateContent(AppPage page)
    {
        ContentFrame.Navigate(page switch
        {
            AppPage.Devices => typeof(DevicesPage),
            AppPage.Settings => typeof(SettingsPage),
            AppPage.EnabledFor => typeof(EnabledForPage),
            _ => typeof(HomePage),
        });
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is HomePage homePage)
        {
            homePage.NavigateRequested += Navigate;
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (updatingNavigation)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            NavigateContent(AppPage.Settings);
            return;
        }

        if (args.SelectedItemContainer?.Tag is not string tag ||
            !Enum.TryParse<AppPage>(tag, ignoreCase: true, out var page))
        {
            return;
        }

        NavigateContent(page);
    }
}
