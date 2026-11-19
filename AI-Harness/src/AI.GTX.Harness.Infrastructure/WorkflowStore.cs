using System.Text.Json;
using AI.GTX.Harness.Core;
using AI.GTX.Harness.Tools;

namespace AI.GTX.Harness.Infrastructure;

public sealed class WorkflowStore(string root) : IWorkflowStore
{
    private string DirectoryFor(Guid id, int revision)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        return new WorkspaceReader(root).Resolve($"{id:N}/v{revision}");
    }
    public DevelopmentPlanPackage Create(DevelopmentPlan plan, string targetRoot, string[] scope, int revision = 1, Guid? id = null, string? previousHash = null)
    {
        PlanCodec.Validate(plan);
        targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot));
        var stateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (targetRoot.Equals(stateRoot, comparison) || targetRoot.StartsWith(stateRoot + Path.DirectorySeparatorChar, comparison)
            || stateRoot.StartsWith(targetRoot + Path.DirectorySeparatorChar, comparison)) throw new UnauthorizedAccessException("Target and workflow state must be separate.");
        var normalized = scope.Select(s => WorkflowFiles.Normalize(targetRoot, s)).Order(StringComparer.Ordinal).ToArray();
        if (normalized.Length is < 1 or > 64 || normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length) throw new InvalidDataException("Scope requires unique, explicit source paths.");
        var packageId = id ?? Guid.NewGuid();
        if (revision != 1)
        {
            if (id == null || Load(packageId, revision - 1).Hash != previousHash) throw new InvalidDataException("Revision does not extend its previous plan.");
        }
        else if (previousHash != null) throw new InvalidDataException("First revision has no parent.");
        var package = new DevelopmentPlanPackage(1, packageId, revision, previousHash, plan, targetRoot, normalized,
            WorkflowFiles.Digests(WorkflowFiles.Snapshot(targetRoot)), "");
        package = package with { Hash = WorkflowHash.Plan(package) };
        var directory = DirectoryFor(package.Id, revision);
        Directory.CreateDirectory(directory);
        // Exclusive creation prevents silently overwriting a previous revision.
        using (var file = new FileStream(Path.Combine(directory, "plan.json"), FileMode.CreateNew)) JsonSerializer.Serialize(file, package, PlanCodec.Json);
        WriteProgress(package, new(DevelopmentState.AwaitingPlanApproval, plan.Tasks.ToDictionary(t => t.Id, _ => ExecutionState.Pending)));
        return Load(package.Id, revision);
    }
    public DevelopmentPlanPackage Load(Guid id, int revision)
    {
        var package = Read<DevelopmentPlanPackage>(Path.Combine(DirectoryFor(id, revision), "plan.json"));
        if (package.SchemaVersion != 1 || package.Id != id || package.Revision != revision || WorkflowHash.Plan(package) != package.Hash) throw new InvalidDataException("Plan hash or version mismatch.");
        PlanCodec.Validate(package.Plan);
        foreach (var scope in package.Scope) if (WorkflowFiles.Normalize(package.TargetRoot, scope) != scope) throw new InvalidDataException("Invalid persisted scope.");
        return package;
    }
    public DevelopmentProgress ReadProgress(DevelopmentPlanPackage package) => Read<DevelopmentProgress>(Path.Combine(DirectoryFor(package.Id, package.Revision), "progress.json"));
    public void WriteProgress(DevelopmentPlanPackage package, DevelopmentProgress progress) => WorkflowFiles.AtomicJson(Path.Combine(DirectoryFor(package.Id, package.Revision), "progress.json"), progress);
    public void WriteApproval(DevelopmentPlanPackage package, ApprovalRecord approval)
    {
        if (approval.SchemaVersion != 1 || approval.Id == Guid.Empty || string.IsNullOrWhiteSpace(approval.Actor)) throw new InvalidDataException("Invalid approval record.");
        var directory = DirectoryFor(package.Id, package.Revision);
        using (var file = new FileStream(Path.Combine(directory, $"decision-{approval.Id:N}.json"), FileMode.CreateNew)) JsonSerializer.Serialize(file, approval, PlanCodec.Json);
        WorkflowFiles.AtomicJson(Path.Combine(directory, approval.Subject == ApprovalSubject.Plan ? "plan-decision.json" : "changes-decision.json"), approval);
    }
    public ApprovalRecord? ReadApproval(DevelopmentPlanPackage package, ApprovalSubject subject)
    {
        var path = Path.Combine(DirectoryFor(package.Id, package.Revision), subject == ApprovalSubject.Plan ? "plan-decision.json" : "changes-decision.json");
        if (!File.Exists(path)) return null;
        var decision = Read<ApprovalRecord>(path);
        if (decision.SchemaVersion != 1 || decision.Subject != subject) throw new InvalidDataException("Approval subject mismatch.");
        return decision;
    }
    public IDisposable Lock(DevelopmentPlanPackage package) => new FileStream(Path.Combine(DirectoryFor(package.Id, package.Revision), "execution.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static T Read<T>(string path)
    {
        new WorkspaceReader(Path.GetDirectoryName(path)!).Resolve(Path.GetFileName(path));
        if (new FileInfo(path).Length > 5_000_000) throw new InvalidDataException("Workflow record too large.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), PlanCodec.Json) ?? throw new InvalidDataException("Workflow record missing.");
    }
}
