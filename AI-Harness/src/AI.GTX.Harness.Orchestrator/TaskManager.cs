using System.Collections.Concurrent;
using System.Threading.Channels;
using AI.GTX.Harness.Core;
using Microsoft.Extensions.Logging;

namespace AI.GTX.Harness.Orchestrator;

// Serialized, process-local execution. Durable storage and cross-process leases are deferred.
public sealed class TaskManager(IAgentOrchestrator orchestrator, ILogger<TaskManager> logger) : ITaskManager
{
    private readonly ConcurrentDictionary<Guid, DevelopmentTask> tasks = new();
    private readonly Channel<Guid> queue = Channel.CreateUnbounded<Guid>();
    private readonly SemaphoreSlim execution = new(1, 1);
    public DevelopmentTask Enqueue(string description) => Enqueue(description, AgentRoles.All);
    public DevelopmentTask EnqueuePlan(string description) => Enqueue(description, [AgentRoles.Planner]);
    private DevelopmentTask Enqueue(string description, IReadOnlyList<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (description.Length > 50_000) throw new ArgumentException("Task description exceeds the limit.");
        var steps = roles.Select((role, index) => new AgentWorkItem(role, role, description,
            Array.AsReadOnly(index == 0 ? Array.Empty<string>() : new[] { roles[index - 1] }))).ToArray();
        var task = new DevelopmentTask(Guid.NewGuid(), description, Array.AsReadOnly(steps), ExecutionState.Pending);
        tasks[task.Id] = task;
        if (!queue.Writer.TryWrite(task.Id)) throw new InvalidOperationException("Queue is unavailable.");
        logger.LogInformation("Queued task {TaskId}", task.Id);
        return task;
    }
    public DevelopmentTask? Get(Guid id) => tasks.GetValueOrDefault(id);
    public async Task<DevelopmentTask?> RunNextAsync(CancellationToken cancellationToken)
    {
        await execution.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!queue.Reader.TryRead(out var id)) return null;
            var task = tasks[id] with { State = ExecutionState.Running };
            tasks[id] = task;
            WorkflowResult result;
            try { result = await orchestrator.ExecuteAsync(task, cancellationToken); }
            catch (OperationCanceledException) { result = new(id, ExecutionState.Canceled, []); }
            catch (Exception ex)
            {
                logger.LogError("Task {TaskId} failed with {ErrorType}", id, ex.GetType().Name);
                result = new(id, ExecutionState.Failed, []);
            }
            task = task with { State = result.State, Result = result };
            tasks[id] = task;
            logger.LogInformation("Task {TaskId} finished with {ExecutionState}", id, task.State);
            return task;
        }
        finally { execution.Release(); }
    }
}
