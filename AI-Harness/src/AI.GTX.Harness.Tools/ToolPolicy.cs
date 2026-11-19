using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Tools;

public sealed class ToolPolicy : IToolPolicy
{
    public PolicyDecision Evaluate(AgentDefinition agent, ToolCapability capability)
    {
        if (!agent.Enabled || !Enum.IsDefined(capability) || !agent.Permissions.Contains(capability)) return PolicyDecision.Denied;
        return capability is ToolCapability.StageChange or ToolCapability.DotnetBuild or ToolCapability.DotnetTest
            or ToolCapability.Delete or ToolCapability.GitMerge or ToolCapability.ProductionDeploy
            ? PolicyDecision.RequiresApproval : PolicyDecision.Allowed;
    }
}
public sealed class DenyApprovalService : IApprovalService
{
    public Task<bool> ApproveAsync(string operation, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(false); }
}
