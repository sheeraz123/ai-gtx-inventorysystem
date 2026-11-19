using AI.GTX.Harness.Core;
using AI.GTX.Harness.Orchestrator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class TaskManagerTests
{
    [Fact]
    public async Task TracksPendingRunningAndCompletedStates()
    {
        TaskManager? manager = null;
        var agents = AgentRoles.All.Select(role => new ScriptedAgent(role, (_, _) => {
            Assert.Equal(ExecutionState.Running, manager!.Get(currentId)!.State);
            return Task.FromResult(new AgentExecutionResult(ExecutionState.Completed, "test double"));
        })).ToArray();
        manager = new(Fixture.Orchestrator(Fixture.Factory(agents)), NullLogger<TaskManager>.Instance);
        var queued = manager.Enqueue("feature"); currentId = queued.Id;
        Assert.Equal(ExecutionState.Pending, manager.Get(queued.Id)!.State);
        var completed = await manager.RunNextAsync(default);
        Assert.Equal(ExecutionState.Completed, completed!.State);
        Assert.Equal(6, completed.Result!.Steps.Count);
        Assert.Equal(completed, manager.Get(queued.Id));
        Assert.Null(await manager.RunNextAsync(default));
    }
    private Guid currentId;
    [Fact]
    public void RejectsEmptyTasks()
    {
        var manager = new TaskManager(Fixture.Orchestrator(Fixture.Factory([])), NullLogger<TaskManager>.Instance);
        Assert.Throws<ArgumentException>(() => manager.Enqueue(" "));
        Assert.Null(manager.Get(Guid.NewGuid()));
    }
    [Fact]
    public async Task CanceledDequeuePreservesPendingTask()
    {
        var manager = new TaskManager(Fixture.Orchestrator(Fixture.Factory([])), NullLogger<TaskManager>.Instance);
        var task = manager.Enqueue("feature"); using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.RunNextAsync(ct.Token));
        Assert.Equal(ExecutionState.Pending, manager.Get(task.Id)!.State);
    }
}
