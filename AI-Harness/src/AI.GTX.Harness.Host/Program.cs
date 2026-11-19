using AI.GTX.Harness.Core;
using AI.GTX.Harness.Infrastructure;
using AI.GTX.Harness.Infrastructure.Codex;
using AI.GTX.Harness.Tools;
using AI.GTX.Harness.Orchestrator;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

if (args is ["--help"])
{
    Console.WriteLine("Usage: --self-check | --run \"task\" | --codex-check | --plan \"goal\" | --propose <relative-target> <candidate-file>\nMilestone 3: --m3-check | --m3-demo | --m3-status <id> <revision> | --m3-plan-decision <id> <revision> | --m3-run <id> <revision> | --m3-apply <id> <revision> | --m3-cancel <id> <revision>\nPlan and changes require separate explicit decisions. Live Developer/build/test remain blocked until OS isolation is verified.");
    return 0;
}
var milestone3 = args is ["--m3-demo"] or ["--m3-check"] || (args.Length == 3 && args[0] is "--m3-status" or "--m3-plan-decision" or "--m3-run" or "--m3-apply" or "--m3-cancel");
if (args.Length > 0 && !milestone3 && args is not ["--self-check"] && args is not ["--codex-check"] && args is not ["--run", _] && args is not ["--plan", _] && args is not ["--propose", _, _])
{
    Console.Error.WriteLine("Invalid arguments. Use --help.");
    return 1;
}
try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
    builder.Configuration.AddJsonFile("config/harness.json", optional: false, reloadOnChange: false)
        .AddJsonFile("config/codex.json", optional: false, reloadOnChange: false)
        .AddEnvironmentVariables("GTX_");
    if (args is ["--plan", _])
    {
        var plannerKey = builder.Configuration.GetSection("Harness:Agents").GetChildren()
            .Single(section => section["Role"] == AgentRoles.Planner).Key;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            [$"Harness:Agents:{plannerKey}:Provider"] = "codex-cli", [$"Harness:Agents:{plannerKey}:Model"] = builder.Configuration["Planning:Model"] ?? "default",
            [$"Harness:Agents:{plannerKey}:MaxAttempts"] = builder.Configuration["Planning:MaxAttempts"] ?? "2", ["Harness:TimeoutSeconds"] = "600"
        });
    }
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; o.UseUtcTimestamp = true; });
    builder.Services.AddGtxHarness(builder.Configuration, AppContext.BaseDirectory);
    using var host = builder.Build();
    // Resolve options and registry even on a self-check so invalid configuration cannot appear healthy.
    var settings = host.Services.GetRequiredService<IOptions<HarnessOptions>>().Value;
    var factory = host.Services.GetRequiredService<IAgentFactory>();
    foreach (var agent in settings.Agents.Where(a => a.Enabled)) factory.Create(agent.Role);
    host.Services.GetRequiredService<IToolGateway>();
    if (args.Length == 0 || args is ["--self-check"])
    {
        Console.WriteLine("Foundation ready: configuration, DI, agent registry and read-only tool gateway validated. AI execution is not configured.");
        return 0;
    }
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    if (milestone3)
    {
        if (args is ["--m3-check"])
        {
            var boundary = await host.Services.GetRequiredService<IExecutionIsolation>().VerifyAsync(cancellation.Token);
            Console.WriteLine($"OS isolation verified: {boundary.Verified}. {boundary.Detail}"); return boundary.Verified ? 0 : 4;
        }
        var store = host.Services.GetRequiredService<IWorkflowStore>();
        var controller = host.Services.GetRequiredService<DevelopmentWorkflowController>();
        var secrets = host.Services.GetRequiredService<SensitiveDataRedactor>();
        DevelopmentPlanPackage package;
        if (args[0] == "--m3-demo") package = await host.Services.GetRequiredService<DevelopmentDemo>().CreateAsync(cancellation.Token);
        else package = store.Load(Guid.Parse(args[1]), int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        var progress = args[0] switch
        {
            "--m3-plan-decision" => await controller.DecidePlanAsync(package.Id, package.Revision, new ConsoleWorkflowApproval(secrets), Environment.UserName, cancellation.Token),
            "--m3-run" => await controller.ExecuteAsync(package.Id, package.Revision, cancellation.Token),
            "--m3-apply" => await controller.DecideChangesAsync(package.Id, package.Revision, new ConsoleWorkflowApproval(secrets), Environment.UserName, cancellation.Token),
            "--m3-cancel" => controller.Cancel(package.Id, package.Revision),
            _ => store.ReadProgress(package)
        };
        Console.WriteLine(secrets.Redact(JsonSerializer.Serialize(new { package, progress }, PlanCodec.Json)));
        Console.WriteLine($"Workflow {package.Id}, revision {package.Revision}: {progress.State}.");
        var isolation = await host.Services.GetRequiredService<IExecutionIsolation>().VerifyAsync(cancellation.Token);
        Console.WriteLine(isolation.Detail);
        return progress.State is DevelopmentState.Failed or DevelopmentState.RecoveryRequired ? 1 : progress.State == DevelopmentState.Blocked ? 4 : 0;
    }
    if (args is ["--codex-check"])
    {
        var availability = await host.Services.GetRequiredService<ICodexExecutionProvider>().CheckAvailabilityAsync(cancellation.Token);
        Console.WriteLine($"Codex: available={availability.Available}; error={availability.Error}; version={availability.Version}");
        return availability.Available ? 0 : 4;
    }
    if (args is ["--propose", var relativeTarget, var candidate])
    {
        if (new FileInfo(candidate).Length > 100_000) throw new InvalidDataException("Candidate exceeds limit.");
        var content = await File.ReadAllTextAsync(candidate, cancellation.Token);
        var secrets = host.Services.GetRequiredService<SensitiveDataRedactor>();
        if (content != secrets.Redact(content)) throw new InvalidDataException("Candidate contains sensitive values.");
        var targetRoot = Path.GetFullPath(builder.Configuration["Changes:Workspace"] ?? "workspace/approved-changes", AppContext.BaseDirectory);
        var stagingRoot = Path.GetFullPath(builder.Configuration["Changes:Staging"] ?? "workspace/change-proposals", AppContext.BaseDirectory);
        var proposals = new ChangeProposalService(targetRoot, stagingRoot);
        var proposal = await proposals.PrepareAsync(relativeTarget, content, cancellation.Token);
        var applied = await proposals.ApplyAsync(proposal.Id, new ConsoleChangeApproval(proposal.ContentHash, secrets), cancellation.Token);
        Console.WriteLine(applied ? "Approved proposal applied to the configured change workspace." : "Proposal not applied; staged candidate retained.");
        return applied ? 0 : 3;
    }
    var manager = host.Services.GetRequiredService<ITaskManager>();
    var planning = args[0] == "--plan";
    var task = planning ? manager.EnqueuePlan(args[1]) : manager.Enqueue(args[1]);
    var result = await manager.RunNextAsync(cancellation.Token);
    // Do not print raw task descriptions, provider text, exceptions or configuration values.
    Console.WriteLine($"Task {task.Id}: {result!.State}. {(planning ? "Planner-only workflow; no source changes applied." : "Default workflow uses configured role providers.")}");
    foreach (var step in result.Result!.Steps) Console.WriteLine($"{step.Id}: {step.State}; attempts={step.Attempts}; error={step.Error}");
    if (planning && result.Result.Steps.Single().Plan is { } plan)
    {
        var json = JsonSerializer.Serialize(plan, PlanCodec.Json);
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "workspace", "plans");
        new WorkspaceReader(outputRoot).Resolve("."); Directory.CreateDirectory(outputRoot);
        var output = new WorkspaceReader(outputRoot).Resolve(task.Id.ToString("N") + ".json");
        await using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
        await using (var writer = new StreamWriter(file)) await writer.WriteAsync(json);
        Console.WriteLine(json); Console.WriteLine($"Reviewable plan: {output}");
    }
    return result.State switch { ExecutionState.Completed => 0, ExecutionState.Canceled => 2, ExecutionState.NotConfigured => 4, _ => 1 };
}
catch (OperationCanceledException) { Console.Error.WriteLine("Execution canceled."); return 2; }
catch (Exception ex) { Console.Error.WriteLine($"Harness startup failed ({ex.GetType().Name}); inspect configuration. Sensitive details omitted."); return 1; }

sealed class ConsoleChangeApproval(string hash, SensitiveDataRedactor secrets) : IApprovalService
{
    public async Task<bool> ApproveAsync(string operation, CancellationToken cancellationToken)
    {
        Console.WriteLine(secrets.Redact(operation));
        Console.WriteLine($"Type APPLY {hash} to approve this exact proposal; any other input declines:");
        return await Console.In.ReadLineAsync(cancellationToken) == "APPLY " + hash;
    }
}
sealed class ConsoleWorkflowApproval(SensitiveDataRedactor secrets) : IApprovalService
{
    public async Task<bool> ApproveAsync(string operation, CancellationToken ct)
    {
        Console.WriteLine(secrets.Redact(operation));
        var subject = operation.Split('\n')[0];
        Console.WriteLine($"Type APPROVE {subject} to record this decision, or anything else to reject:");
        return await Console.In.ReadLineAsync(ct) == "APPROVE " + subject;
    }
}
