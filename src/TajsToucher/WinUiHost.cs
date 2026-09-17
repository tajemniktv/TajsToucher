using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace TajsToucher;

public enum AppPage
{
    Home,
    Settings,
    EnabledFor,
    Devices,
}

internal static class AppShellLauncher
{
    public static int Show(AppPage initialPage = AppPage.Home, EventWaitHandle? touchWait = null)
    {
        App.InitialPage = initialPage;
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_initializationParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(touchWait);
        });
        return 0;
    }
}
