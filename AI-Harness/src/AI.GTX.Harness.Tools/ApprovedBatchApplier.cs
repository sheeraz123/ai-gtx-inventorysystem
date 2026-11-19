using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Tools;

public interface IAtomicFileCommitter { void Commit(string path, byte[] bytes); }
public sealed class AtomicFileCommitter : IAtomicFileCommitter
{
    public void Commit(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
// Approval is enforced by the controller. This broker independently verifies both persisted decisions.
public sealed class ApprovedBatchApplier(IWorkflowStore store, IDevelopmentWorkspace workspace, IAtomicFileCommitter files) : IApprovedBatchApplier
{
    public DevelopmentState Apply(DevelopmentPlanPackage plan, DevelopmentProposal proposal)
    {
        plan = store.Load(plan.Id, plan.Revision);
        var progress = store.ReadProgress(plan);
        var planApproval = store.ReadApproval(plan, ApprovalSubject.Plan);
        var changeApproval = store.ReadApproval(plan, ApprovalSubject.Changes);
        if (planApproval is not { Approved: true, Subject: ApprovalSubject.Plan } || planApproval.ContentHash != plan.Hash
            || changeApproval is not { Approved: true, Subject: ApprovalSubject.Changes } || changeApproval.ContentHash != WorkflowHash.ChangesApproval(progress)
            || progress.State != DevelopmentState.AwaitingChangeApproval || progress.Proposal?.Hash != proposal.Hash
            || progress.Tests is not { Passed: true, IsolationVerified: true, Simulated: false } || progress.Review?.Decision != ReviewDecision.ReadyForHumanReview)
            throw new UnauthorizedAccessException("Separate valid plan and change approvals plus verification are required.");
        workspace.Validate(plan, proposal);
        var originals = WorkflowFiles.Snapshot(plan.TargetRoot);
        var applied = new List<FileChangeManifest>();
        var journalRoot = Path.Combine(proposal.Workspace, "..", "journal-" + proposal.Id.ToString("N"));
        new WorkspaceReader(Path.GetFullPath(journalRoot)).Resolve("."); Directory.CreateDirectory(journalRoot);
        // Persist recovery bytes and intent before the first source mutation.
        foreach (var entry in proposal.Files)
            if (originals.TryGetValue(entry.Path, out var original)) File.WriteAllBytes(Path.Combine(journalRoot, WorkflowHash.Bytes(System.Text.Encoding.UTF8.GetBytes(entry.Path)) + ".bak"), original);
        WorkflowFiles.AtomicJson(Path.Combine(journalRoot, "intent.json"), new { plan.Hash, ProposalHash = proposal.Hash, proposal.Files, Status = "Applying" });
        try
        {
            foreach (var entry in proposal.Files)
            {
                var target = new WorkspaceReader(plan.TargetRoot).Resolve(entry.Path);
                var current = File.Exists(target) ? WorkflowHash.Bytes(File.ReadAllBytes(target)) : null;
                if (current != entry.BeforeHash) throw new InvalidDataException("Target changed during apply.");
                var candidate = new WorkspaceReader(proposal.Workspace).Resolve(entry.Path);
                var bytes = File.ReadAllBytes(candidate);
                if (WorkflowHash.Bytes(bytes) != entry.AfterHash) throw new InvalidDataException("Candidate changed during apply.");
                applied.Add(entry); files.Commit(target, bytes);
                WorkflowFiles.AtomicJson(Path.Combine(journalRoot, "progress.json"), applied);
            }
            WorkflowFiles.AtomicJson(Path.Combine(journalRoot, "result.json"), new { Status = "Applied" });
            return DevelopmentState.Applied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var recovery = false;
            foreach (var entry in applied.AsEnumerable().Reverse())
            {
                try
                {
                    var target = new WorkspaceReader(plan.TargetRoot).Resolve(entry.Path);
                    var current = File.Exists(target) ? WorkflowHash.Bytes(File.ReadAllBytes(target)) : null;
                    if (current == entry.BeforeHash) continue;
                    if (current != entry.AfterHash) { recovery = true; continue; }
                    if (originals.TryGetValue(entry.Path, out var bytes)) new AtomicFileCommitter().Commit(target, bytes);
                    else File.Delete(target);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { recovery = true; }
            }
            WorkflowFiles.AtomicJson(Path.Combine(journalRoot, "result.json"), new { Status = recovery ? "RecoveryRequired" : "RolledBack", Error = ex.GetType().Name });
            return recovery ? DevelopmentState.RecoveryRequired : DevelopmentState.RolledBack;
        }
    }
}
