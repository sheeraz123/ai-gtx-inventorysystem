using AI.GTX.Harness.Core;
using AI.GTX.Harness.Tools;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class ChangeProposalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gtx-proposal-test-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(root, "target"); private string Stage => Path.Combine(root, "staging");
    [Fact] public async Task PreviewAndDeniedApprovalDoNotModifyTarget()
    {
        Directory.CreateDirectory(Target); await File.WriteAllTextAsync(Path.Combine(Target, "a.cs"), "old");
        var service = new ChangeProposalService(Target, Stage); var proposal = await service.PrepareAsync("a.cs", "new", default);
        Assert.Contains("-old", proposal.Diff); Assert.Contains("+new", proposal.Diff);
        Assert.False(await service.ApplyAsync(proposal.Id, new DenyApprovalService(), default));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(Target, "a.cs")));
    }
    [Fact] public async Task ExplicitApprovalAppliesOnlyReviewedCandidate()
    {
        var service = new ChangeProposalService(Target, Stage); var proposal = await service.PrepareAsync("new.cs", "reviewed", default);
        Assert.False(Directory.Exists(Target)); var approval = new Approval();
        Assert.True(await service.ApplyAsync(proposal.Id, approval, default)); Assert.Contains(proposal.ContentHash, approval.Operation!);
        Assert.Equal("reviewed", await File.ReadAllTextAsync(Path.Combine(Target, "new.cs")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(proposal.Id, approval, default));
    }
    [Fact] public async Task ChangedTargetCannotBeOverwritten()
    {
        Directory.CreateDirectory(Target); var path = Path.Combine(Target, "a.cs"); await File.WriteAllTextAsync(path, "old");
        var service = new ChangeProposalService(Target, Stage); var proposal = await service.PrepareAsync("a.cs", "new", default); await File.WriteAllTextAsync(path, "external edit");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(proposal.Id, new Approval(), default));
        Assert.Equal("external edit", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task TamperedStagingIsRejected()
    {
        var service = new ChangeProposalService(Target, Stage); var proposal = await service.PrepareAsync("a.cs", "reviewed", default);
        await File.WriteAllTextAsync(Path.Combine(Stage, proposal.Id.ToString("N") + ".txt"), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(proposal.Id, new Approval(), default));
        Assert.False(Directory.Exists(Target));
    }
    [Theory] [InlineData("../escape.cs")] [InlineData(".git/config")] [InlineData("a.cs:stream")]
    public async Task EscapingPathsAreRejected(string path) => await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ChangeProposalService(Target, Stage).PrepareAsync(path, "bad", default));
    [Fact] public void OverlappingRootsAreRejected() => Assert.Throws<ArgumentException>(() => new ChangeProposalService(Target, Path.Combine(Target, "staging")));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Approval : IApprovalService
    {
        public string? Operation;
        public Task<bool> ApproveAsync(string operation, CancellationToken ct) { Operation = operation; return Task.FromResult(true); }
    }
}
