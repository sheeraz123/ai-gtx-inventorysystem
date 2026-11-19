using System.Runtime.InteropServices;
using AI.GTX.Harness.Infrastructure.Codex;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class ProcessRunnerTests
{
    private static ProcessRequest Request(string mode, int timeout = 10, string input = "")
    {
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../AI.GTX.Harness.ProcessProbe/bin/{configuration}/net11.0/AI.GTX.Harness.ProcessProbe.dll"));
        return new(host, [probe, mode], AppContext.BaseDirectory, input, timeout);
    }
    [Fact] public async Task CapturesBothStreamsAndUsesLiteralInput()
    {
        var result = await new ControlledProcessRunner(new()).RunAsync(Request("echo", input: "literal ; & $(text)"), default);
        Assert.Equal(ProcessFailure.None, result.Failure); Assert.Equal(0, result.ExitCode);
        Assert.Equal("literal ; & $(text)", result.Output); Assert.Equal("diagnostic", result.Error);
    }
    [Fact] public async Task TimeoutTerminatesChild() => Assert.Equal(ProcessFailure.TimedOut, (await new ControlledProcessRunner(new()).RunAsync(Request("wait", 1), default)).Failure);
    [Fact] public async Task CancellationTerminatesChild()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        Assert.Equal(ProcessFailure.Canceled, (await new ControlledProcessRunner(new()).RunAsync(Request("wait"), ct.Token)).Failure);
    }
    [Fact] public async Task OutputIsBounded() => Assert.Equal(ProcessFailure.OutputLimit, (await new ControlledProcessRunner(new()).RunAsync(Request("flood"), default)).Failure);
    [Fact] public async Task CapturedSecretsAreRedacted()
    {
        var result = await new ControlledProcessRunner(new(() => ["sensitive-fixture"])).RunAsync(Request("echo", input: "sensitive-fixture"), default);
        Assert.DoesNotContain("sensitive-fixture", result.Output); Assert.Contains("[REDACTED]", result.Output);
    }
}
