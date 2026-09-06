using System.Reflection;

namespace TajsToucher;

internal static class CliHelp
{
    public static int Print()
    {
        Console.WriteLine("TajsToucher - fail-open Windows notifications for Git OpenPGP signing");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  TajsToucher.exe");
        Console.WriteLine("  TajsToucher.exe app");
        Console.WriteLine("  TajsToucher.exe install");
        Console.WriteLine("  TajsToucher.exe uninstall");
        Console.WriteLine("  TajsToucher.exe diagnose");
        Console.WriteLine("  TajsToucher.exe settings");
        Console.WriteLine("  TajsToucher.exe proxy");
        Console.WriteLine("  TajsToucher.exe <gpg arguments>");
        Console.WriteLine();
        Console.WriteLine("Launching without arguments opens the app dashboard.");
        Console.WriteLine("GPG arguments are forwarded to the real GnuPG executable.");
        return 0;
    }

    public static int PrintVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine($"TajsToucher {version?.ToString(3) ?? "0.1.0"}");
        return 0;
    }
}
