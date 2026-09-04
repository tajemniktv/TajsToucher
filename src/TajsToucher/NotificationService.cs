namespace TajsToucher;

internal static class NotificationService
{
    private const int BalloonDurationMilliseconds = 10_000;

    public static void TryLaunch(string? repositoryName)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return;
            }

            var arguments = new List<string> { "--notify" };
            if (!string.IsNullOrWhiteSpace(repositoryName))
            {
                arguments.Add(repositoryName);
            }

            // Use bInheritHandles=false so a ten-second notification cannot
            // keep Git's stdin/stdout/stderr pipes alive after GPG exits.
            _ = DetachedProcessLauncher.TryLaunch(executable, arguments);
        }
        catch
        {
            // Notifications are explicitly fail-open. Signing must continue
            // even when Explorer or the helper process is unavailable.
        }
    }

    public static int Show(string? repositoryName)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        using var icon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Visible = true,
            BalloonTipIcon = ToolTipIcon.Info,
            BalloonTipTitle = "YubiKey yearns touching",
            BalloonTipText = BuildMessage(repositoryName),
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

    private static string BuildMessage(string? repositoryName)
    {
        var message = "Git is requesting an OpenPGP signature. Please touchy touch the YubiKey while it flashes.";
        return string.IsNullOrWhiteSpace(repositoryName) ? message : $"{message} Repository: {repositoryName}.";
    }
}
