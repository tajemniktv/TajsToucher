namespace TajsToucher;

internal static class NotificationService
{
    private const int BalloonDurationMilliseconds = 10_000;

    public static bool TryLaunch(string? repositoryName)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            var arguments = new List<string> { "--notify" };
            if (!string.IsNullOrWhiteSpace(repositoryName))
            {
                arguments.Add(repositoryName);
            }

            // Use bInheritHandles=false so a ten-second notification cannot
            // keep Git's stdin/stdout/stderr pipes alive after GPG exits.
            return DetachedProcessLauncher.TryLaunch(executable, arguments);
        }
        catch
        {
            // Notifications are explicitly fail-open. Signing must continue
            // even when Explorer or the helper process is unavailable.
            return false;
        }
    }

    public static int Show(string? repositoryName)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var settings = new ConfigurationStore().LoadNotificationSettings();
        var customIcon = NotificationIconLoader.TryLoad(settings.IconPath);
        try
        {
            using var icon = new NotifyIcon
            {
                Icon = customIcon ?? SystemIcons.Information,
                Visible = true,
                BalloonTipIcon = ToolTipIcon.Info,
                BalloonTipTitle = RenderTitle(settings.Title, repositoryName),
                BalloonTipText = NotificationTemplate.Render(settings.Text, repositoryName),
            };
            using var timer = new System.Windows.Forms.Timer { Interval = BalloonDurationMilliseconds };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                icon.Visible = false;
                Application.ExitThread();
            };
            timer.Start();
            icon.ShowBalloonTip(BalloonDurationMilliseconds);
            Application.Run();
            return 0;
        }
        finally
        {
            customIcon?.Dispose();
        }
    }

    private static string RenderTitle(string title, string? repositoryName)
    {
        return title.Replace("{Repository}", repositoryName ?? "unknown", StringComparison.OrdinalIgnoreCase);
    }

}
