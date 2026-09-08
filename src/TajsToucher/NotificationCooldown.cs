using Microsoft.Win32;
using System.Security.Principal;

namespace TajsToucher;

internal static class NotificationCooldown
{
    // Only notification helpers use this lock; the GPG forwarding process never waits on it.
    public static bool TryAcquire(int seconds)
    {
        if (seconds <= 0)
        {
            return true;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return TryAcquire(seconds, @"Software\TajsToucher\Runtime",
                @"Local\TajsToucher.NotificationCooldown." + identity.User?.Value);
        }
        catch
        {
            return true;
        }
    }

    internal static bool TryAcquire(int seconds, string registryPath, string mutexName)
    {
        if (seconds <= 0) return true;
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
                    return true;
                }

                var now = DateTime.UtcNow.Ticks;
                var previous = key.GetValue("LastNotificationUtcTicks") is long ticks ? ticks : 0;
                if (!IsDue(previous, now, seconds))
                {
                    return false;
                }

                key.SetValue("LastNotificationUtcTicks", now, RegistryValueKind.QWord);
                return true;
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }
        catch
        {
            // Broken optional cooldown state must not suppress every future prompt.
            return true;
        }
    }

    internal static bool IsDue(long previousTicks, long nowTicks, int seconds) =>
        seconds <= 0 || previousTicks <= 0 || previousTicks > nowTicks ||
        nowTicks - previousTicks >= TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 300)).Ticks;
}
