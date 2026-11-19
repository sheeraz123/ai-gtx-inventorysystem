using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class ApplicationLifetimeTests : IDisposable
{
    private readonly DiagnosticsFixture diagnostics = new();
    public void Dispose() => diagnostics.Dispose();
    [Fact]
    public async Task ClosingMessagesFollowAuthoritativeDrainState()
    {
        using var diagnostics = new DiagnosticsFixture();
        var path = System.IO.Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "unused.db");
        var service = new Application.AuthenticationService(
            new Infrastructure.AuthenticationStore(new Infrastructure.DevelopmentDatabase(new Infrastructure.ConfiguredDatabaseLocation(path))),
            new Infrastructure.Argon2PasswordHasher(), TimeProvider.System);
        var lifetime = new ApplicationLifetime();
        var vm = new AuthenticationViewModel(service, lifetime);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = lifetime.RunAsync(_ => finish.Task);
        lifetime.SessionCleanup = () => Task.CompletedTask;
        var shutdown = lifetime.ShutdownAsync();
        Assert.Equal(AuthenticationPage.Closing, vm.Page);
        Assert.Equal("Waiting for an active operation to finish. GTS Billing will close automatically.", vm.ClosingMessage);
        Assert.False(shutdown.IsCompleted);
        await Assert.ThrowsAsync<OperationCanceledException>(() => lifetime.RunAsync(_ => Task.CompletedTask));
        finish.SetResult();
        await Task.WhenAll(work, shutdown);
        Assert.Equal("Closing GTS Billing, please wait…", vm.ClosingMessage);
    }
    [Fact]
    public async Task ShutdownCancelsButWaitsForOperationDisposalBeforeCleanup()
    {
        var lifetime = new ApplicationLifetime();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var cancelled = false;
        var work = lifetime.RunAsync(async token =>
        {
            using var registration = token.Register(() => cancelled = true);
            ready.SetResult();
            await finish.Task;
            order.Add("transaction-disposed");
        });
        await ready.Task;
        lifetime.SessionCleanup = () => { order.Add("session-cleared"); return Task.CompletedTask; };
        lifetime.HostCleanup = () => { order.Add("host-disposed"); return Task.CompletedTask; };
        lifetime.ShutdownCompleted += () => order.Add("application-stopped");
        var shutdown = lifetime.ShutdownAsync();
        Assert.True(lifetime.IsStopping);
        Assert.True(cancelled);
        Assert.False(shutdown.IsCompleted);
        Assert.Same(shutdown, lifetime.ShutdownAsync());
        await Assert.ThrowsAsync<OperationCanceledException>(() => lifetime.RunAsync(_ => Task.CompletedTask));
        finish.SetResult();
        await work;
        await shutdown;
        Assert.True(lifetime.IsStopped);
        Assert.Equal(new[] { "transaction-disposed", "session-cleared", "host-disposed", "application-stopped" }, order);
    }

    [Fact]
    public async Task FailedWorkStillDrainsAndFailedCleanupNeverSignalsRelease()
    {
        var lifetime = new ApplicationLifetime();
        var released = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.RunAsync(_ => throw new InvalidOperationException("Synthetic")));
        lifetime.HostCleanup = () => throw new InvalidOperationException("Synthetic cleanup failure");
        lifetime.ShutdownCompleted += () => released = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.ShutdownAsync());
        Assert.True(lifetime.IsStopping);
        Assert.False(lifetime.IsStopped);
        Assert.False(released);
    }

    [Fact]
    public async Task CancellationRollsBackDisposableTransactionBeforeOwnershipRelease()
    {
        var path = System.IO.Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "stage3-5-drain", Guid.NewGuid().ToString("N"), "synthetic.db");
        var database = new Infrastructure.DevelopmentDatabase(new Infrastructure.ConfiguredDatabaseLocation(path));
        await database.InitializeAsync();
        var lifetime = new ApplicationLifetime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rolledBack = false;
        var work = lifetime.RunAsync(async token =>
        {
            await using var db = database.OpenExisting();
            await using (var transaction = await db.BeginTransactionAsync())
            {
                db.Add(new Domain.Company { Code = "DRAIN_TEST", LegalName = "Synthetic transaction" });
                await db.SaveChangesAsync();
                started.SetResult();
                await finish.Task;
                await transaction.RollbackAsync();
            }
            rolledBack = true;
        });
        await started.Task;
        lifetime.HostCleanup = () =>
        {
            Assert.True(rolledBack);
            return Task.CompletedTask;
        };
        var shutdown = lifetime.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);
        finish.SetResult();
        await Task.WhenAll(work, shutdown);
        await using var verify = database.OpenExisting();
        Assert.Empty(await verify.Set<Domain.Company>().ToListAsync());
    }
}
