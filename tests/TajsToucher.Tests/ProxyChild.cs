using System.Text;
using System.Text.Json;

namespace TajsToucher.Tests;

// Executed only by forwarding tests as a real child process, never by the test adapter.
internal static class ProxyChild
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--proxy-sdk-isolation")
        {
            using var inputBuffer = new MemoryStream();
            using var outputBuffer = new MemoryStream();
            using var errorBuffer = new MemoryStream();
            var result = GpgProxy.ForwardProcess("dotnet",
                [typeof(ProxyChild).Assembly.Location, "--proxy-child", "--ignore-input"], inputBuffer, outputBuffer, errorBuffer);
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                assembly.GetName().Name?.StartsWith("Yubico", StringComparison.Ordinal) == true);
            return result == 19 && !loaded ? 0 : 1;
        }
        if (args.Length == 0 || args[0] != "--proxy-child") return 2;
        if (args.Length > 1 && args[1] == "--ignore-input") return 19;
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        input.CopyTo(output);
        using var error = Console.OpenStandardError();
        error.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args.Skip(1).ToArray())));
        return 37;
    }
}
