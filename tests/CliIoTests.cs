using System.Diagnostics;
using Xunit;

namespace Lofrano.BoundedObservedSet.Tests;

public sealed class CliIoTests
{
    [Theory]
    [InlineData("stdout", 1, "Demo output failed.\n")]
    [InlineData("stderr", 2, "")]
    [InlineData("both", 1, "")]
    public async Task NativeOutputFailuresAreBounded(string mode, int expectedExit, string expectedError)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/dev/full"))
            return;

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BoundedObservedSet.sln")))
            root = root.Parent;
        Assert.NotNull(root);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string demo = Path.Combine(root.FullName, "build", "bin", "BoundedObservedSet.Demo",
            configuration, "net10.0", "BoundedObservedSet.Demo.dll");
        Assert.True(File.Exists(demo), "The demo must be built by the test project reference.");
        string script = mode switch
        {
            "stdout" => "ulimit -c 0; exec dotnet \"$1\" --json >/dev/full",
            "stderr" => "ulimit -c 0; exec dotnet \"$1\" SYNTHETIC_PRIVATE_MARKER 2>/dev/full",
            _ => "ulimit -c 0; exec dotnet \"$1\" --json >/dev/full 2>/dev/full"
        };
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("observed-native-io-test");
        start.ArgumentList.Add(demo);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Process did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellation.Token);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            Assert.Equal(expectedExit, process.ExitCode);
            Assert.Equal("", await output);
            Assert.Equal(expectedError, await error);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
