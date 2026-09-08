using Microsoft.Win32;
using System.Security.Principal;

namespace TajsToucher;

internal static class NotificationCooldown
{
    // Only notification helpers use this lock; the GPG forwarding process never waits on it.
    public static bool TryShow(int seconds, Action show)
    {
        if (seconds <= 0)
        {
            show();
            return true;
        }

        string mutexName;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            mutexName = @"Local\TajsToucher.NotificationCooldown." + identity.User?.Value;
        }
        catch
        {
            show();
            return true;
        }
        return TryShow(seconds, @"Software\TajsToucher\Runtime", mutexName, show);
    }

    internal static bool TryShow(int seconds, string registryPath, string mutexName, Action show)
    {
        if (seconds <= 0) { show(); return true; }
        var showStarted = false;
        try
        {
            using var mutex = new Mutex(false, mutexName);
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    return false;
                }

                using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
                if (key is null)
                {
                    showStarted = true;
                    show();
                    return true;
                }

                var now = DateTime.UtcNow.Ticks;
                var previous = key.GetValue("LastNotificationUtcTicks") is long ticks ? ticks : 0;
                if (!IsDue(previous, now, seconds))
                {
                    return false;
                }

                showStarted = true;
                show(); // Keep the reservation locked until Windows accepts the notice.
                try { key.SetValue("LastNotificationUtcTicks", DateTime.UtcNow.Ticks, RegistryValueKind.QWord); }
                catch { /* A cooldown write failure cannot undo a successful notice. */ }
                return true;
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }
        catch when (!showStarted)
        {
            // Broken optional cooldown state must not suppress every future prompt.
            show();
            return true;
        }
    }

    internal static bool IsDue(long previousTicks, long nowTicks, int seconds) =>
        seconds <= 0 || previousTicks <= 0 || previousTicks > nowTicks ||
        nowTicks - previousTicks >= TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 300)).Ticks;
}
