using System.Text.Json;
using AI.GTX.Harness.Core;
using Microsoft.Extensions.Logging;

namespace AI.GTX.Harness.Orchestrator;

// One developer task plus dependent test/review steps: deliberately the smallest executable slice.
public sealed class DevelopmentWorkflowController(IWorkflowStore store, IDevelopmentWorkspace workspaces, IExecutionIsolation isolation,
    IDeveloperExecutionAgent developer, IControlledTesterAgent tester, IStructuredReviewerAgent reviewer,
    IApprovedBatchApplier applier, ILogger<DevelopmentWorkflowController> logger)
{
    public async Task<DevelopmentProgress> DecidePlanAsync(Guid id, int revision, IApprovalService approval, string actor, CancellationToken ct)
    {
        var plan = store.Load(id, revision); using var lease = store.Lock(plan);
        var state = store.ReadProgress(plan);
        if (state.State != DevelopmentState.AwaitingPlanApproval) throw new InvalidOperationException("Plan decision is not pending.");
        var approved = await approval.ApproveAsync($"PLAN {plan.Hash}\nTarget: {plan.TargetRoot}\nScope: {string.Join(", ", plan.Scope)}\n{JsonSerializer.Serialize(plan.Plan, PlanCodec.Json)}", ct);
        store.WriteApproval(plan, new(1, Guid.NewGuid(), ApprovalSubject.Plan, plan.Hash, approved, actor, DateTimeOffset.UtcNow));
        state = state with { State = approved ? DevelopmentState.Ready : DevelopmentState.PlanRejected, Message = "Plan decision recorded; source apply is not authorized." };
        store.WriteProgress(plan, state); return state;
    }
    public async Task<DevelopmentProgress> ExecuteAsync(Guid id, int revision, CancellationToken ct)
    {
        var plan = store.Load(id, revision); using var lease = store.Lock(plan);
        var state = store.ReadProgress(plan); var approval = store.ReadApproval(plan, ApprovalSubject.Plan);
        if (approval is not { Approved: true } || approval.Subject != ApprovalSubject.Plan || approval.ContentHash != plan.Hash) throw new UnauthorizedAccessException("An approved, unchanged plan is required.");
        if (state.State is not (DevelopmentState.Ready or DevelopmentState.Blocked)) throw new InvalidOperationException("Workflow is not ready for execution.");
        if (state.State == DevelopmentState.Blocked && state.Proposal != null) throw new InvalidOperationException("A reviewed/failed proposal requires a new plan revision before execution.");
        try
        {
            ct.ThrowIfCancellationRequested();
            var boundary = await isolation.VerifyAsync(ct);
            if (!boundary.Verified) { state = state with { State = DevelopmentState.Blocked, Message = boundary.Detail }; store.WriteProgress(plan, state); return state; }
            var dev = plan.Plan.Tasks.Single(t => t.Role == AgentRoles.Developer);
            var testing = plan.Plan.Tasks.Single(t => t.Role == AgentRoles.Tester);
            var reviewTask = plan.Plan.Tasks.Single(t => t.Role == AgentRoles.Reviewer);
            if (plan.Plan.Tasks.Length != 3) throw new InvalidOperationException("This slice requires exactly one Developer, Tester and Reviewer task.");
            void Begin(PlanTask task)
            {
                if (task.Dependencies.Any(d => state.Tasks[d] != ExecutionState.Completed)) throw new InvalidOperationException("Task dependencies are not complete.");
                state.Tasks[task.Id] = ExecutionState.Running; store.WriteProgress(plan, state);
            }
            state = state with { State = DevelopmentState.Developing }; Begin(dev);
            var workspace = workspaces.Prepare(plan);
            var changes = await developer.DevelopAsync(plan, dev, workspaces.ReadScope(plan, workspace), ct);
            var proposal = workspaces.Stage(plan, dev.Id, workspace, changes);
            state.Tasks[dev.Id] = ExecutionState.Completed; state = state with { State = DevelopmentState.Testing, Proposal = proposal }; Begin(testing);
            var tests = await tester.TestAsync(proposal, ct);
            workspaces.Validate(plan, proposal);
            state = state with { Tests = tests };
            if (!tests.Passed || !tests.IsolationVerified || tests.Total < 1 || tests.Total != tests.PassedCount || tests.FailedCount != 0
                || tests.Commands is not { Length: 2 } || tests.Commands[0].Command != nameof(TestCommand.Build) || tests.Commands[1].Command != nameof(TestCommand.Test)
                || tests.Commands.Any(c => c.ExitCode != 0 || c.TimedOut || c.Canceled || c.Output.Length > 1_000_000))
            { state.Tasks[testing.Id] = ExecutionState.Failed; state = state with { State = DevelopmentState.Failed, Message = "Build/test verification did not pass." }; }
            else
            {
                state.Tasks[testing.Id] = ExecutionState.Completed; state = state with { State = DevelopmentState.Reviewing }; Begin(reviewTask);
                var review = await reviewer.ReviewAsync(plan, dev, proposal, tests, ct);
                if (review.ProposalHash != proposal.Hash || !Enum.IsDefined(review.Decision) || review.Findings == null || review.CheckedCriteria == null || plan.Plan.Tasks.SelectMany(t => t.AcceptanceCriteria).Any(c => !review.CheckedCriteria.Contains(c))
                    || (review.Decision == ReviewDecision.ReadyForHumanReview && review.Findings.Any(f => f.Severity is "critical" or "high"))) throw new InvalidDataException("Review evidence mismatch.");
                workspaces.Validate(plan, proposal);
                state.Tasks[reviewTask.Id] = review.Decision == ReviewDecision.ReadyForHumanReview ? ExecutionState.Completed : ExecutionState.Blocked;
                state = state with { Review = review, State = review.Decision == ReviewDecision.ReadyForHumanReview ? DevelopmentState.AwaitingChangeApproval : DevelopmentState.Blocked,
                    Message = "Reviewer outcome recorded. Human change approval is still required." };
            }
        }
        catch (OperationCanceledException) { state = state with { State = DevelopmentState.Canceled, Message = "Canceled; no target changes applied." }; }
        catch (Exception ex)
        {
            logger.LogError("Development workflow {PlanId} failed with {ErrorType}", plan.Id, ex.GetType().Name);
            state = state with { State = DevelopmentState.Failed, Message = "Execution failed; no target changes applied." };
        }
        foreach (var key in state.Tasks.Keys.ToArray()) if (state.Tasks[key] == ExecutionState.Running) state.Tasks[key] = state.State == DevelopmentState.Canceled ? ExecutionState.Canceled : ExecutionState.Failed;
        if (state.State is DevelopmentState.Failed or DevelopmentState.Canceled or DevelopmentState.Blocked)
            foreach (var key in state.Tasks.Keys.ToArray()) if (state.Tasks[key] == ExecutionState.Pending) state.Tasks[key] = ExecutionState.Blocked;
        store.WriteProgress(plan, state); return state;
    }
    public async Task<DevelopmentProgress> DecideChangesAsync(Guid id, int revision, IApprovalService approval, string actor, CancellationToken ct)
    {
        var plan = store.Load(id, revision); using var lease = store.Lock(plan); var state = store.ReadProgress(plan);
        if (state.State != DevelopmentState.AwaitingChangeApproval || state.Proposal == null) throw new UnauthorizedAccessException("Tested and reviewed proposal required.");
        workspaces.Validate(plan, state.Proposal);
        // Hash computation belongs to the storage/broker implementation, supplied by an approval binding below.
        var hash = ApprovalBinding.Changes(state);
        var approved = await approval.ApproveAsync($"CHANGES {hash}\nTarget: {plan.TargetRoot}\nFiles: {JsonSerializer.Serialize(state.Proposal.Files, PlanCodec.Json)}\n{state.Proposal.Diff}\nTests: {JsonSerializer.Serialize(state.Tests, PlanCodec.Json)}\nReview: {JsonSerializer.Serialize(state.Review, PlanCodec.Json)}", ct);
        store.WriteApproval(plan, new(1, Guid.NewGuid(), ApprovalSubject.Changes, hash, approved, actor, DateTimeOffset.UtcNow));
        ct.ThrowIfCancellationRequested();
        state = state with { State = approved ? applier.Apply(plan, state.Proposal) : DevelopmentState.ChangesRejected, Message = approved ? "Approved application attempted; inspect outcome." : "Changes rejected; target remains unchanged." };
        store.WriteProgress(plan, state); return state;
    }
    public DevelopmentProgress Cancel(Guid id, int revision)
    {
        var plan = store.Load(id, revision); using var lease = store.Lock(plan); var state = store.ReadProgress(plan);
        if (state.State is DevelopmentState.Applied or DevelopmentState.RecoveryRequired) throw new InvalidOperationException("Applied/recovery-required workflow cannot be canceled.");
        state = state with { State = DevelopmentState.Canceled, Message = "Canceled; approvals cannot be used to resume this revision." }; store.WriteProgress(plan, state); return state;
    }
}
internal static class ApprovalBinding
{
    public static string Changes(DevelopmentProgress progress) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { progress.Proposal, progress.Tests, progress.Review }, PlanCodec.Json)));
}
