using AI.GTX.Harness.Agents;
using AI.GTX.Harness.Core;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class FactoryTests
{
    [Fact]
    public void CreatesNewRegisteredInstancesAndSupportsFutureRole()
    {
        var factory = new DynamicAgentFactory([new("migration", () => Fixture.Success("migration"), new HashSet<ToolCapability>())], [Fixture.Definition("migration")]);
        Assert.Equal("migration", factory.Create("migration").Agent.Role);
        Assert.NotSame(factory.Create("migration").Agent, factory.Create("migration").Agent);
    }
    [Fact]
    public void UnknownAndDisabledRolesAreDenied()
    {
        var definition = Fixture.Definition(); definition.Enabled = false;
        var factory = new DynamicAgentFactory([new("planner", () => Fixture.Success("planner"), new HashSet<ToolCapability>())], [definition]);
        Assert.Throws<UnauthorizedAccessException>(() => factory.Create("planner"));
        Assert.Throws<UnauthorizedAccessException>(() => factory.Create("unknown"));
    }
    [Fact]
    public void ConfigurationCannotEscalateRolePermissions()
    {
        Assert.Throws<ArgumentException>(() => new DynamicAgentFactory(
            [new("planner", () => Fixture.Success("planner"), new HashSet<ToolCapability> { ToolCapability.ReadFile })],
            [Fixture.Definition("planner", 1, ToolCapability.ProductionDeploy)]));
    }
    [Fact]
    public void SnapshotsConfigurationToPreventMutationAfterValidation()
    {
        var original = Fixture.Definition();
        var factory = new DynamicAgentFactory([new("planner", () => Fixture.Success("planner"), new HashSet<ToolCapability>())], [original]);
        original.Enabled = false;
        var created = factory.Create("planner"); created.Definition.Enabled = false;
        Assert.True(factory.Create("planner").Definition.Enabled);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RejectsUnboundedRetryConfiguration(int attempts) => Assert.Throws<ArgumentException>(() => Fixture.Factory([Fixture.Success("planner")], attempts));
    [Fact]
    public void RegistrationRoleMismatchIsRejected()
    {
        var factory = new DynamicAgentFactory([new("planner", () => Fixture.Success("developer"), new HashSet<ToolCapability>())], [Fixture.Definition()]);
        Assert.Throws<InvalidOperationException>(() => factory.Create("planner"));
    }
}
