using AI.GTX.Harness.Core;
using AI.GTX.Harness.Infrastructure;
using AI.GTX.Harness.Infrastructure.Codex;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class LiveExecutionGateTests
{
    [Theory] [InlineData("developer")] [InlineData("reviewer")]
    public async Task StructuredCodexRolesCannotStartWithoutVerifiedIsolation(string role)
    {
        var runner = new CodexTests.FakeRunner();
        var provider = new CodexExecutionProvider(runner, Options.Create(new CodexOptions()), new(), NullLogger<CodexExecutionProvider>.Instance, new UnavailableExecutionIsolation());
        var result = await provider.GenerateAsync(role, "test", "{}", "default", default);
        Assert.Equal(ProviderError.Unsupported, result.Error); Assert.Empty(runner.Calls);
    }
    [Fact] public async Task DefaultTesterBackendNeverFallsBackToHostExecution()
    {
        Assert.False((await new UnavailableExecutionIsolation().VerifyAsync(default)).Verified);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new UnavailableIsolatedCommandExecutor().ExecuteAsync(Path.GetTempPath(), TestCommand.Build, default));
    }
}
