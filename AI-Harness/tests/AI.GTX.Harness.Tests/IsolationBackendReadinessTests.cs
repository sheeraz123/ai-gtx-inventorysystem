using AI.GTX.Harness.Core;
using AI.GTX.Harness.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.GTX.Harness.Tests;

// These are host-side fail-closed tests. They are NOT guest filesystem/network/process confinement evidence.
public sealed class IsolationBackendReadinessTests
{
    [Theory]
    [InlineData("main-repository", TestCommand.Build)]
    [InlineData("user-profile", TestCommand.Build)]
    [InlineData("authentication-files", TestCommand.Test)]
    [InlineData("production-secrets", TestCommand.Test)]
    public async Task UnavailableBackendRejectsProtectedResourceRequestsBeforeExecution(string resource, TestCommand command)
    {
        // Synthetic targets only: do not open the actual profile, repository or authentication files.
        var syntheticTarget = Path.Combine(Path.GetTempPath(), "gtx-denied-probe-" + Guid.NewGuid().ToString("N"), resource);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new UnavailableIsolatedCommandExecutor().ExecuteAsync(syntheticTarget, command, default));
        Assert.False(Directory.Exists(syntheticTarget));
    }
    [Fact]
    public async Task UnavailableBackendCannotProduceTestOrConfinementEvidence()
    {
        Assert.False((await new UnavailableExecutionIsolation().VerifyAsync(default)).Verified);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new UnavailableIsolatedCommandExecutor().ReadEvidenceAsync(Path.GetTempPath(), [], default));
    }
    [Fact]
    public async Task ConfigurationCannotTurnInventoryIntoIsolationVerification()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../config/harness.json")))
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Isolation:Verified"] = "true", ["Isolation:Backend"] = "HyperV", ["Isolation:NegativeOsTests"] = "Passed"
            }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddGtxHarness(configuration, AppContext.BaseDirectory);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<UnavailableExecutionIsolation>(provider.GetRequiredService<IExecutionIsolation>());
        Assert.False((await provider.GetRequiredService<IExecutionIsolation>().VerifyAsync(default)).Verified);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.GetRequiredService<IIsolatedCommandExecutor>().ExecuteAsync(Path.GetTempPath(), TestCommand.Build, default));
    }
}
