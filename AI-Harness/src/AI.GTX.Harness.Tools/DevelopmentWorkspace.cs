using System.Text;
using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Tools;

public sealed class DevelopmentWorkspace(string stagingRoot) : IDevelopmentWorkspace
{
    private string ValidateWorkspace(string workspace)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(stagingRoot), Path.GetFullPath(workspace));
        if (!Guid.TryParseExact(relative, "N", out _)) throw new UnauthorizedAccessException("A direct, generated staging workspace is required.");
        return new WorkspaceReader(stagingRoot).Resolve(relative);
    }
    public string Prepare(DevelopmentPlanPackage package)
    {
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.TargetRoot)); var stagedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (sourceRoot.Equals(stagedRoot, comparison) || sourceRoot.StartsWith(stagedRoot + Path.DirectorySeparatorChar, comparison)
            || stagedRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, comparison)) throw new UnauthorizedAccessException("Source and staging must not overlap.");
        var source = WorkflowFiles.Snapshot(package.TargetRoot);
        if (WorkflowHash.Object(WorkflowFiles.Digests(source)) != WorkflowHash.Object(package.Baseline)) throw new InvalidDataException("Target changed since plan creation.");
        var workspace = new WorkspaceReader(stagingRoot).Resolve(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        foreach (var path in package.Scope.Where(source.ContainsKey))
        {
            var destination = new WorkspaceReader(workspace).Resolve(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllBytes(destination, source[path]);
        }
        return workspace;
    }
    public IReadOnlyDictionary<string, string> ReadScope(DevelopmentPlanPackage package, string workspace)
    {
        ValidateWorkspace(workspace);
        var files = WorkflowFiles.Snapshot(workspace);
        if (files.Keys.Any(p => !package.Scope.Contains(p, StringComparer.Ordinal))) throw new UnauthorizedAccessException("Unauthorized workspace modification.");
        return files.ToDictionary(p => p.Key, p => Encoding.UTF8.GetString(p.Value));
    }
    public DevelopmentProposal Stage(DevelopmentPlanPackage package, string taskId, string workspace, SourceChange[] changes)
    {
        ValidateWorkspace(workspace); ReadScope(package, workspace);
        if (changes.Length is < 1 or > 32 || changes.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Length) throw new InvalidDataException("Invalid source change list.");
        // Validate the complete response before making any staged edits.
        foreach (var change in changes)
        {
            if (WorkflowFiles.Normalize(workspace, change.Path) != change.Path || !package.Scope.Contains(change.Path, StringComparer.Ordinal)) throw new UnauthorizedAccessException("Source path is outside the approved scope.");
            if (Encoding.UTF8.GetByteCount(change.Content) > 100_000) throw new InvalidDataException("Source change too large.");
        }
        var baseline = WorkflowFiles.Snapshot(package.TargetRoot);
        var manifest = new List<FileChangeManifest>(); var diff = new StringBuilder();
        foreach (var change in changes.OrderBy(c => c.Path, StringComparer.Ordinal))
        {
            var bytes = Encoding.UTF8.GetBytes(change.Content); baseline.TryGetValue(change.Path, out var before);
            var target = new WorkspaceReader(workspace).Resolve(change.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes);
            manifest.Add(new(change.Path, before == null ? null : WorkflowHash.Bytes(before), WorkflowHash.Bytes(bytes), bytes.Length));
            diff.Append(WorkflowFiles.Diff(change.Path, before, bytes));
        }
        var proposal = new DevelopmentProposal(Guid.NewGuid(), package.Hash, taskId, workspace, manifest.ToArray(), diff.ToString(), "");
        proposal = proposal with { Hash = WorkflowHash.Proposal(proposal) };
        Validate(package, proposal); return proposal;
    }
    public void Validate(DevelopmentPlanPackage package, DevelopmentProposal proposal)
    {
        if (proposal.PlanHash != package.Hash || WorkflowHash.Proposal(proposal) != proposal.Hash) throw new InvalidDataException("Proposal content hash mismatch.");
        if (package.Plan.Tasks.SingleOrDefault(t => t.Id == proposal.TaskId)?.Role != AgentRoles.Developer
            || proposal.Files.Length is < 1 or > 32 || proposal.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != proposal.Files.Length
            || proposal.Files.Any(f => !package.Scope.Contains(f.Path, StringComparer.Ordinal) || WorkflowFiles.Normalize(proposal.Workspace, f.Path) != f.Path
                || f.BeforeHash != package.Baseline.SingleOrDefault(b => b.Path == f.Path)?.Hash || f.Bytes is < 0 or > 100_000))
            throw new UnauthorizedAccessException("Manifest exceeds the approved task or file scope.");
        ValidateSources(package, proposal);
        if (WorkflowHash.Object(WorkflowFiles.Digests(WorkflowFiles.Snapshot(package.TargetRoot))) != WorkflowHash.Object(package.Baseline)) throw new InvalidDataException("Target baseline is stale.");
    }
    public void ValidateSources(DevelopmentPlanPackage package, DevelopmentProposal proposal)
    {
        ValidateWorkspace(proposal.Workspace);
        var outputs = package.Scope.Where(p => p.EndsWith(".csproj", StringComparison.Ordinal)).SelectMany(p => {
            var parent = Path.GetDirectoryName(p)?.Replace('\\', '/'); var prefix = string.IsNullOrEmpty(parent) ? "" : parent + "/";
            return new[] { prefix + "bin", prefix + "obj" };
        }).Append("TestResults").ToHashSet(StringComparer.Ordinal);
        var files = WorkflowFiles.Snapshot(proposal.Workspace, outputs);
        if (files.Keys.Any(p => !package.Scope.Contains(p, StringComparer.Ordinal))) throw new UnauthorizedAccessException("Unauthorized staged file detected.");
        foreach (var path in package.Scope)
        {
            var expected = proposal.Files.SingleOrDefault(f => f.Path == path)?.AfterHash ?? package.Baseline.SingleOrDefault(f => f.Path == path)?.Hash;
            var actual = files.TryGetValue(path, out var bytes) ? WorkflowHash.Bytes(bytes) : null;
            if (actual != expected) throw new InvalidDataException("Staged source changed after proposal creation.");
        }
    }
}
