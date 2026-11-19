using AI.GTX.Harness.Agents;
using AI.GTX.Harness.Core;
using AI.GTX.Harness.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class IntegrationTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddJsonFile(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../config/harness.json"))).Build();
    private static ServiceProvider Services(IConfiguration configuration)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddGtxHarness(configuration, AppContext.BaseDirectory);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    [Fact]
    public async Task RealDiGraphRunsPlaceholderWithoutCredentialsAndBlocksDependents()
    {
        using var services = Services(Configuration());
        var manager = services.GetRequiredService<ITaskManager>(); var queued = manager.Enqueue("test foundation");
        var result = await manager.RunNextAsync(default);
        Assert.Equal(ExecutionState.NotConfigured, result!.State);
        Assert.Equal(ExecutionState.NotConfigured, result.Result!.Steps[0].State);
        Assert.All(result.Result.Steps.Skip(1), step => Assert.Equal(ExecutionState.Blocked, step.State));
        Assert.Equal(result, manager.Get(queued.Id));
        Assert.IsType<AI.GTX.Harness.Tools.FoundationToolGateway>(services.GetRequiredService<IToolGateway>());
    }
    [Fact]
    public async Task EverySpecializedAgentReportsPlaceholderHonestly()
    {
        using var services = Services(Configuration()); var factory = services.GetRequiredService<IAgentFactory>();
        foreach (var role in AgentRoles.All)
        {
            var instance = factory.Create(role);
            var result = await instance.Agent.ExecuteAsync(new(role, role, "test", []), instance.Definition, [], default);
            Assert.Equal(ExecutionState.NotConfigured, result.State);
        }
    }
    [Fact]
    public async Task GeneratedTextAloneCannotMarkEngineeringWorkComplete()
    {
        var provider = new RecordingProvider(); var agent = new DeveloperAgent(new ModelProviderResolver([provider]));
        var definition = Fixture.Definition("developer"); definition.Model = "configured-model"; definition.Provider = provider.Name;
        var result = await agent.ExecuteAsync(new("d", "developer", "implement feature", []), definition, [], default);
        Assert.Equal(ExecutionState.Blocked, result.State);
        Assert.Equal("configured-model", provider.Request!.Model);
        Assert.Equal("developer", provider.Request.Role);
    }
    [Fact]
    public void InvalidConfigurationFailsValidation()
    {
        var configuration = new ConfigurationBuilder().AddConfiguration(Configuration()).AddInMemoryCollection(new Dictionary<string, string?> {
            ["Harness:TimeoutSeconds"] = "0"
        }).Build();
        using var services = Services(configuration);
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<HarnessOptions>>().Value);
    }
    [Fact]
    public void UnknownProviderDoesNotFallBackSilently()
    {
        var resolver = new ModelProviderResolver([new PlaceholderAiProvider()]);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("unknown-provider"));
    }
    private sealed class RecordingProvider : IAiModelProvider
    {
        public string Name => "recording"; public ModelRequest? Request;
        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
        { Request = request; return Task.FromResult(new ModelResponse(true, "generated text, not executed code")); }
    }
}
