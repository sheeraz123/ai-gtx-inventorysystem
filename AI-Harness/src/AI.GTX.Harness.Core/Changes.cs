namespace AI.GTX.Harness.Core;

public sealed record ChangeProposal(Guid Id, string RelativePath, string ContentHash, string Diff);
public interface IChangeProposalService
{
    Task<ChangeProposal> PrepareAsync(string relativePath, string content, CancellationToken cancellationToken);
    Task<bool> ApplyAsync(Guid proposalId, IApprovalService approval, CancellationToken cancellationToken);
}
