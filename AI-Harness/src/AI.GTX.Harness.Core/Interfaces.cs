namespace AI.GTX.Harness.Core;

public interface IAgent
{
    string Role { get; }
    Task<AgentExecutionResult> ExecuteAsync(AgentWorkItem task, AgentDefinition definition,
        IReadOnlyList<WorkItemResult> priorResults, CancellationToken cancellationToken);
}
public interface IAgentFactory { AgentInstance Create(string role); }
public interface IAiModelProvider
{
    string Name { get; }
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}
public interface IModelProviderResolver { IAiModelProvider Resolve(string name); }
public interface IAgentOrchestrator { Task<WorkflowResult> ExecuteAsync(DevelopmentTask task, CancellationToken cancellationToken); }
public interface ITaskManager
{
    DevelopmentTask Enqueue(string description);
    DevelopmentTask EnqueuePlan(string description);
    DevelopmentTask? Get(Guid id);
    Task<DevelopmentTask?> RunNextAsync(CancellationToken cancellationToken);
}
public interface IToolPolicy { PolicyDecision Evaluate(AgentDefinition agent, ToolCapability capability); }
public interface IApprovalService { Task<bool> ApproveAsync(string operation, CancellationToken cancellationToken); }
public interface IToolGateway { Task<ToolResponse> ExecuteAsync(AgentDefinition agent, ToolRequest request, CancellationToken cancellationToken); }
