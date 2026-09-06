using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TajsToucher.Pages;
using TajsToucher.Services;
using WinRT.Interop;

namespace TajsToucher;

public partial class App : Application
{
    private readonly WindowsSystemTrayService trayService = new();
    private MainWindow? mainWindow;
    private bool exitRequested;

    internal static AppPage InitialPage { get; set; } = AppPage.Home;

    internal static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        trayService.OpenDashboardRequested += (_, _) => ShowDashboard(AppPage.Home);
        trayService.OpenSettingsRequested += (_, _) => ShowDashboard(AppPage.Settings);
        trayService.OpenEnabledForRequested += (_, _) => ShowDashboard(AppPage.EnabledFor);
        trayService.ExitRequested += (_, _) => RequestExit();
        trayService.Initialize();

        ShowDashboard(InitialPage);
    }

    internal static nint GetMainWindowHandle()
    {
        return MainWindowInstance is null
            ? 0
            : WindowNative.GetWindowHandle(MainWindowInstance);
    }

    private void ShowDashboard(AppPage page)
    {
        if (mainWindow is null)
        {
            mainWindow = new MainWindow(page);
            MainWindowInstance = mainWindow;
            mainWindow.AppWindow.Closing += OnWindowClosing;
        }
        else
        {
            mainWindow.Navigate(page);
        }

        mainWindow.AppWindow.Show();
        mainWindow.Activate();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (exitRequested)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private void RequestExit()
    {
        if (exitRequested)
        {
            return;
        }

        exitRequested = true;
        trayService.Dispose();
        mainWindow?.Close();
        Exit();
    }
}
