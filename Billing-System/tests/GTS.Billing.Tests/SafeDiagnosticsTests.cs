using System.IO;
using GTS.Billing.Presentation.Wpf;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GTS.Billing.Tests;

internal sealed class DiagnosticsFixture : IDisposable
{
    private readonly IDisposable scope;
    internal string DirectoryPath { get; } = Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "stage351-diagnostics", Guid.NewGuid().ToString("N"));
    internal DiagnosticsFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        SafeDiagnostics.SetTestProcessDirectory(DirectoryPath);
        scope = SafeDiagnostics.UseTestDirectory(DirectoryPath);
    }
    public void Dispose() => scope.Dispose();
}

[CollectionDefinition("Bounded diagnostic timing", DisableParallelization = true)]
public sealed class BoundedDiagnosticTimingCollection { }

// These assertions exercise a 500 ms best-effort writer, not CPU/thread-pool saturation.
[Collection("Bounded diagnostic timing")]
public sealed class SafeDiagnosticsTests
{
    [Fact]
    public async Task StalledWriteHasBoundedCallerLatencyAndOneOutstandingAttempt()
    {
        using var fixture = new DiagnosticsFixture();
        using var finish = new ManualResetEventSlim();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var scope = SafeDiagnostics.UseTestDirectory(fixture.DirectoryPath, (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            entered.TrySetResult();
            finish.Wait();
            completed.TrySetResult();
        });
        try
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal(DiagnosticWriteResult.IoFailure, SafeDiagnostics.Record("startup-failed", "Error"));
            Assert.Equal(DiagnosticWriteResult.IoFailure, SafeDiagnostics.Record("shutdown-drain-failed", "Error"));
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(3));
            // Scheduling the writer is distinct from bounding the caller's latency.
            // Heavy parallel tests can legitimately delay Task.Run beyond the 500 ms wait.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, attempts);
        }
        finally { finish.Set(); }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WriteOutcomesAreObservableAndNeverCreateMissingDirectory()
    {
        using var fixture = new DiagnosticsFixture();
        Assert.Equal(DiagnosticWriteResult.Written, SafeDiagnostics.Record("shell-started", "Information"));
        var missing = Path.Combine(fixture.DirectoryPath, "absent");
        using (SafeDiagnostics.UseTestDirectory(missing))
        {
            Assert.Equal(DiagnosticWriteResult.Unavailable, SafeDiagnostics.Record("startup-failed", "Error"));
            Assert.False(Directory.Exists(missing));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailureIsBoundedAndDoesNotRecurse(bool accessDenied)
    {
        using var fixture = new DiagnosticsFixture();
        var attempts = 0;
        using var scope = SafeDiagnostics.UseTestDirectory(fixture.DirectoryPath, (_, _) =>
        {
            attempts++;
            if (accessDenied) throw new UnauthorizedAccessException("Synthetic sensitive payload");
            throw new IOException("Synthetic full disk");
        });
        Assert.Equal(accessDenied ? DiagnosticWriteResult.AccessDenied : DiagnosticWriteResult.IoFailure,
            SafeDiagnostics.Record("logout-audit-failed", "Warning"));
        Assert.Equal(1, attempts);
        Assert.Equal(accessDenied ? DiagnosticWriteResult.AccessDenied : DiagnosticWriteResult.IoFailure, SafeDiagnostics.LastOutcome);
    }

    [Fact]
    public void MessagesExceptionsPathsAndUnknownEventTextAreExcluded()
    {
        using var fixture = new DiagnosticsFixture();
        var secret = "SyntheticSecret-" + Guid.NewGuid().ToString("N");
        using var provider = new SafeDiagnostics();
        provider.CreateLogger(secret).Log(LogLevel.Error, new EventId(351), secret, new IOException(secret), (s, _) => s);
        SafeDiagnostics.Record(secret, secret);
        var text = File.ReadAllText(Path.Combine(fixture.DirectoryPath, "events.jsonl"));
        Assert.DoesNotContain(secret, text);
        Assert.Contains("event-351", text);
        Assert.Contains("diagnostic-event", text);
    }

    [Fact]
    public void OperationalDestinationHasNoDatabaseOrExecutableDependency()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GTS Billing", "Logs"),
            SafeDiagnostics.OperationalDirectory);
    }
}
