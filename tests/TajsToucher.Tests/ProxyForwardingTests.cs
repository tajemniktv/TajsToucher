using System.Text;
using System.Text.Json;

namespace TajsToucher.Tests;

[TestClass]
public sealed class ProxyForwardingTests
{
    [TestMethod]
    [Timeout(15000)]
    public void ForwardingDoesNotLoadYubicoAssembliesInFreshProcess()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        Assert.AreEqual(0, GpgProxy.ForwardProcess("dotnet",
            [typeof(ProxyChild).Assembly.Location, "--proxy-sdk-isolation"], input, output, error));
    }

    [TestMethod]
    [Timeout(15000)]
    public void PreservesBinaryInputOutputArgumentsAndExitCode()
    {
        var bytes = Enumerable.Range(0, 262144).Select(i => (byte)i).ToArray();
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var arguments = new[] { "", "two words", "quote\"inside", @"C:\path with spaces\", "工具", "--version", "line\nbreak" };
        var childArguments = new[] { typeof(ProxyChild).Assembly.Location, "--proxy-child" }.Concat(arguments).ToArray();
        var exitCode = GpgProxy.ForwardProcess("dotnet", childArguments, input, output, error);
        Assert.AreEqual(37, exitCode);
        CollectionAssert.AreEqual(bytes, output.ToArray());
        CollectionAssert.AreEqual(arguments, JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(error.ToArray()))!);
    }

    [TestMethod]
    [Timeout(15000)]
    public void ChildCanExitWithoutWaitingForCallerInput()
    {
        using var input = new PendingInput();
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var exitCode = GpgProxy.ForwardProcess("dotnet",
            new[] { typeof(ProxyChild).Assembly.Location, "--proxy-child", "--ignore-input" }, input, output, error);
        Assert.AreEqual(19, exitCode);
    }

    private sealed class PendingInput : MemoryStream
    {
        private readonly TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(completion.Task);
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => completion.Task;
        protected override void Dispose(bool disposing)
        {
            completion.TrySetResult(0);
            base.Dispose(disposing);
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public void ChildExitCancelsAndSettlesCooperativeInputCopy()
    {
        using var input = new CancellableInput();
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        Assert.AreEqual(19, GpgProxy.ForwardProcess("dotnet", [typeof(ProxyChild).Assembly.Location, "--proxy-child", "--ignore-input"], input, output, error));
        Assert.IsTrue(input.Settled);
        Assert.IsTrue(input.Token.IsCancellationRequested);
    }
    private sealed class CancellableInput : MemoryStream
    {
        public bool Settled { get; private set; }
        public CancellationToken Token { get; private set; }
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            finally { Settled = true; }
        }
    }
}
