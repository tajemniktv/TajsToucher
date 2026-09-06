namespace TajsToucher;

internal static class NotificationIconLoader
{
    public static Icon? TryLoad(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        try
        {
            return new Icon(iconPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or ExternalException)
        {
            return null;
        }
    }
}
