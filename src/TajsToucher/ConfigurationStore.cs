using Microsoft.Win32;

namespace TajsToucher;

internal sealed record InstallationState(
    string WrapperPath,
    string RealGpgPath,
    IReadOnlyList<string> PreviousOpenPgpPrograms);

internal sealed class ConfigurationStore
{
    private const string RegistryPath = "Software\\TajsToucher";

    public InstallationState? Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
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
        if (count < 0 || count > 32)
        {
            return null;
        }

        var previousPrograms = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            if (key.GetValue($"PreviousProgram{index}") is not string value || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            previousPrograms.Add(value);
        }

        return new InstallationState(wrapperPath, realGpgPath, previousPrograms);
    }

    public void Save(InstallationState state)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
                       ?? throw new InvalidOperationException("The per-user configuration key could not be created.");

        key.SetValue("WrapperPath", state.WrapperPath, RegistryValueKind.String);
        key.SetValue("RealGpgPath", state.RealGpgPath, RegistryValueKind.String);

        var oldCount = key.GetValue("PreviousProgramCount") is int storedCount ? storedCount : 0;
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

    public void Clear()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
    }
}
