namespace AI.GTX.Harness.Core;

public enum DevelopmentState { AwaitingPlanApproval, PlanRejected, Ready, Developing, Testing, Reviewing, AwaitingChangeApproval, ChangesRejected, Applied, Canceled, Blocked, Failed, RolledBack, RecoveryRequired }
public enum ApprovalSubject { Plan, Changes }
public enum ReviewDecision { ReadyForHumanReview, ChangesRequested, Blocked }
public sealed record FileDigest(string Path, string Hash);
public sealed record SourceChange(string Path, string Content);
public sealed record DevelopmentPlanPackage(int SchemaVersion, Guid Id, int Revision, string? PreviousHash, DevelopmentPlan Plan,
    string TargetRoot, string[] Scope, FileDigest[] Baseline, string Hash);
public sealed record ApprovalRecord(int SchemaVersion, Guid Id, ApprovalSubject Subject, string ContentHash, bool Approved, string Actor, DateTimeOffset At);
public sealed record FileChangeManifest(string Path, string? BeforeHash, string AfterHash, int Bytes);
public sealed record DevelopmentProposal(Guid Id, string PlanHash, string TaskId, string Workspace, FileChangeManifest[] Files, string Diff, string Hash);
public sealed record CommandEvidence(string Command, int ExitCode, bool TimedOut, bool Canceled, string Output);
public sealed record TestEvidence(bool IsolationVerified, bool Simulated, bool Passed, int Total, int PassedCount, int FailedCount, CommandEvidence[] Commands);
public sealed record ReviewFinding(string Severity, string Path, string Message, string AcceptanceCriterion);
public sealed record ReviewEvidence(ReviewDecision Decision, string ProposalHash, ReviewFinding[] Findings, string[] CheckedCriteria);
public sealed record DevelopmentProgress(DevelopmentState State, Dictionary<string, ExecutionState> Tasks,
    DevelopmentProposal? Proposal = null, TestEvidence? Tests = null, ReviewEvidence? Review = null, string Message = "");
public sealed record IsolationStatus(bool Verified, string Detail);
public sealed record StructuredCodexResult(ProviderError Error, string Json = "", int? ExitCode = null, string Diagnostics = "");
public interface IStructuredCodexProvider
{
    Task<StructuredCodexResult> GenerateAsync(string role, string instructions, string schema, string model, CancellationToken cancellationToken);
}
public interface IWorkflowStore
{
    DevelopmentPlanPackage Create(DevelopmentPlan plan, string targetRoot, string[] scope, int revision = 1, Guid? id = null, string? previousHash = null);
    DevelopmentPlanPackage Load(Guid id, int revision);
    DevelopmentProgress ReadProgress(DevelopmentPlanPackage package);
    void WriteProgress(DevelopmentPlanPackage package, DevelopmentProgress progress);
    void WriteApproval(DevelopmentPlanPackage package, ApprovalRecord approval);
    ApprovalRecord? ReadApproval(DevelopmentPlanPackage package, ApprovalSubject subject);
    IDisposable Lock(DevelopmentPlanPackage package);
}
public interface IDevelopmentWorkspace
{
    string Prepare(DevelopmentPlanPackage package);
    IReadOnlyDictionary<string, string> ReadScope(DevelopmentPlanPackage package, string workspace);
    DevelopmentProposal Stage(DevelopmentPlanPackage package, string taskId, string workspace, SourceChange[] changes);
    void Validate(DevelopmentPlanPackage package, DevelopmentProposal proposal);
    void ValidateSources(DevelopmentPlanPackage package, DevelopmentProposal proposal);
}
public interface IExecutionIsolation { Task<IsolationStatus> VerifyAsync(CancellationToken cancellationToken); }
public interface IDeveloperExecutionAgent
{
    Task<SourceChange[]> DevelopAsync(DevelopmentPlanPackage plan, PlanTask task, IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken);
}
public interface IControlledTesterAgent { Task<TestEvidence> TestAsync(DevelopmentProposal proposal, CancellationToken cancellationToken); }
public interface IStructuredReviewerAgent
{
    Task<ReviewEvidence> ReviewAsync(DevelopmentPlanPackage plan, PlanTask task, DevelopmentProposal proposal, TestEvidence tests, CancellationToken cancellationToken);
}
public enum TestCommand { Build, Test }
public interface IIsolatedCommandExecutor
{
    Task<CommandEvidence> ExecuteAsync(string workspace, TestCommand command, CancellationToken cancellationToken);
    Task<TestEvidence> ReadEvidenceAsync(string workspace, CommandEvidence[] commands, CancellationToken cancellationToken);
}
public interface IApprovedBatchApplier { DevelopmentState Apply(DevelopmentPlanPackage plan, DevelopmentProposal proposal); }
