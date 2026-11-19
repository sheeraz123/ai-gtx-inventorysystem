using AI.GTX.Harness.Core;
using AI.GTX.Harness.Tools;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class ToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gtx-foundation-" + Guid.NewGuid().ToString("N"));
    public ToolTests() => Directory.CreateDirectory(root);
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    [InlineData(".env")]
    [InlineData("nested/secrets.json")]
    [InlineData("appsettings.Local.json")]
    [InlineData("file.txt:stream")]
    [InlineData(".git./config")]
    [InlineData(".ssh /id")]
    [InlineData("NUL")]
    public void ProtectedOrEscapingPathsAreDenied(string path) => Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceReader(root).Resolve(path));
    [Fact]
    public void AbsolutePathsAreDenied() => Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceReader(root).Resolve(root));
    [Fact]
    public async Task ReadsAndListsOnlyWorkspaceFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "code.cs"), "public class Example {}");
        await File.WriteAllTextAsync(Path.Combine(root, ".env"), "DO_NOT_LIST");
        var gateway = new FoundationToolGateway(new ToolPolicy(), new(root));
        var agent = Fixture.Definition("planner", 1, ToolCapability.ReadFile, ToolCapability.ListFiles);
        var result = await gateway.ExecuteAsync(agent, new(ToolCapability.ReadFile, "code.cs"), default);
        Assert.True(result.Executed); Assert.Contains("class Example", result.Message);
        var listing = await gateway.ExecuteAsync(agent, new(ToolCapability.ListFiles, "."), default);
        Assert.Contains("code.cs", listing.Message); Assert.DoesNotContain(".env", listing.Message);
    }
    [Fact]
    public async Task RejectsOversizedFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "large.txt"), new string('x', 100_001));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkspaceReader(root).ReadAsync("large.txt", default));
    }
    [Theory]
    [InlineData(ToolCapability.StageChange)]
    [InlineData(ToolCapability.DotnetBuild)]
    [InlineData(ToolCapability.DotnetTest)]
    [InlineData(ToolCapability.Delete)]
    [InlineData(ToolCapability.GitMerge)]
    [InlineData(ToolCapability.ProductionDeploy)]
    public async Task HighImpactActionsNeedApprovalAndHaveNoExecutionAdapter(ToolCapability capability)
    {
        var agent = Fixture.Definition("test-role", 1, capability); var policy = new ToolPolicy();
        Assert.Equal(PolicyDecision.RequiresApproval, policy.Evaluate(agent, capability));
        var result = await new FoundationToolGateway(policy, new(root)).ExecuteAsync(agent, new(capability, "unapproved.cs"), default);
        Assert.False(result.Executed); Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }
    [Fact]
    public async Task UnconfiguredPermissionsFailClosed()
    {
        var gateway = new FoundationToolGateway(new ToolPolicy(), new(root));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.ExecuteAsync(Fixture.Definition(), new(ToolCapability.DotnetTest), default));
        Assert.False(await new DenyApprovalService().ApproveAsync("merge", default));
    }
    public void Dispose() => Directory.Delete(root, recursive: true);
}
