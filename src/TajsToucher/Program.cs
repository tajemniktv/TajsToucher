namespace TajsToucher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return ApplicationHost.Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"TajsToucher: {exception.Message}");
            return 1;
        }
    }
}
