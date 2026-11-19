using System.Security.Cryptography;
using System.Text;
using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Tools;

// A deliberately single-file transaction. Proposals live separately; nothing applies automatically.
public sealed class ChangeProposalService : IChangeProposalService
{
    private sealed record Staged(ChangeProposal Proposal, string StagedPath, string? OriginalHash);
    private readonly string targetRoot;
    private readonly string stagingRoot;
    private readonly Dictionary<Guid, Staged> proposals = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    public ChangeProposalService(string targetRoot, string stagingRoot)
    {
        this.targetRoot = Path.GetFullPath(targetRoot); this.stagingRoot = Path.GetFullPath(stagingRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (this.targetRoot.Equals(this.stagingRoot, comparison) || this.stagingRoot.StartsWith(this.targetRoot + Path.DirectorySeparatorChar, comparison)
            || this.targetRoot.StartsWith(this.stagingRoot + Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Staging and target roots must be separate.");
        new WorkspaceReader(this.targetRoot).Resolve("."); new WorkspaceReader(this.stagingRoot).Resolve(".");
    }
    public async Task<ChangeProposal> PrepareAsync(string relativePath, string content, CancellationToken ct)
    {
        if (content.Length > 100_000 || relativePath.Contains('\n') || relativePath.Contains('\r')) throw new ArgumentException("Invalid proposal.");
        await gate.WaitAsync(ct);
        try
        {
            var reader = new WorkspaceReader(targetRoot); var target = reader.Resolve(relativePath);
            byte[]? originalBytes = null;
            if (File.Exists(target))
            {
                await using var originalFile = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (originalFile.Length > 100_000) throw new InvalidDataException("Original file too large.");
                using var snapshot = new MemoryStream(); await originalFile.CopyToAsync(snapshot, ct); originalBytes = snapshot.ToArray();
            }
            string? original = null;
            if (originalBytes != null) { using var text = new StreamReader(new MemoryStream(originalBytes)); original = await text.ReadToEndAsync(ct); }
            var originalHash = originalBytes == null ? null : Hash(originalBytes);
            var bytes = Encoding.UTF8.GetBytes(content); if (bytes.Length > 100_000) throw new ArgumentException("Proposal too large.");
            Directory.CreateDirectory(stagingRoot);
            var id = Guid.NewGuid(); var stage = new WorkspaceReader(stagingRoot).Resolve(id.ToString("N") + ".txt");
            await using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write)) await file.WriteAsync(bytes, ct);
            var diff = Diff(relativePath, original, content);
            var proposal = new ChangeProposal(id, relativePath, Hash(bytes), diff);
            proposals[id] = new(proposal, stage, originalHash);
            return proposal;
        }
        finally { gate.Release(); }
    }
    public async Task<bool> ApplyAsync(Guid proposalId, IApprovalService approval, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!proposals.TryGetValue(proposalId, out var staged)) throw new InvalidOperationException("Unknown or already applied proposal.");
            var p = staged.Proposal;
            if (!await approval.ApproveAsync($"Apply proposal {p.Id} SHA256 {p.ContentHash} to {Path.Combine(targetRoot, p.RelativePath)}\n{p.Diff}", ct)) return false;
            ct.ThrowIfCancellationRequested();
            var target = new WorkspaceReader(targetRoot).Resolve(p.RelativePath);
            var stage = new WorkspaceReader(stagingRoot).Resolve(Path.GetFileName(staged.StagedPath));
            if (new FileInfo(stage).Length > 100_000) throw new InvalidDataException("Staging size changed.");
            var bytes = await File.ReadAllBytesAsync(stage, ct);
            if (Hash(bytes) != p.ContentHash) throw new InvalidDataException("Staged proposal changed after review.");
            if (File.Exists(target))
            {
                // FileShare.None prevents cooperating writers from changing an existing target during validation and write.
                await using var file = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (file.Length > 100_000) throw new InvalidDataException("Target size changed.");
                using var snapshot = new MemoryStream(); await file.CopyToAsync(snapshot, ct);
                if (Hash(snapshot.ToArray()) != staged.OriginalHash) throw new InvalidDataException("Target changed since proposal creation.");
                ct.ThrowIfCancellationRequested(); file.Position = 0;
                // Do not observe cancellation after starting the one-file commit.
                await file.WriteAsync(bytes, CancellationToken.None); file.SetLength(bytes.Length); await file.FlushAsync(CancellationToken.None);
            }
            else
            {
                if (staged.OriginalHash != null) throw new InvalidDataException("Original target was removed.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await file.WriteAsync(bytes, CancellationToken.None);
            }
            proposals.Remove(proposalId); return true;
        }
        finally { gate.Release(); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Diff(string path, string? before, string after)
    {
        static string[] Lines(string text)
        {
            if (text.Length == 0) return [];
            var normalized = text.Replace("\r\n", "\n");
            return (normalized.EndsWith('\n') ? normalized[..^1] : normalized).Split('\n');
        }
        var oldLines = Lines(before ?? ""); var newLines = Lines(after);
        return $"--- {(before == null ? "/dev/null" : "a/" + path)}\n+++ b/{path}\n@@ -{(oldLines.Length == 0 ? 0 : 1)},{oldLines.Length} +{(newLines.Length == 0 ? 0 : 1)},{newLines.Length} @@\n"
            + string.Join('\n', oldLines.Select(l => "-" + l)) + (oldLines.Length > 0 ? "\n" : "")
            + (before is { Length: > 0 } && !before.EndsWith('\n') ? "\\ No newline at end of file\n" : "")
            + string.Join('\n', newLines.Select(l => "+" + l)) + (newLines.Length > 0 ? "\n" : "")
            + (after.Length > 0 && !after.EndsWith('\n') ? "\\ No newline at end of file\n" : "")
            + "# Candidate is UTF-8; line-ending bytes are bound by the displayed SHA256.\n";
    }
}
