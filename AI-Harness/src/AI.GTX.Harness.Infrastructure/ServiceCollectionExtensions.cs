using AI.GTX.Harness.Agents;
using AI.GTX.Harness.Core;
using AI.GTX.Harness.Orchestrator;
using AI.GTX.Harness.Tools;
using AI.GTX.Harness.Infrastructure.Codex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AI.GTX.Harness.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGtxHarness(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddOptions<HarnessOptions>().Bind(configuration.GetSection("Harness"))
            .Validate(o => o.TimeoutSeconds is > 0 and <= 3600, "TimeoutSeconds must be between 1 and 3600.")
            .Validate(o => o.RetryDelayMilliseconds is >= 0 and <= 10_000, "RetryDelayMilliseconds must be between 0 and 10000.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Workspace), "Workspace is required.")
            .Validate(o => o.Agents.Count > 0, "Configure at least one approved agent.")
            .ValidateOnStart();
        services.Configure<ProviderOptions>(configuration.GetSection("Provider"));
        services.AddOptions<CodexOptions>().Bind(configuration.GetSection("Codex"))
            .Validate(o => o.TimeoutSeconds is > 0 and <= 600 && Path.IsPathFullyQualified(o.WorkspaceRoot), "Codex requires an absolute approved workspace root and timeout of 1–600 seconds.");
        services.AddSingleton<SensitiveDataRedactor>();
        services.AddSingleton<IControlledProcessRunner, ControlledProcessRunner>();
        services.AddSingleton<IExecutionIsolation, UnavailableExecutionIsolation>();
        services.AddSingleton<IIsolatedCommandExecutor, UnavailableIsolatedCommandExecutor>();
        services.AddSingleton<CodexExecutionProvider>();
        services.AddSingleton<ICodexExecutionProvider>(sp => sp.GetRequiredService<CodexExecutionProvider>());
        services.AddSingleton<IStructuredCodexProvider>(sp => sp.GetRequiredService<CodexExecutionProvider>());
        var developmentRoot = Path.Combine(contentRoot, "workspace", "milestone3");
        services.AddSingleton<IWorkflowStore>(new WorkflowStore(Path.Combine(developmentRoot, "state")));
        services.AddSingleton<IDevelopmentWorkspace>(new DevelopmentWorkspace(Path.Combine(developmentRoot, "proposals")));
        services.AddSingleton<IAtomicFileCommitter, AtomicFileCommitter>();
        services.AddSingleton<IApprovedBatchApplier, ApprovedBatchApplier>();
        services.AddSingleton<IDeveloperExecutionAgent, CodexDeveloperExecutionAgent>();
        services.AddSingleton<IControlledTesterAgent, ControlledTesterAgent>();
        services.AddSingleton<IStructuredReviewerAgent, CodexStructuredReviewerAgent>();
        services.AddSingleton<DevelopmentWorkflowController>();
        services.AddSingleton(sp => new DevelopmentDemo(sp.GetRequiredService<ICodexExecutionProvider>(), sp.GetRequiredService<IWorkflowStore>(), Path.Combine(developmentRoot, "targets")));
        services.AddSingleton<IAiModelProvider, CodexAiModelProvider>();
        services.AddSingleton<IAiModelProvider, PlaceholderAiProvider>();
        services.AddSingleton<IModelProviderResolver, ModelProviderResolver>();
        RegisterAgent<PlannerAgent>(services, AgentRoles.Planner, ToolCapability.ReadFile, ToolCapability.ListFiles);
        RegisterAgent<ArchitectAgent>(services, AgentRoles.Architect, ToolCapability.ReadFile, ToolCapability.ListFiles);
        RegisterAgent<DeveloperAgent>(services, AgentRoles.Developer, ToolCapability.ReadFile, ToolCapability.ListFiles, ToolCapability.StageChange, ToolCapability.DotnetBuild);
        RegisterAgent<TesterAgent>(services, AgentRoles.Tester, ToolCapability.ReadFile, ToolCapability.ListFiles, ToolCapability.StageChange, ToolCapability.DotnetTest);
        RegisterAgent<ReviewerAgent>(services, AgentRoles.Reviewer, ToolCapability.ReadFile, ToolCapability.ListFiles, ToolCapability.GitStatus, ToolCapability.GitDiff);
        RegisterAgent<DocumentationAgent>(services, AgentRoles.Documentation, ToolCapability.ReadFile, ToolCapability.ListFiles, ToolCapability.StageChange);
        services.AddSingleton<IAgentFactory>(sp =>
        {
            var definitions = sp.GetRequiredService<IOptions<HarnessOptions>>().Value.Agents;
            var resolver = sp.GetRequiredService<IModelProviderResolver>();
            foreach (var definition in definitions) resolver.Resolve(definition.Provider);
            return new DynamicAgentFactory(sp.GetServices<AgentRegistration>(), definitions);
        });
        services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
        services.AddSingleton<ITaskManager, TaskManager>();
        services.AddSingleton<IToolPolicy, ToolPolicy>();
        services.AddSingleton<IApprovalService, DenyApprovalService>();
        services.AddSingleton(sp => new WorkspaceReader(Path.GetFullPath(sp.GetRequiredService<IOptions<HarnessOptions>>().Value.Workspace, contentRoot)));
        services.AddSingleton<IToolGateway, FoundationToolGateway>();
        return services;
    }
    private static void RegisterAgent<T>(IServiceCollection services, string role, params ToolCapability[] maximumPermissions) where T : class, IAgent
    {
        services.AddTransient<T>();
        services.AddSingleton(sp => new AgentRegistration(role, () => sp.GetRequiredService<T>(), maximumPermissions.ToHashSet()));
    }
}
