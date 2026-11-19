using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Agents;

public abstract class ModelBackedAgent(IModelProviderResolver providers) : IAgent
{
    public abstract string Role { get; }
    protected abstract string Instructions { get; }
    public async Task<AgentExecutionResult> ExecuteAsync(AgentWorkItem task, AgentDefinition definition,
        IReadOnlyList<WorkItemResult> priorResults, CancellationToken cancellationToken)
    {
        var response = await providers.Resolve(definition.Provider).GenerateAsync(
            new(Role, definition.Model, Instructions, task.Description, priorResults), cancellationToken);
        return Interpret(response);
    }
    protected virtual AgentExecutionResult Interpret(ModelResponse response)
    {
        // Non-planning roles still have no tool-calling loop.
        return response.Configured
            ? new(ExecutionState.Blocked, "Model responded, but task execution and verification are not implemented in Milestone 1.")
            : new(ExecutionState.NotConfigured, "Placeholder provider: no AI task was executed.");
    }
}
public sealed class PlannerAgent(IModelProviderResolver providers) : ModelBackedAgent(providers)
{
    public override string Role => AgentRoles.Planner;
    protected override string Instructions => "Analyze requirements, identify uncertainties and break work into verifiable tasks. Do not invent business rules.";
    protected override AgentExecutionResult Interpret(ModelResponse response)
    {
        if (response.Error != ProviderError.None)
            return new(response.Error is ProviderError.Unavailable or ProviderError.Authentication or ProviderError.Unsupported ? ExecutionState.NotConfigured : ExecutionState.Failed,
                "Planner provider did not complete.", Error: response.Error);
        if (!response.Configured || response.Plan == null) return base.Interpret(response);
        try { PlanCodec.Validate(response.Plan); }
        catch (InvalidDataException) { return new(ExecutionState.Failed, "Invalid structured plan.", Error: ProviderError.MalformedResponse); }
        return new(ExecutionState.Completed, "Structured development plan prepared for review; no source changes applied.", response.Plan);
    }
}
public sealed class ArchitectAgent(IModelProviderResolver providers) : ModelBackedAgent(providers)
{
    public override string Role => AgentRoles.Architect;
    protected override string Instructions => "Design module boundaries, database abstraction and API contracts. Preserve multi-company isolation; record decisions and open questions.";
}
public sealed class DeveloperAgent(IModelProviderResolver providers) : ModelBackedAgent(providers)
{
    public override string Role => AgentRoles.Developer;
    protected override string Instructions => "Prepare code changes against the approved design. Use controlled change proposals; never claim unwritten code exists.";
}
public sealed class TesterAgent(IModelProviderResolver providers) : ModelBackedAgent(providers)
{
    public override string Role => AgentRoles.Tester;
    protected override string Instructions => "Design meaningful unit and integration tests. Report test success only with observed test execution evidence.";
}
public sealed class ReviewerAgent(IModelProviderResolver providers) : ModelBackedAgent(providers)
{
    public override string Role => AgentRoles.Reviewer;
    protected override string Instructions => "Review correctness, security, company isolation and tests. Report actionable findings; do not modify source files.";
}
public sealed class DocumentationAgent(IModelProviderResolver providers) : ModelBackedAgent(providers)
{
    public override string Role => AgentRoles.Documentation;
    protected override string Instructions => "Prepare usage and architecture documentation based on verified changes. Record limitations and unanswered requirements.";
}
