using Microsoft.Win32;

namespace TajsToucher;

internal sealed record InstallationState(
    string WrapperPath,
    string RealGpgPath,
    IReadOnlyList<string> PreviousOpenPgpPrograms);

internal sealed class ConfigurationStore(string registryPath = "Software\\TajsToucher")
{
    internal const int MaximumPreviousPrograms = 32;

    public InstallationState? Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        if (key is null)
        {
            return null;
        }

        var wrapperPath = key.GetValue("WrapperPath") as string;
        var realGpgPath = key.GetValue("RealGpgPath") as string;
        if (string.IsNullOrWhiteSpace(wrapperPath) || string.IsNullOrWhiteSpace(realGpgPath))
        {
            return null;
        }

        var count = key.GetValue("PreviousProgramCount") is int storedCount ? storedCount : 0;
        if (count < 0 || count > MaximumPreviousPrograms)
        {
            return null;
        }

        var previousPrograms = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            if (key.GetValue($"PreviousProgram{index}") is not string value)
            {
                return null;
            }

            previousPrograms.Add(value);
        }

        return new InstallationState(wrapperPath, realGpgPath, previousPrograms);
    }

    public NotificationSettings LoadNotificationSettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        if (key is null)
        {
            return NotificationSettings.Defaults;
        }

        return new NotificationSettings(
                key.GetValue("NotificationTitle") as string ?? NotificationSettings.DefaultTitle,
                key.GetValue("NotificationText") as string ?? NotificationSettings.DefaultText,
                key.GetValue("NotificationIconPath") as string ?? string.Empty,
                key.GetValue("NotificationSound") is not int sound || sound != 0,
                key.GetValue("NotificationCooldownSeconds") is int cooldown ? cooldown : 0)
            {
                NotifyOnSigning = key.GetValue("NotifyOnSigning") is not int signing || signing != 0,
                NotifyOnEncryption = key.GetValue("NotifyOnEncryption") is int encryption && encryption != 0,
                NotifyOnDecryption = key.GetValue("NotifyOnDecryption") is int decryption && decryption != 0,
                NotifyOnFailure = key.GetValue("NotifyOnFailure") is int failure && failure != 0,
                RecordDiagnostics = key.GetValue("RecordDiagnostics") is int diagnostics && diagnostics != 0,
                NotifyOnDevicePresence = key.GetValue("NotifyOnDevicePresence") is int presence && presence != 0,
                NotifyOnLowRetries = key.GetValue("NotifyOnLowRetries") is int retries && retries != 0,
            }
            .Normalize();
    }

    public void SaveNotificationSettings(NotificationSettings settings)
    {
        var normalized = settings.Normalize();
        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true)
                       ?? throw new InvalidOperationException("The per-user configuration key could not be created.");
        key.SetValue("NotificationTitle", normalized.Title, RegistryValueKind.String);
        key.SetValue("NotificationText", normalized.Text, RegistryValueKind.String);
        key.SetValue("NotificationIconPath", normalized.IconPath, RegistryValueKind.String);
        key.SetValue("NotificationSound", normalized.PlaySound ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NotificationCooldownSeconds", normalized.CooldownSeconds, RegistryValueKind.DWord);
        key.SetValue("NotifyOnSigning", normalized.NotifyOnSigning ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NotifyOnEncryption", normalized.NotifyOnEncryption ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NotifyOnDecryption", normalized.NotifyOnDecryption ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NotifyOnFailure", normalized.NotifyOnFailure ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("RecordDiagnostics", normalized.RecordDiagnostics ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NotifyOnDevicePresence", normalized.NotifyOnDevicePresence ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NotifyOnLowRetries", normalized.NotifyOnLowRetries ? 1 : 0, RegistryValueKind.DWord);
    }

    public void Save(InstallationState state)
    {
        if (state.PreviousOpenPgpPrograms.Count > MaximumPreviousPrograms)
        {
            throw new InvalidOperationException("Too many previous Git program values to save safely.");
        }

        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true)
                       ?? throw new InvalidOperationException("The per-user configuration key could not be created.");

        key.SetValue("WrapperPath", state.WrapperPath, RegistryValueKind.String);
        key.SetValue("RealGpgPath", state.RealGpgPath, RegistryValueKind.String);

        var oldCount = key.GetValue("PreviousProgramCount") is int storedCount
            ? Math.Clamp(storedCount, 0, MaximumPreviousPrograms)
            : 0;
        for (var index = 0; index < oldCount; index++)
        {
            key.DeleteValue($"PreviousProgram{index}", throwOnMissingValue: false);
        }

        key.SetValue("PreviousProgramCount", state.PreviousOpenPgpPrograms.Count, RegistryValueKind.DWord);
        for (var index = 0; index < state.PreviousOpenPgpPrograms.Count; index++)
        {
            key.SetValue($"PreviousProgram{index}", state.PreviousOpenPgpPrograms[index], RegistryValueKind.String);
        }
    }

    public void ClearInstallationState()
    {
        bool empty;
        using (var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true))
        {
            if (key is null)
            {
                return;
            }

            key.DeleteValue("WrapperPath", throwOnMissingValue: false);
            key.DeleteValue("RealGpgPath", throwOnMissingValue: false);
            var count = key.GetValue("PreviousProgramCount") is int storedCount && storedCount >= 0 && storedCount <= 32
                ? storedCount
                : 0;
            key.DeleteValue("PreviousProgramCount", throwOnMissingValue: false);
            for (var index = 0; index < count; index++)
            {
                key.DeleteValue($"PreviousProgram{index}", throwOnMissingValue: false);
            }

            empty = key.GetValueNames().Length == 0;
        }

        if (empty)
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
        }
    }
}
