using AI.GTX.Harness.Agents;
using AI.GTX.Harness.Core;
using AI.GTX.Harness.Orchestrator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.GTX.Harness.Tests;

internal sealed class ScriptedAgent(string role, Func<int, CancellationToken, Task<AgentExecutionResult>> action) : IAgent
{
    public string Role => role;
    public int Calls { get; private set; }
    public Task<AgentExecutionResult> ExecuteAsync(AgentWorkItem task, AgentDefinition definition, IReadOnlyList<WorkItemResult> priorResults, CancellationToken ct) => action(++Calls, ct);
}
internal static class Fixture
{
    public static AgentDefinition Definition(string role = "planner", int attempts = 1, params ToolCapability[] permissions) => new() { Role = role, MaxAttempts = attempts, Permissions = permissions };
    public static DynamicAgentFactory Factory(IEnumerable<IAgent> agents, int attempts = 1) => new(
        agents.Select(a => new AgentRegistration(a.Role, () => a, new HashSet<ToolCapability>())),
        agents.Select(a => Definition(a.Role, attempts)));
    public static AgentOrchestrator Orchestrator(IAgentFactory factory) => new(factory,
        Options.Create(new HarnessOptions { RetryDelayMilliseconds = 0, TimeoutSeconds = 5 }), NullLogger<AgentOrchestrator>.Instance);
    public static DevelopmentTask Task(params AgentWorkItem[] steps) => new(Guid.NewGuid(), "test task", steps, ExecutionState.Pending);
    public static ScriptedAgent Success(string role) => new(role, (_, _) => System.Threading.Tasks.Task.FromResult(new AgentExecutionResult(ExecutionState.Completed, "test double completed")));
}
