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
        ContentFrame.Navigated += OnContentFrameNavigated;
        Navigate(initialPage);
    }

    internal void Navigate(AppPage page)
    {
        var item = page switch
        {
            AppPage.Settings => AppNavigation.MenuItems.OfType<NavigationViewItem>().First(item => Equals(item.Tag, nameof(AppPage.Settings))),
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

        if (args.SelectedItemContainer?.Tag is not string tag ||
            !Enum.TryParse<AppPage>(tag, ignoreCase: true, out var page))
        {
            return;
        }

        NavigateContent(page);
    }
}
