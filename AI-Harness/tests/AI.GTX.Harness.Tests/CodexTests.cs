using System.Text.Json;
using AI.GTX.Harness.Core;
using AI.GTX.Harness.Infrastructure;
using AI.GTX.Harness.Infrastructure.Codex;
using AI.GTX.Harness.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class CodexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gtx-codex-test-" + Guid.NewGuid().ToString("N"));
    internal static DevelopmentPlan ValidPlan() => new() { Title = "Example", Goal = "Plan sample", Assumptions = [], Questions = [], Tasks = [new() { Id = "implement", Role = "developer", Description = "Implement sample", Dependencies = [], AcceptanceCriteria = ["Example works"] }, new() { Id = "test", Role = "tester", Description = "Test sample", Dependencies = ["implement"], AcceptanceCriteria = ["Tests execute"] }] };
    private CodexExecutionProvider Provider(FakeRunner runner, string? executable = null, string? workspace = null) => new(runner,
        Options.Create(new CodexOptions { Executable = executable ?? Environment.ProcessPath!, WorkspaceRoot = workspace ?? root }), new(), NullLogger<CodexExecutionProvider>.Instance);
    [Fact] public async Task AvailabilityRequiresFlagsFeaturesAndChatGptLogin() => Assert.True((await Provider(new()).CheckAvailabilityAsync(default)).Available);
    [Fact] public async Task MissingExecutableIsUnavailable()
    {
        var runner = new FakeRunner(); var result = await Provider(runner, Path.Combine(root, "missing.exe")).CheckAvailabilityAsync(default);
        Assert.Equal(ProviderError.Unavailable, result.Error); Assert.Empty(runner.Calls);
    }
    [Theory] [InlineData("api-key")] [InlineData("not logged in")]
    public async Task ApiKeyOrMissingLoginIsRejected(string auth) => Assert.Equal(ProviderError.Authentication, (await Provider(new() { Auth = auth }).PlanAsync("goal", "default", default)).Error);
    [Fact] public async Task MissingExecutionModeIsUnsupported() => Assert.Equal(ProviderError.Unsupported, (await Provider(new() { Help = "old CLI" }).CheckAvailabilityAsync(default)).Error);
    [Fact] public async Task RelativeWorkspaceIsRejectedBeforeLaunchingChild()
    {
        var runner = new FakeRunner(); var result = await Provider(runner, workspace: "../escape").PlanAsync("goal", "default", default);
        Assert.Equal(ProviderError.WorkspaceDenied, result.Error); Assert.Empty(runner.Calls);
    }
    [Fact] public async Task SafeArgumentsUseStdinAndAnIsolatedWorkspace()
    {
        var runner = new FakeRunner(); const string goal = "Plan a thing; & malicious-command $(escape)";
        var result = await Provider(runner).PlanAsync(goal, "default", default);
        Assert.Equal(ProviderError.None, result.Error); Assert.NotNull(result.Plan);
        var execution = runner.Calls.Last(); Assert.Contains(goal, execution.Input); Assert.DoesNotContain(execution.Arguments, a => a.Contains(goal));
        Assert.Contains("read-only", execution.Arguments); Assert.Contains("never", execution.Arguments); Assert.Contains("shell_tool", execution.Arguments);
        Assert.Contains("--ignore-user-config", execution.Arguments); Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", execution.Arguments);
        Assert.DoesNotContain("--approve-for-me", execution.Arguments); Assert.StartsWith(root + Path.DirectorySeparatorChar, execution.WorkingDirectory);
    }
    [Theory] [InlineData("not json")] [InlineData("{}")] [InlineData("{\"title\":\"incomplete\"}")]
    public async Task MalformedResponseIsRejected(string json) => Assert.Equal(ProviderError.MalformedResponse, (await Provider(new() { PlanJson = json }).PlanAsync("goal", "default", default)).Error);
    [Fact] public async Task ToolEventsArePolicyViolations() => Assert.Equal(ProviderError.PolicyViolation, (await Provider(new() { Events = "{\"item\":{\"type\":\"command_execution\"}}" }).PlanAsync("goal", "default", default)).Error);
    [Theory] [InlineData("{\"type\":\"error\",\"message\":\"fatal\"}")] [InlineData("{\"type\":\"turn.failed\"}")]
    public async Task FatalEventsNeverCountAsCompletion(string events) => Assert.Equal(ProviderError.ExecutionFailed, (await Provider(new() { Events = events }).PlanAsync("goal", "default", default)).Error);
    [Fact] public async Task SuccessfulExitWithoutCompletedTurnIsRejected() => Assert.Equal(ProviderError.MalformedResponse, (await Provider(new() { Events = "{\"type\":\"thread.started\"}" }).PlanAsync("goal", "default", default)).Error);
    [Fact] public void UnknownPlanFieldsAreRejected() => Assert.Throws<JsonException>(() => PlanCodec.Parse(JsonSerializer.Serialize(ValidPlan(), PlanCodec.Json).Replace("\"title\":", "\"extra\":1,\"title\":")));
    [Theory] [InlineData(ProcessFailure.TimedOut, ProviderError.TimedOut)] [InlineData(ProcessFailure.Canceled, ProviderError.Canceled)] [InlineData(ProcessFailure.OutputLimit, ProviderError.OutputLimit)]
    public async Task ProcessFailuresAreTyped(ProcessFailure failure, ProviderError expected) => Assert.Equal(expected, (await Provider(new() { Execution = new(failure, null) }).PlanAsync("goal", "default", default)).Error);
    [Fact] public async Task TransientFailuresUseExistingRetryBudget()
    {
        var provider = new CodexAiModelProvider(new SequenceCodex()); var planner = new PlannerAgent(new ModelProviderResolver([provider]));
        var definition = Fixture.Definition("planner", 2); definition.Provider = "codex-cli";
        var factory = new DynamicAgentFactory([new("planner", () => planner, new HashSet<ToolCapability>())], [definition]);
        var result = await Fixture.Orchestrator(factory).ExecuteAsync(Fixture.Task(new AgentWorkItem("planner", "planner", "goal", [])), default);
        Assert.Equal(ExecutionState.Completed, result.State); Assert.Equal(2, result.Steps.Single().Attempts); Assert.NotNull(result.Steps.Single().Plan);
    }
    [Fact] public void CyclesAreRejected()
    {
        var plan = ValidPlan(); var task = plan.Tasks[0];
        plan.Tasks[0] = new() { Id = task.Id, Role = task.Role, Description = task.Description, Dependencies = ["test"], AcceptanceCriteria = task.AcceptanceCriteria };
        Assert.Throws<InvalidDataException>(() => PlanCodec.Validate(plan));
    }
    [Fact] public void EmptyAcceptanceCriteriaAreRejected() => Assert.Throws<InvalidDataException>(() => PlanCodec.Parse(JsonSerializer.Serialize(ValidPlan(), PlanCodec.Json).Replace("\"Example works\"", "\"\"")));
    [Fact] public void SecretsAreRedacted()
    {
        var result = new SensitiveDataRedactor(() => ["test-credential-value"]).Redact("test-credential-value Bearer abc.def.ghi api_key=hidden sk-abcdefghijk");
        foreach (var secret in new[] { "test-credential-value", "abc.def.ghi", "hidden", "sk-abcdefghijk" }) Assert.DoesNotContain(secret, result);
    }
    [Fact] public void JsonCredentialFieldsAreRedactedWithoutBreakingJson()
    {
        var text = new SensitiveDataRedactor().Redact("{\"api_key\":\"hidden-value\"}");
        using var document = JsonDocument.Parse(text); Assert.Equal("[REDACTED]", document.RootElement.GetProperty("api_key").GetString());
    }
    [Fact] public async Task PlannerOnlyQueueUsesExistingTaskLifecycle()
    {
        var agent = Fixture.Success("planner");
        var manager = new AI.GTX.Harness.Orchestrator.TaskManager(Fixture.Orchestrator(Fixture.Factory([agent])), NullLogger<AI.GTX.Harness.Orchestrator.TaskManager>.Instance);
        var queued = manager.EnqueuePlan("goal"); Assert.Single(queued.Steps); Assert.Equal(ExecutionState.Pending, queued.State);
        var result = await manager.RunNextAsync(default); Assert.Equal(ExecutionState.Completed, result!.State); Assert.Equal(result, manager.Get(queued.Id));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    internal sealed class FakeRunner : IControlledProcessRunner
    {
        public List<ProcessRequest> Calls { get; } = [];
        public string Auth = "Logged in using ChatGPT", Help = "--output-schema --json --sandbox read-only --ignore-user-config --ignore-rules --ephemeral --output-last-message --skip-git-repo-check";
        public string PlanJson = JsonSerializer.Serialize(ValidPlan(), PlanCodec.Json), Events = "{\"type\":\"item.completed\",\"item\":{\"type\":\"error\",\"message\":\"nonfatal warning\"}}\n{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\"}}\n{\"type\":\"turn.completed\"}";
        public ProcessResult? Execution;
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
        {
            Calls.Add(request);
            if (request.Arguments.Contains("--version")) return new(ProcessFailure.None, 0, "codex-cli test");
            if (request.Arguments.Contains("--help")) return new(ProcessFailure.None, 0, Help);
            if (request.Arguments.Contains("features")) return new(ProcessFailure.None, 0, string.Join('\n', new[] { "shell_tool", "unified_exec", "hooks", "apps", "plugins", "computer_use", "browser_use", "image_generation", "multi_agent_v2", "code_mode_host", "code_mode_host_grpc", "unbounded_connection_retries" }.Select(f => f + " stable true")));
            if (request.Arguments.Contains("login")) return new(ProcessFailure.None, Auth == "not logged in" ? 1 : 0, Auth);
            if (Execution != null) return Execution;
            var index = request.Arguments.ToList().IndexOf("--output-last-message"); await File.WriteAllTextAsync(request.Arguments[index + 1], PlanJson, ct);
            return new(ProcessFailure.None, 0, Events);
        }
    }
    private sealed class SequenceCodex : ICodexExecutionProvider
    {
        private int calls;
        public Task<CodexAvailability> CheckAvailabilityAsync(CancellationToken ct) => Task.FromResult(new CodexAvailability(true, ProviderError.None, "test"));
        public Task<CodexExecutionResult> PlanAsync(string goal, string model, CancellationToken ct) => Task.FromResult(++calls == 1 ? new CodexExecutionResult(ProviderError.Transient) : new(ProviderError.None, ValidPlan()));
    }
}
