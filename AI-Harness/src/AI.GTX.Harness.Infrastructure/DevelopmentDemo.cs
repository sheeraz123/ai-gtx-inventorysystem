using AI.GTX.Harness.Core;
using AI.GTX.Harness.Tools;

namespace AI.GTX.Harness.Infrastructure;

public sealed class DevelopmentDemo(ICodexExecutionProvider planner, IWorkflowStore store, string sampleRoot)
{
    public async Task<DevelopmentPlanPackage> CreateAsync(CancellationToken ct)
    {
        // Trusted, deterministic baseline fixture, not generated code and not application source.
        var target = new WorkspaceReader(sampleRoot).Resolve(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        var baseline = new Dictionary<string, string> {
            ["Sample.slnx"] = "<Solution><Project Path=\"src/Sample/Sample.csproj\" /><Project Path=\"tests/Sample.Tests/Sample.Tests.csproj\" /></Solution>",
            ["src/Sample/Sample.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>",
            ["src/Sample/Greeting.cs"] = "namespace Sample; public static class Greeting { public static string Format(string? name) => throw new System.NotImplementedException(); }\n",
            ["tests/Sample.Tests/Sample.Tests.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/Sample/Sample.csproj\" /><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" /><PackageReference Include=\"xunit\" Version=\"2.9.3\" /><PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\" /></ItemGroup></Project>",
            ["tests/Sample.Tests/GreetingTests.cs"] = "using Xunit; namespace Sample.Tests; public class GreetingTests { [Fact] public void PendingImplementation() => Assert.True(true); }\n"
        };
        foreach (var file in baseline)
        {
            var path = new WorkspaceReader(target).Resolve(file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = new FileStream(path, FileMode.CreateNew); await using var writer = new StreamWriter(output); await writer.WriteAsync(file.Value.AsMemory(), ct);
        }
        var goal = "Create a plan for a DISPOSABLE sample, not Billing. Return exactly three tasks: D1 role developer with no dependencies, T1 role tester depending on D1, R1 role reviewer depending on T1. "
            + "The developer implements Sample.Greeting.Format(string? name): trim surrounding whitespace; return Hello, <trimmed name>! for a nonblank name; return Hello! for null, empty or whitespace. "
            + "Tests cover ordinary, trimmed, null, empty, whitespace and Unicode names. Review checks correctness, security and test evidence. "
            + "Acceptance criteria must be explicit. Scoped files are Sample.slnx, src/Sample/Sample.csproj, src/Sample/Greeting.cs, tests/Sample.Tests/Sample.Tests.csproj and tests/Sample.Tests/GreetingTests.cs. "
            + "Only produce tasks. Do not use tools or implement code. No extra tasks or invented business requirements.";
        var result = await planner.PlanAsync(goal, "default", ct);
        if (result.Error != ProviderError.None || result.Plan == null) throw new InvalidOperationException("Planner unavailable; no plan approval or execution occurred.");
        if (result.Plan.Tasks.Length != 3 || result.Plan.Tasks.Count(t => t.Role == AgentRoles.Developer) != 1
            || result.Plan.Tasks.Count(t => t.Role == AgentRoles.Tester) != 1 || result.Plan.Tasks.Count(t => t.Role == AgentRoles.Reviewer) != 1) throw new InvalidDataException("Sample plan must use the supported three-role slice.");
        return store.Create(result.Plan, target, baseline.Keys.ToArray());
    }
}
