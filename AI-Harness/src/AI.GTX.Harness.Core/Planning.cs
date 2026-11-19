using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.GTX.Harness.Core;

public enum ProviderError { None, Unavailable, Authentication, Unsupported, WorkspaceDenied, TimedOut, Canceled, OutputLimit, Transient, ExecutionFailed, MalformedResponse, PolicyViolation }
public sealed record CodexAvailability(bool Available, ProviderError Error, string Version);
public sealed record CodexExecutionResult(ProviderError Error, DevelopmentPlan? Plan = null, int? ExitCode = null, string Diagnostics = "");
public interface ICodexExecutionProvider
{
    Task<CodexAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
    Task<CodexExecutionResult> PlanAsync(string goal, string model, CancellationToken cancellationToken);
}
public sealed class DevelopmentPlan
{
    public required string Title { get; init; }
    public required string Goal { get; init; }
    public required PlanTask[] Tasks { get; init; }
    public required string[] Assumptions { get; init; }
    public required string[] Questions { get; init; }
}
public sealed class PlanTask
{
    public required string Id { get; init; }
    public required string Role { get; init; }
    public required string Description { get; init; }
    public required string[] Dependencies { get; init; }
    public required string[] AcceptanceCriteria { get; init; }
}
public static class PlanCodec
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true
    };
    public static DevelopmentPlan Parse(string json)
    {
        if (json.Length > 100_000) throw new InvalidDataException("Plan exceeds size limit.");
        var plan = JsonSerializer.Deserialize<DevelopmentPlan>(json, Json) ?? throw new InvalidDataException("Plan is missing.");
        Validate(plan); return plan;
    }
    public static void Validate(DevelopmentPlan plan)
    {
        static bool Text(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Length <= max;
        if (!Text(plan.Title, 200) || !Text(plan.Goal, 5000) || plan.Tasks is not { Length: > 0 and <= 32 }
            || plan.Assumptions == null || plan.Questions == null || plan.Assumptions.Length > 32 || plan.Questions.Length > 32
            || plan.Assumptions.Concat(plan.Questions).Any(s => !Text(s, 2000))) throw new InvalidDataException("Incomplete plan.");
        if (plan.Tasks.Any(t => t == null || !Text(t.Id, 64) || !System.Text.RegularExpressions.Regex.IsMatch(t.Id, "^[a-zA-Z0-9_-]+$")
            || !AgentRoles.All.Contains(t.Role) || !Text(t.Description, 4000) || t.Dependencies == null
            || t.Dependencies.Length > 32 || t.AcceptanceCriteria is not { Length: > 0 and <= 16 }
            || t.AcceptanceCriteria.Any(s => !Text(s, 2000)))) throw new InvalidDataException("Incomplete task.");
        var ids = plan.Tasks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != plan.Tasks.Length || plan.Tasks.Any(t => t.Dependencies.Any(d => !ids.Contains(d)))) throw new InvalidDataException("Invalid task dependencies.");
        var visited = new HashSet<string>();
        while (visited.Count < ids.Count)
        {
            var ready = plan.Tasks.Where(t => !visited.Contains(t.Id) && t.Dependencies.All(visited.Contains)).ToArray();
            if (ready.Length == 0) throw new InvalidDataException("Dependency cycle in plan.");
            foreach (var task in ready) visited.Add(task.Id);
        }
    }
    public const string Schema = """
    {"type":"object","additionalProperties":false,"required":["title","goal","tasks","assumptions","questions"],"properties":{
      "title":{"type":"string"},"goal":{"type":"string"},
      "assumptions":{"type":"array","items":{"type":"string"}},"questions":{"type":"array","items":{"type":"string"}},
      "tasks":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","role","description","dependencies","acceptanceCriteria"],"properties":{
        "id":{"type":"string"},"role":{"type":"string","enum":["planner","architect","developer","tester","reviewer","documentation"]},"description":{"type":"string"},
        "dependencies":{"type":"array","items":{"type":"string"}},"acceptanceCriteria":{"type":"array","items":{"type":"string"}}
      }}}
    }}
    """;
}
