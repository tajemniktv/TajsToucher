namespace TajsToucher;

internal static class SettingsLauncher
{
    public static int Show()
    {
        return AppShellLauncher.Show(AppPage.Settings);
    }
}
