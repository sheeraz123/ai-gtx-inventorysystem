using AI.GTX.Harness.Core;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class OrchestratorTests
{
    [Fact]
    public async Task ResolvesDependenciesEvenWhenStepsAreUnordered()
    {
        var order = new List<string>();
        var agents = new[] { "a", "b" }.Select(role => new ScriptedAgent(role, (_, _) => {
            order.Add(role); return Task.FromResult(new AgentExecutionResult(ExecutionState.Completed, "done"));
        })).ToArray();
        var result = await Fixture.Orchestrator(Fixture.Factory(agents)).ExecuteAsync(Fixture.Task(
            new("second", "b", "test", ["first"]), new("first", "a", "test", [])), default);
        Assert.Equal(ExecutionState.Completed, result.State);
        Assert.Equal(new[] { "a", "b" }, order);
        Assert.All(result.Steps, s => Assert.Equal(1, s.Attempts));
    }
    [Fact]
    public async Task RejectsDependencyCyclesBeforeExecutingAgents()
    {
        var agent = Fixture.Success("a");
        await Assert.ThrowsAsync<ArgumentException>(() => Fixture.Orchestrator(Fixture.Factory([agent])).ExecuteAsync(
            Fixture.Task(new AgentWorkItem("a", "a", "test", ["b"]), new("b", "a", "test", ["a"])), default));
        Assert.Equal(0, agent.Calls);
    }
    [Fact]
    public async Task RejectsMissingDependencies()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Fixture.Orchestrator(Fixture.Factory([Fixture.Success("a")])).ExecuteAsync(
            Fixture.Task(new AgentWorkItem("a", "a", "test", ["missing"])), default));
    }
    [Fact]
    public async Task TransientFailuresRetryWithinConfiguredBudget()
    {
        var agent = new ScriptedAgent("a", (attempt, _) => attempt == 1 ? throw new RetryableAgentException() : Task.FromResult(new AgentExecutionResult(ExecutionState.Completed, "done")));
        var result = await Fixture.Orchestrator(Fixture.Factory([agent], 2)).ExecuteAsync(Fixture.Task(new AgentWorkItem("a", "a", "test", [])), default);
        Assert.Equal(ExecutionState.Completed, result.State);
        Assert.Equal(2, result.Steps.Single().Attempts);
    }
    [Fact]
    public async Task ExhaustedRetriesFailAndBlockDependents()
    {
        var failing = new ScriptedAgent("a", (_, _) => throw new RetryableAgentException()); var next = Fixture.Success("b");
        var result = await Fixture.Orchestrator(Fixture.Factory([failing, next], 2)).ExecuteAsync(
            Fixture.Task(new AgentWorkItem("a", "a", "test", []), new("b", "b", "test", ["a"])), default);
        Assert.Equal(ExecutionState.Failed, result.State);
        Assert.Equal(2, failing.Calls); Assert.Equal(0, next.Calls);
        Assert.Equal(ExecutionState.Blocked, result.Steps[1].State);
    }
    [Fact]
    public async Task UnexpectedFailuresAreNotRetriedOrExposed()
    {
        var agent = new ScriptedAgent("a", (_, _) => throw new IOException("SECRET_VALUE_DO_NOT_LOG"));
        var result = await Fixture.Orchestrator(Fixture.Factory([agent], 3)).ExecuteAsync(Fixture.Task(new AgentWorkItem("a", "a", "test", [])), default);
        Assert.Equal(1, agent.Calls);
        Assert.Equal(ExecutionState.Failed, result.State);
        Assert.DoesNotContain("SECRET_VALUE", result.Steps.Single().Summary);
    }
    [Fact]
    public async Task CancellationPreventsExecution()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel(); var agent = Fixture.Success("a");
        var result = await Fixture.Orchestrator(Fixture.Factory([agent])).ExecuteAsync(Fixture.Task(new AgentWorkItem("a", "a", "test", [])), ct.Token);
        Assert.Equal(ExecutionState.Canceled, result.State); Assert.Equal(0, agent.Calls);
    }
    [Fact]
    public async Task DisabledAgentBlocksItsDependents()
    {
        var factory = Fixture.Factory([]);
        var result = await Fixture.Orchestrator(factory).ExecuteAsync(Fixture.Task(new AgentWorkItem("a", "a", "test", []), new("b", "b", "test", ["a"])), default);
        Assert.Equal(ExecutionState.Blocked, result.State);
    }
    [Fact]
    public async Task NonterminalAgentResultIsRejected()
    {
        var agent = new ScriptedAgent("a", (_, _) => Task.FromResult(new AgentExecutionResult(ExecutionState.Running, "invalid")));
        var result = await Fixture.Orchestrator(Fixture.Factory([agent])).ExecuteAsync(Fixture.Task(new AgentWorkItem("a", "a", "test", [])), default);
        Assert.Equal(ExecutionState.Failed, result.State);
    }
}
