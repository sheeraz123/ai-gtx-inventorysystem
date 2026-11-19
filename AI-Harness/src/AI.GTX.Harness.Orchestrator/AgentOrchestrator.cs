using AI.GTX.Harness.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.GTX.Harness.Orchestrator;

public sealed class AgentOrchestrator(IAgentFactory factory, IOptions<HarnessOptions> options,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    public async Task<WorkflowResult> ExecuteAsync(DevelopmentTask task, CancellationToken cancellationToken)
    {
        ValidateGraph(task.Steps);
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TaskId"] = task.Id });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        var results = task.Steps.ToDictionary(s => s.Id, s => new WorkItemResult(s.Id, ExecutionState.Pending, 0, "Pending."));
        while (results.Values.Any(r => r.State == ExecutionState.Pending))
        {
            foreach (var step in task.Steps.Where(s => results[s.Id].State == ExecutionState.Pending))
            {
                if (step.Dependencies.Any(d => results[d].State is ExecutionState.Pending or ExecutionState.Running)) continue;
                if (step.Dependencies.Any(d => results[d].State != ExecutionState.Completed))
                { results[step.Id] = new(step.Id, ExecutionState.Blocked, 0, "A dependency did not complete."); continue; }
                if (timeout.IsCancellationRequested)
                { results[step.Id] = new(step.Id, ExecutionState.Canceled, 0, "Canceled before execution."); continue; }
                var attempts = 0;
                try
                {
                    var instance = factory.Create(step.Role);
                    while (true)
                    {
                        attempts++;
                        results[step.Id] = new(step.Id, ExecutionState.Running, attempts, "Running.");
                        logger.LogInformation("Agent {AgentRole} attempt {Attempt} starting", step.Role, attempts);
                        try
                        {
                            var result = await instance.Agent.ExecuteAsync(step, instance.Definition,
                                results.Values.Where(r => r.State == ExecutionState.Completed).ToArray(), timeout.Token);
                            if (result.State is ExecutionState.Pending or ExecutionState.Running or ExecutionState.Canceled)
                                throw new InvalidOperationException("Agent returned an invalid terminal state.");
                            // Agent prose stays in process memory, never in structured log fields.
                            results[step.Id] = new(step.Id, result.State, attempts, result.Summary, result.Plan, result.Error);
                            break;
                        }
                        catch (RetryableAgentException) when (attempts < instance.Definition.MaxAttempts)
                        {
                            logger.LogWarning("Agent {AgentRole} transient failure; retry scheduled", step.Role);
                            await Task.Delay(options.Value.RetryDelayMilliseconds, timeout.Token);
                        }
                    }
                }
                catch (RetryableAgentException) { results[step.Id] = new(step.Id, ExecutionState.Failed, attempts, "Transient provider retry budget exhausted.", Error: ProviderError.Transient); }
                catch (OperationCanceledException) { results[step.Id] = new(step.Id, ExecutionState.Canceled, attempts, "Canceled or timed out.", Error: ProviderError.Canceled); }
                catch (UnauthorizedAccessException) { results[step.Id] = new(step.Id, ExecutionState.Blocked, attempts, "Agent disabled or operation denied."); }
                catch (Exception ex)
                {
                    logger.LogError("Agent {AgentRole} failed with {ErrorType}", step.Role, ex.GetType().Name);
                    results[step.Id] = new(step.Id, ExecutionState.Failed, attempts, "Agent execution failed.");
                }
                logger.LogInformation("Agent {AgentRole} finished with {ExecutionState}", step.Role, results[step.Id].State);
            }
        }
        var states = results.Values.Select(r => r.State).ToArray();
        var state = states.All(s => s == ExecutionState.Completed) ? ExecutionState.Completed
            : states.Contains(ExecutionState.Canceled) ? ExecutionState.Canceled
            : states.Contains(ExecutionState.Failed) ? ExecutionState.Failed
            : states.Contains(ExecutionState.NotConfigured) ? ExecutionState.NotConfigured : ExecutionState.Blocked;
        return new(task.Id, state, task.Steps.Select(s => results[s.Id]).ToArray());
    }
    private static void ValidateGraph(IReadOnlyList<AgentWorkItem> steps)
    {
        if (steps.Count == 0 || steps.Count > 100) throw new ArgumentException("Workflow needs 1 to 100 steps.");
        var ids = steps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != steps.Count || steps.Any(s => string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Role) || s.Dependencies.Any(d => !ids.Contains(d))))
            throw new ArgumentException("Workflow has duplicate IDs or invalid dependencies.");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Count < steps.Count)
        {
            var ready = steps.Where(s => !visited.Contains(s.Id) && s.Dependencies.All(visited.Contains)).ToArray();
            if (ready.Length == 0) throw new ArgumentException("Workflow dependency cycle detected.");
            foreach (var step in ready) visited.Add(step.Id);
        }
    }
}
