using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Agents;

public sealed class DynamicAgentFactory : IAgentFactory
{
    private readonly Dictionary<string, AgentRegistration> registrations;
    private readonly Dictionary<string, AgentDefinition> definitions;
    public DynamicAgentFactory(IEnumerable<AgentRegistration> registrations, IEnumerable<AgentDefinition> definitions)
    {
        this.registrations = registrations.ToDictionary(r => r.Role, StringComparer.Ordinal);
        this.definitions = definitions.ToDictionary(d => d.Role, d => d.Snapshot(), StringComparer.Ordinal);
        foreach (var definition in this.definitions.Values)
        {
            if (!this.registrations.TryGetValue(definition.Role, out var registration)) throw new ArgumentException("Unregistered agent role in configuration.");
            if (definition.MaxAttempts is < 1 or > 3) throw new ArgumentException("Agent attempts must be between 1 and 3.");
            if (string.IsNullOrWhiteSpace(definition.Provider) || string.IsNullOrWhiteSpace(definition.Model)) throw new ArgumentException("Provider and model must be specified.");
            if (definition.Permissions.Any(p => !registration.MaximumPermissions.Contains(p))) throw new ArgumentException("Agent configuration exceeds role permissions.");
        }
    }
    public AgentInstance Create(string role)
    {
        if (!definitions.TryGetValue(role, out var definition) || !definition.Enabled) throw new UnauthorizedAccessException("Agent is not enabled by configuration.");
        var agent = registrations[role].Create();
        if (agent.Role != role) throw new InvalidOperationException("Agent registration role mismatch.");
        return new(agent, definition.Snapshot());
    }
}
