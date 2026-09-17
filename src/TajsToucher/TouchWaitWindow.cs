namespace TajsToucher;

// Only the detached helper initializes WinUI; the GPG forwarding process does not.
internal static class TouchWaitWindow
{
    internal static void Show(EventWaitHandle ended)
    {
        if (!ended.WaitOne(0)) AppShellLauncher.Show(touchWait: ended);
    }
}
