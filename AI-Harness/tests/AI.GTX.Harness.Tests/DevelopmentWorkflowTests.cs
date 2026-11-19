using System.Text.Json;
using AI.GTX.Harness.Core;
using AI.GTX.Harness.Agents;
using AI.GTX.Harness.Infrastructure;
using AI.GTX.Harness.Orchestrator;
using AI.GTX.Harness.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class DevelopmentWorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gtx-m3-test-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(root, "target");
    private string State => Path.Combine(root, "state");
    private readonly FakeGeneration generation = new();
    private readonly FakeCommands commands = new();
    private readonly FakeIsolation isolation = new();
    private readonly IWorkflowStore store;
    private readonly IDevelopmentWorkspace workspace;
    public DevelopmentWorkflowTests()
    {
        Directory.CreateDirectory(Target); File.WriteAllText(Path.Combine(Target, "a.cs"), "original-a"); File.WriteAllText(Path.Combine(Target, "b.cs"), "original-b");
        store = new WorkflowStore(State); workspace = new DevelopmentWorkspace(Path.Combine(root, "staging"));
    }
    private static DevelopmentPlan Plan() => new() {
        Title = "Disposable test plan", Goal = "Change sample only", Assumptions = [], Questions = [], Tasks = [
            new() { Id = "D1", Role = "developer", Description = "Implement sample", Dependencies = [], AcceptanceCriteria = ["Sample behaves as requested"] },
            new() { Id = "T1", Role = "tester", Description = "Verify sample", Dependencies = ["D1"], AcceptanceCriteria = ["Tests pass"] },
            new() { Id = "R1", Role = "reviewer", Description = "Review sample", Dependencies = ["T1"], AcceptanceCriteria = ["No blocking findings"] } ]
    };
    private DevelopmentPlanPackage Package() => store.Create(Plan(), Target, ["a.cs", "b.cs"]);
    private DevelopmentWorkflowController Controller(IAtomicFileCommitter? committer = null) => new(store, workspace, isolation,
        new CodexDeveloperExecutionAgent(generation, store), new ControlledTesterAgent(isolation, commands), new CodexStructuredReviewerAgent(generation),
        new ApprovedBatchApplier(store, workspace, committer ?? new AtomicFileCommitter()), NullLogger<DevelopmentWorkflowController>.Instance);
    private async Task<DevelopmentPlanPackage> ApproveAsync()
    {
        var plan = Package(); await Controller().DecidePlanAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default); return plan;
    }
    [Fact] public async Task PipelinePausesForChangeApprovalAndDoesNotApply()
    {
        var plan = await ApproveAsync(); var result = await Controller().ExecuteAsync(plan.Id, 1, default);
        Assert.Equal(DevelopmentState.AwaitingChangeApproval, result.State); Assert.NotNull(result.Proposal); Assert.True(result.Tests!.Passed);
        Assert.Equal("original-a", File.ReadAllText(Path.Combine(Target, "a.cs"))); Assert.Null(store.ReadApproval(plan, ApprovalSubject.Changes));
        Assert.All(result.Tasks.Values, state => Assert.Equal(ExecutionState.Completed, state));
    }
    [Fact] public async Task UnapprovedPlanCannotStartDeveloper()
    {
        var plan = Package(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Controller().ExecuteAsync(plan.Id, 1, default)); Assert.Equal(0, generation.DeveloperCalls);
    }
    [Fact] public async Task RejectedPlanCannotExecute()
    {
        var plan = Package(); await Controller().DecidePlanAsync(plan.Id, 1, new Decision(false), "TEST-DOUBLE", default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Controller().ExecuteAsync(plan.Id, 1, default));
        Assert.Equal(DevelopmentState.PlanRejected, store.ReadProgress(plan).State);
    }
    [Fact] public async Task PersistedDecisionsSurviveServiceRestart()
    {
        var plan = await ApproveAsync(); var reopened = new WorkflowStore(State); var loaded = reopened.Load(plan.Id, 1);
        Assert.Equal(plan.Hash, loaded.Hash); Assert.True(reopened.ReadApproval(loaded, ApprovalSubject.Plan)!.Approved);
        Assert.Equal(ApprovalSubject.Plan, reopened.ReadApproval(loaded, ApprovalSubject.Plan)!.Subject);
    }
    [Fact] public async Task PlanApprovalDoesNotAuthorizeSourceApply()
    {
        var plan = await ApproveAsync(); var result = await Controller().ExecuteAsync(plan.Id, 1, default);
        Assert.Throws<UnauthorizedAccessException>(() => new ApprovedBatchApplier(store, workspace, new AtomicFileCommitter()).Apply(plan, result.Proposal!));
    }
    [Fact] public async Task SeparateChangeApprovalAppliesExactBytesInTestFixture()
    {
        var plan = await ApproveAsync(); await Controller().ExecuteAsync(plan.Id, 1, default);
        var result = await Controller().DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default);
        Assert.Equal(DevelopmentState.Applied, result.State); Assert.Equal("candidate-a", File.ReadAllText(Path.Combine(Target, "a.cs")));
        Assert.Equal(ApprovalSubject.Changes, store.ReadApproval(plan, ApprovalSubject.Changes)!.Subject);
    }
    [Fact] public async Task RejectedChangesPreserveTarget()
    {
        var plan = await ApproveAsync(); await Controller().ExecuteAsync(plan.Id, 1, default);
        var result = await Controller().DecideChangesAsync(plan.Id, 1, new Decision(false), "TEST-DOUBLE", default);
        Assert.Equal(DevelopmentState.ChangesRejected, result.State); Assert.Equal("original-a", File.ReadAllText(Path.Combine(Target, "a.cs")));
    }
    [Fact] public async Task MissingIsolationBlocksBeforeProviderAndTester()
    {
        var plan = await ApproveAsync(); isolation.Verified = false;
        var result = await Controller().ExecuteAsync(plan.Id, 1, default);
        Assert.Equal(DevelopmentState.Blocked, result.State); Assert.Equal(0, generation.DeveloperCalls); Assert.Equal(0, commands.Calls);
    }
    [Theory] [InlineData("../escape.cs")] [InlineData(".git/config")] [InlineData("a.cs:secret")] [InlineData(".codex/config.toml")]
    public async Task UnsafeDeveloperPathsAreRejected(string path)
    {
        var plan = await ApproveAsync(); generation.Path = path;
        var result = await Controller().ExecuteAsync(plan.Id, 1, default); Assert.Equal(DevelopmentState.Failed, result.State);
        Assert.Equal("original-a", File.ReadAllText(Path.Combine(Target, "a.cs")));
    }
    [Fact] public async Task UnauthorizedExtraWorkspaceFileIsDetected()
    {
        var plan = await ApproveAsync(); var directory = workspace.Prepare(plan); File.WriteAllText(Path.Combine(directory, "outside-scope.cs"), "bad");
        Assert.Throws<UnauthorizedAccessException>(() => workspace.ReadScope(plan, directory));
    }
    [Fact] public async Task ChangedPlanBytesAreRejected()
    {
        var plan = await ApproveAsync(); var path = Path.Combine(State, plan.Id.ToString("N"), "v1", "plan.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Disposable test plan", "tampered"));
        Assert.Throws<InvalidDataException>(() => store.Load(plan.Id, 1));
    }
    [Fact] public async Task NewRevisionDoesNotInheritApproval()
    {
        var plan = await ApproveAsync(); var next = store.Create(Plan(), Target, ["a.cs", "b.cs"], 2, plan.Id, plan.Hash);
        Assert.Null(store.ReadApproval(next, ApprovalSubject.Plan));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Controller().ExecuteAsync(plan.Id, 2, default));
    }
    [Fact] public async Task StaleTargetBeforeExecutionIsRejected()
    {
        var plan = await ApproveAsync(); File.WriteAllText(Path.Combine(Target, "a.cs"), "external edit");
        Assert.Equal(DevelopmentState.Failed, (await Controller().ExecuteAsync(plan.Id, 1, default)).State); Assert.Equal(0, generation.DeveloperCalls);
    }
    [Fact] public async Task StaleTargetBeforeApplyIsRejected()
    {
        var plan = await ApproveAsync(); await Controller().ExecuteAsync(plan.Id, 1, default); File.WriteAllText(Path.Combine(Target, "a.cs"), "external edit");
        await Assert.ThrowsAsync<InvalidDataException>(() => Controller().DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default));
        Assert.Equal("external edit", File.ReadAllText(Path.Combine(Target, "a.cs")));
    }
    [Fact] public async Task TamperedStagedSourceIsRejected()
    {
        var plan = await ApproveAsync(); var result = await Controller().ExecuteAsync(plan.Id, 1, default);
        File.WriteAllText(Path.Combine(result.Proposal!.Workspace, "a.cs"), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => Controller().DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default));
    }
    [Fact] public async Task RehashedManifestCannotExpandApprovedScope()
    {
        var plan = await ApproveAsync(); var result = await Controller().ExecuteAsync(plan.Id, 1, default);
        var forged = result.Proposal! with { Files = [new("unexpected.cs", null, "fake", 4)], Hash = "" };
        forged = forged with { Hash = WorkflowHash.Proposal(forged) };
        Assert.Throws<UnauthorizedAccessException>(() => workspace.Validate(plan, forged));
    }
    [Theory] [InlineData("build")] [InlineData("test")] [InlineData("timeout")]
    public async Task FailedBuildTestsAndTimeoutDoNotReachReviewer(string failure)
    {
        var plan = await ApproveAsync(); commands.Failure = failure;
        var result = await Controller().ExecuteAsync(plan.Id, 1, default);
        Assert.Equal(DevelopmentState.Failed, result.State); Assert.Equal(0, generation.ReviewerCalls);
        Assert.Equal(failure is "build" or "timeout" ? 1 : 2, commands.Calls);
    }
    [Fact] public async Task CancellationRecordsTerminalStateWithoutApply()
    {
        var plan = await ApproveAsync(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await Controller().ExecuteAsync(plan.Id, 1, cancellation.Token); Assert.Equal(DevelopmentState.Canceled, result.State); Assert.Equal(0, generation.DeveloperCalls);
    }
    [Fact] public async Task PersistedCancellationPreventsResume()
    {
        var plan = await ApproveAsync(); Controller().Cancel(plan.Id, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Controller().ExecuteAsync(plan.Id, 1, default));
    }
    [Fact] public async Task ReviewerRejectionBlocksHumanApply()
    {
        var plan = await ApproveAsync(); generation.Review = "ChangesRequested";
        Assert.Equal(DevelopmentState.Blocked, (await Controller().ExecuteAsync(plan.Id, 1, default)).State);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Controller().DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default));
    }
    [Fact] public async Task ProviderUnavailableFailsWithoutApply()
    {
        var plan = await ApproveAsync(); generation.Unavailable = true;
        Assert.Equal(DevelopmentState.Failed, (await Controller().ExecuteAsync(plan.Id, 1, default)).State);
        Assert.Equal("original-a", File.ReadAllText(Path.Combine(Target, "a.cs")));
    }
    [Fact] public async Task PartialCommitFailureRollsBackEarlierFiles()
    {
        var plan = await ApproveAsync(); await Controller().ExecuteAsync(plan.Id, 1, default);
        var result = await Controller(new FailSecond()).DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default);
        Assert.Equal(DevelopmentState.RolledBack, result.State);
        Assert.Equal("original-a", File.ReadAllText(Path.Combine(Target, "a.cs"))); Assert.Equal("original-b", File.ReadAllText(Path.Combine(Target, "b.cs")));
    }
    [Fact] public async Task ExceptionAfterAtomicReplacementAlsoRollsBack()
    {
        var plan = await ApproveAsync(); await Controller().ExecuteAsync(plan.Id, 1, default);
        var result = await Controller(new FailAfterReplacement()).DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default);
        Assert.Equal(DevelopmentState.RolledBack, result.State); Assert.Equal("original-a", File.ReadAllText(Path.Combine(Target, "a.cs"))); Assert.Equal("original-b", File.ReadAllText(Path.Combine(Target, "b.cs")));
    }
    [Fact] public async Task RollbackNeverOverwritesAnUnexpectedExternalEdit()
    {
        var plan = await ApproveAsync(); await Controller().ExecuteAsync(plan.Id, 1, default);
        var result = await Controller(new ConflictDuringCommit(Target)).DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default);
        Assert.Equal(DevelopmentState.RecoveryRequired, result.State); Assert.Equal("external edit", File.ReadAllText(Path.Combine(Target, "a.cs")));
    }
    [Fact] public async Task SimulatedEvidenceCannotAuthorizeRealApply()
    {
        var plan = await ApproveAsync(); commands.Simulated = true; await Controller().ExecuteAsync(plan.Id, 1, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Controller().DecideChangesAsync(plan.Id, 1, new Decision(true), "TEST-DOUBLE", default));
    }
    [Fact] public void CyclicDependenciesAreRejectedAtPlanPersistence()
    {
        var plan = Plan(); var original = plan.Tasks[0]; plan.Tasks[0] = new() { Id = original.Id, Role = original.Role, Description = original.Description, AcceptanceCriteria = original.AcceptanceCriteria, Dependencies = ["R1"] };
        Assert.Throws<InvalidDataException>(() => store.Create(plan, Target, ["a.cs"]));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Decision(bool approved) : IApprovalService { public Task<bool> ApproveAsync(string operation, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(approved); } }
    private sealed class FakeIsolation : IExecutionIsolation { public bool Verified = true; public Task<IsolationStatus> VerifyAsync(CancellationToken ct) => Task.FromResult(new IsolationStatus(Verified, "TEST DOUBLE, not operating-system proof")); }
    private sealed class FakeGeneration : IStructuredCodexProvider
    {
        public int DeveloperCalls, ReviewerCalls; public string Path = "a.cs", Review = "ReadyForHumanReview"; public bool Unavailable;
        public Task<StructuredCodexResult> GenerateAsync(string role, string instructions, string schema, string model, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (Unavailable) return Task.FromResult(new StructuredCodexResult(ProviderError.Unavailable));
            if (role == "developer") { DeveloperCalls++; return Task.FromResult(new StructuredCodexResult(ProviderError.None, JsonSerializer.Serialize(new { files = new[] { new SourceChange(Path, "candidate-a"), new SourceChange("b.cs", "candidate-b") } }, PlanCodec.Json))); }
            ReviewerCalls++; using var input = JsonDocument.Parse(instructions);
            return Task.FromResult(new StructuredCodexResult(ProviderError.None, JsonSerializer.Serialize(new { decision = Review,
                proposalHash = input.RootElement.GetProperty("hash").GetString(), findings = Array.Empty<ReviewFinding>(), checkedCriteria = input.RootElement.GetProperty("plan").GetProperty("tasks").EnumerateArray().SelectMany(t => t.GetProperty("acceptanceCriteria").EnumerateArray().Select(c => c.GetString())).ToArray() }, PlanCodec.Json)));
        }
    }
    private sealed class FakeCommands : IIsolatedCommandExecutor
    {
        // Unit fixtures intentionally claim evidence to exercise trust checks. No commands are run here.
        public int Calls; public string Failure = ""; public bool Simulated;
        public Task<CommandEvidence> ExecuteAsync(string workspace, TestCommand command, CancellationToken ct)
        {
            Calls++; ct.ThrowIfCancellationRequested(); return Task.FromResult(new CommandEvidence(command.ToString(), Failure == command.ToString().ToLowerInvariant() ? 1 : 0, Failure == "timeout", false, "TEST DOUBLE"));
        }
        public Task<TestEvidence> ReadEvidenceAsync(string workspace, CommandEvidence[] evidence, CancellationToken ct) => Task.FromResult(new TestEvidence(true, Simulated, Failure == "", 3, Failure == "" ? 3 : 2, Failure == "" ? 0 : 1, evidence));
    }
    private sealed class FailSecond : IAtomicFileCommitter
    {
        private int count; public void Commit(string path, byte[] bytes) { if (++count == 2) throw new IOException("Injected failure"); new AtomicFileCommitter().Commit(path, bytes); }
    }
    private sealed class FailAfterReplacement : IAtomicFileCommitter
    {
        private int count; public void Commit(string path, byte[] bytes) { new AtomicFileCommitter().Commit(path, bytes); if (++count == 2) throw new IOException("Failure after replacement"); }
    }
    private sealed class ConflictDuringCommit(string target) : IAtomicFileCommitter
    {
        private int count; public void Commit(string path, byte[] bytes)
        {
            if (++count == 2) { File.WriteAllText(Path.Combine(target, "a.cs"), "external edit"); throw new IOException("Concurrent edit fixture"); }
            new AtomicFileCommitter().Commit(path, bytes);
        }
    }
}
