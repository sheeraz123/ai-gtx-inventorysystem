using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Infrastructure;

// Not a configurable boolean: adding a verified backend requires trusted implementation code and confinement tests.
public sealed class UnavailableExecutionIsolation : IExecutionIsolation
{
    public Task<IsolationStatus> VerifyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new IsolationStatus(false, "No OS-isolated execution backend has been verified. Live Developer and untrusted build/test execution are disabled."));
    }
}
public sealed class UnavailableIsolatedCommandExecutor : IIsolatedCommandExecutor
{
    // Future backend must implement these fixed templates in a secret-free, network-denied OS sandbox.
    public static IReadOnlyList<string> BuildArguments { get; } = Array.AsReadOnly(new[] { "build", "Sample.slnx", "--no-restore", "--configuration", "Release" });
    public static IReadOnlyList<string> TestArguments { get; } = Array.AsReadOnly(new[] { "test", "tests/Sample.Tests/Sample.Tests.csproj", "--no-build", "--no-restore", "--configuration", "Release", "--logger", "trx", "--results-directory", "TestResults" });
    public Task<CommandEvidence> ExecuteAsync(string workspace, TestCommand command, CancellationToken cancellationToken) => throw new UnauthorizedAccessException("No verified OS sandbox; no command started.");
    public Task<TestEvidence> ReadEvidenceAsync(string workspace, CommandEvidence[] commands, CancellationToken cancellationToken) => throw new UnauthorizedAccessException("No test execution evidence exists.");
}
