namespace AI.GTX.Harness.Core;

// String role IDs allow future roles without changing an enum or the orchestrator.
public static class AgentRoles
{
    public const string Planner = "planner", Architect = "architect", Developer = "developer",
        Tester = "tester", Reviewer = "reviewer", Documentation = "documentation";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Planner, Architect, Developer, Tester, Reviewer, Documentation });
}
public enum ExecutionState { Pending, Running, Completed, Failed, Blocked, Canceled, NotConfigured }
public enum ToolCapability { ReadFile, ListFiles, StageChange, DotnetBuild, DotnetTest, GitStatus, GitDiff, Delete, GitMerge, ProductionDeploy }
public enum PolicyDecision { Allowed, RequiresApproval, Denied }
public sealed record AgentWorkItem(string Id, string Role, string Description, IReadOnlyList<string> Dependencies);
public sealed record AgentExecutionResult(ExecutionState State, string Summary, DevelopmentPlan? Plan = null, ProviderError Error = ProviderError.None);
public sealed record WorkItemResult(string Id, ExecutionState State, int Attempts, string Summary, DevelopmentPlan? Plan = null, ProviderError Error = ProviderError.None);
public sealed record WorkflowResult(Guid TaskId, ExecutionState State, IReadOnlyList<WorkItemResult> Steps);
public sealed record DevelopmentTask(Guid Id, string Description, IReadOnlyList<AgentWorkItem> Steps, ExecutionState State, WorkflowResult? Result = null);
public sealed record ModelRequest(string Role, string Model, string Instructions, string Task, IReadOnlyList<WorkItemResult> PriorResults);
public sealed record ModelResponse(bool Configured, string Text, DevelopmentPlan? Plan = null, ProviderError Error = ProviderError.None);
public sealed record ToolRequest(ToolCapability Capability, string RelativePath = "");
public sealed record ToolResponse(bool Executed, string Message);
public sealed record AgentRegistration(string Role, Func<IAgent> Create, IReadOnlySet<ToolCapability> MaximumPermissions);
public sealed record AgentInstance(IAgent Agent, AgentDefinition Definition);

public sealed class AgentDefinition
{
    public string Role { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "placeholder";
    public string Model { get; set; } = "unconfigured";
    public int MaxAttempts { get; set; } = 1;
    public ToolCapability[] Permissions { get; set; } = [];
    public AgentDefinition Snapshot() => new() { Role = Role, Enabled = Enabled, Provider = Provider, Model = Model, MaxAttempts = MaxAttempts, Permissions = [.. Permissions] };
}
public sealed class HarnessOptions
{
    public string Workspace { get; set; } = "workspace/milestone1";
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryDelayMilliseconds { get; set; } = 100;
    public List<AgentDefinition> Agents { get; set; } = [];
}
// Only explicit provider adapters can classify a failure as retryable. Tool changes are not replayed.
public sealed class RetryableAgentException() : Exception("Transient agent failure.");
