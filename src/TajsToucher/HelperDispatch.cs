namespace TajsToucher;

internal static class HelperDispatch
{
    // Process-local launch mode, not authentication. Never inferred from GPG arguments.
    internal const string EnvironmentVariable = "TAJSTOUCHER_PRIVATE_HELPER";
    internal const string Operation = "operation-v1";
    internal const string Notification = "notification-v1";

    internal static string CreateEnvironmentBlock(string mode)
    {
        var values = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(pair => (string)pair.Key, pair => (string?)pair.Value ?? "", StringComparer.OrdinalIgnoreCase);
        values[EnvironmentVariable] = mode;
        return string.Join('\0', values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
    }
}
