using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace TajsToucher;

public enum AppPage
{
    Home,
    Settings,
    EnabledFor,
}

internal static class AppShellLauncher
{
    public static int Show(AppPage initialPage = AppPage.Home)
    {
        App.InitialPage = initialPage;
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_initializationParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }
}
