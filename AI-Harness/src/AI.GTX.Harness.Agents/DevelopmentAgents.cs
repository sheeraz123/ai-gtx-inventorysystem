using System.Text.Json;
using System.Text.Json.Serialization;
using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Agents;

public sealed class CodexDeveloperExecutionAgent(IStructuredCodexProvider codex, IWorkflowStore store) : IDeveloperExecutionAgent
{
    private sealed class Draft { public required SourceChange[] Files { get; init; } }
    public async Task<SourceChange[]> DevelopAsync(DevelopmentPlanPackage plan, PlanTask task, IReadOnlyDictionary<string, string> files, CancellationToken ct)
    {
        var persisted = store.Load(plan.Id, plan.Revision); var approval = store.ReadApproval(persisted, ApprovalSubject.Plan);
        var progress = store.ReadProgress(persisted);
        var eligible = persisted.Plan.Tasks.Single(t => t.Id == task.Id && t.Role == AgentRoles.Developer);
        if (persisted.Hash != plan.Hash || approval is not { Approved: true } || approval.ContentHash != plan.Hash
            || progress.State != DevelopmentState.Developing || progress.Tasks[eligible.Id] != ExecutionState.Running
            || eligible.Dependencies.Any(d => progress.Tasks[d] != ExecutionState.Completed)) throw new UnauthorizedAccessException("Developer requires an approved plan and eligible task.");
        var input = JsonSerializer.Serialize(new { Task = eligible, Scope = persisted.Scope, Files = files,
            Instruction = "Return complete UTF-8 file contents for changes to the explicit scope only. No deletions or commands. Include implementation and tests required by the approved task." }, PlanCodec.Json);
        var result = await codex.GenerateAsync("developer", input, Schema, "default", ct);
        if (result.Error != ProviderError.None) throw new InvalidOperationException("Developer provider unavailable or failed.");
        var draft = JsonSerializer.Deserialize<Draft>(result.Json, PlanCodec.Json) ?? throw new InvalidDataException("Missing changes.");
        if (draft.Files is not { Length: > 0 and <= 32 } || draft.Files.Any(f => f == null || string.IsNullOrWhiteSpace(f.Path) || f.Content == null || !persisted.Scope.Contains(f.Path))) throw new InvalidDataException("Invalid scoped changes.");
        return draft.Files;
    }
    private const string Schema = """
    {"type":"object","additionalProperties":false,"required":["files"],"properties":{"files":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["path","content"],"properties":{"path":{"type":"string"},"content":{"type":"string"}}}}}}
    """;
}
public sealed class ControlledTesterAgent(IExecutionIsolation isolation, IIsolatedCommandExecutor commands) : IControlledTesterAgent
{
    public async Task<TestEvidence> TestAsync(DevelopmentProposal proposal, CancellationToken ct)
    {
        if (!(await isolation.VerifyAsync(ct)).Verified) throw new UnauthorizedAccessException("No verified OS isolation for untrusted builds/tests.");
        var build = (await commands.ExecuteAsync(proposal.Workspace, TestCommand.Build, ct)) with { Command = nameof(TestCommand.Build) };
        if (build.ExitCode != 0 || build.TimedOut || build.Canceled) return new(true, false, false, 0, 0, 0, [build]);
        var tests = (await commands.ExecuteAsync(proposal.Workspace, TestCommand.Test, ct)) with { Command = nameof(TestCommand.Test) };
        var evidence = await commands.ReadEvidenceAsync(proposal.Workspace, [build, tests], ct);
        return evidence with { IsolationVerified = true, Passed = evidence.Passed && evidence.Total > 0 && evidence.PassedCount == evidence.Total
            && evidence.FailedCount == 0 && tests.ExitCode == 0 && !tests.TimedOut && !tests.Canceled, Commands = [build, tests] };
    }
}
public sealed class CodexStructuredReviewerAgent(IStructuredCodexProvider codex) : IStructuredReviewerAgent
{
    private static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson() { var value = new JsonSerializerOptions(PlanCodec.Json); value.Converters.Add(new JsonStringEnumConverter<ReviewDecision>()); return value; }
    public async Task<ReviewEvidence> ReviewAsync(DevelopmentPlanPackage plan, PlanTask task, DevelopmentProposal proposal, TestEvidence tests, CancellationToken ct)
    {
        if (!tests.Passed || !tests.IsolationVerified || tests.Total < 1) throw new InvalidOperationException("Reviewer requires successful isolated test evidence.");
        var input = JsonSerializer.Serialize(new { Task = task, plan.Plan, proposal.Hash, proposal.Diff, proposal.Files, Tests = tests,
            Instruction = "Review security, correctness, maintainability and every listed acceptance criterion against the diff and build/test evidence. Return exact proposalHash and exact checkedCriteria strings. You cannot grant human approval." }, PlanCodec.Json);
        var result = await codex.GenerateAsync("reviewer", input, Schema, "default", ct);
        if (result.Error != ProviderError.None) throw new InvalidOperationException("Reviewer provider unavailable or failed.");
        var review = JsonSerializer.Deserialize<ReviewEvidence>(result.Json, Json) ?? throw new InvalidDataException("Missing review.");
        Validate(review, task, proposal);
        if (plan.Plan.Tasks.SelectMany(t => t.AcceptanceCriteria).Any(c => !review.CheckedCriteria.Contains(c, StringComparer.Ordinal))) throw new InvalidDataException("Every plan task's acceptance criteria must be reviewed.");
        return review;
    }
    public static void Validate(ReviewEvidence review, PlanTask task, DevelopmentProposal proposal)
    {
        if (!Enum.IsDefined(review.Decision) || review.ProposalHash != proposal.Hash || review.Findings == null || review.CheckedCriteria == null
            || task.AcceptanceCriteria.Any(c => !review.CheckedCriteria.Contains(c, StringComparer.Ordinal))
            || review.Findings.Length > 100 || review.Findings.Any(f => f == null || string.IsNullOrWhiteSpace(f.Message)
                || f.Severity is not ("critical" or "high" or "medium" or "low") || f.Path == null || f.AcceptanceCriterion == null)) throw new InvalidDataException("Malformed or incomplete review.");
        if (review.Decision == ReviewDecision.ReadyForHumanReview && review.Findings.Any(f => f.Severity is "critical" or "high")) throw new InvalidDataException("Blocking findings cannot approve a proposal.");
    }
    private const string Schema = """
    {"type":"object","additionalProperties":false,"required":["decision","proposalHash","findings","checkedCriteria"],"properties":{
    "decision":{"type":"string","enum":["ReadyForHumanReview","ChangesRequested","Blocked"]},"proposalHash":{"type":"string"},
    "checkedCriteria":{"type":"array","items":{"type":"string"}},"findings":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["severity","path","message","acceptanceCriterion"],"properties":{
    "severity":{"type":"string","enum":["critical","high","medium","low"]},"path":{"type":"string"},"message":{"type":"string"},"acceptanceCriterion":{"type":"string"}}}}}}
    """;
}
