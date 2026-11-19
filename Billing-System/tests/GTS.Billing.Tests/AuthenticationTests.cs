using System.Diagnostics;
using System.IO;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using GTS.Billing.Printing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace GTS.Billing.Tests;

public sealed class AuthenticationTests : IAsyncLifetime
{
    private readonly DiagnosticsFixture diagnostics = new();
    internal const string Original = "Synthetic passphrase Ω 42";
    private const string NewPassword = "Synthetic replacement café 46";
    private const string Username = "Test.Admin";
    private readonly string directory = Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "stage3-tests", Guid.NewGuid().ToString("N"));
    private readonly TestClock clock = new();
    private string DbPath => Path.Combine(directory, "synthetic.db");
    private DevelopmentDatabase Database => new(new ConfiguredDatabaseLocation(DbPath));
    private AuthenticationService Service() => new(new AuthenticationStore(Database), new Argon2PasswordHasher(), clock);
    public Task InitializeAsync() => Database.InitializeAsync();
    public Task DisposeAsync() { diagnostics.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); return Task.CompletedTask; }
    private static Task<AuthenticationResult> Setup(AuthenticationService service) => service.SetupAsync(Username, Original, Original);
    private static Task<AuthenticationResult> Login(AuthenticationService service) => service.LoginAsync(" test.admin ", Original);

    [Fact]
    public async Task StageOneTwoDatabaseRetainsDataAndMigrationWhenOpenedByStageThree()
    {
        var company = new Company { Code = "STAGE2_TEST", LegalName = "Synthetic prior-stage company" };
        await using (var before = Database.OpenExisting())
        {
            before.Add(company);
            await before.SaveChangesAsync();
            Assert.Equal(new[] { "20261116000100_InitialDevelopmentSchema", "20261118000100_CompanyMaster", "20261118000200_CompanyInputFlexibility" }, await before.Database.GetAppliedMigrationsAsync());
        }
        await Database.VerifyExistingAsync();
        Assert.Equal(AuthenticationOutcome.Success, (await Setup(Service())).Outcome);
        await using var after = Database.OpenExisting();
        Assert.Equal(company.LegalName, (await after.FindAsync(company.Id))?.LegalName);
        Assert.False(after.Database.HasPendingModelChanges());
        Assert.Equal("PlaintextDevelopment", (await after.Set<Installation>().SingleAsync()).DatabaseMode);
        var header = (await File.ReadAllBytesAsync(DbPath)).AsSpan(0, 16);
        Assert.True(header.SequenceEqual("SQLite format 3\0"u8));
    }

    [Fact]
    public async Task SetupIsAtomicAndRestartRecognizesExistingAdmin()
    {
        var app = Service();
        Assert.Equal(AuthenticationEntry.Setup, await app.EntryAsync());
        var setup = await Setup(app);
        Assert.Equal(AuthenticationOutcome.Success, setup.Outcome);
        var codes = setup.Codes!;
        Assert.Equal(10, codes.Count);
        Assert.True(codes.Distinct().Count() == 10 && codes.All(code => code.Length == 39 &&
            System.Text.RegularExpressions.Regex.IsMatch(code, "^[2-9A-HJ-NP-Z]{4}(-[2-9A-HJ-NP-Z]{4}){7}$")));
        Assert.Null(await app.GetCurrentAsync());
        Assert.Equal(AuthenticationEntry.Login, await Service().EntryAsync());
        Assert.Equal(AuthenticationOutcome.AlreadyConfigured, (await Setup(Service())).Outcome);

        await using var db = Database.OpenExisting();
        var admin = await db.Set<SuperAdminAssignment>().SingleAsync();
        var user = await db.Set<User>().SingleAsync();
        Assert.Equal(admin.UserId, user.Id);
        var stored = await db.Set<RecoveryCode>().ToListAsync();
        Assert.Equal(10, stored.Count);
        Assert.All(stored, row => Assert.Equal(user.Id, row.UserId));
        Assert.True(stored.Select(r => r.CodeHash.Split(':')[1]).Distinct().Count() == 10);
        var audit = await db.Set<AuditEvent>().ToListAsync();
        Assert.Contains(audit, e => e.Action == "SetupAlreadyConfigured");
        Assert.All(audit, e => Assert.NotNull(e.RecordId));
        var secrets = codes.Append(Original).ToArray();
        var raw = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(DbPath));
        Assert.True(secrets.All(secret => !raw.Contains(secret, StringComparison.Ordinal)));
        Assert.True(secrets.All(secret => audit.All(e => !e.ChangesJson.Contains(secret, StringComparison.Ordinal))));
        Assert.True(secrets.All(secret => !setup.ToString().Contains(secret, StringComparison.Ordinal)));
        Assert.True(secrets.All(secret => !System.Text.Json.JsonSerializer.Serialize(setup).Contains(secret, StringComparison.Ordinal)));
        Assert.True(secrets.All(secret => !user.PasswordHash.Contains(secret, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task InvalidOrConflictingSetupDoesNotInitialize()
    {
        var service = Service();
        Assert.Equal(AuthenticationOutcome.InvalidInput, (await service.SetupAsync(Username, "short", "short")).Outcome);
        Assert.Equal(AuthenticationOutcome.InvalidInput, (await service.SetupAsync(Username, Original, NewPassword)).Outcome);
        Assert.Equal(AuthenticationOutcome.InvalidInput, (await service.SetupAsync("bad name", Original, Original)).Outcome);
        var existing = new User { Username = Username, NormalizedUsername = "TEST.ADMIN", PasswordHash = "SYNTHETIC-NON-VERIFIER" };
        await using var db = Database.OpenExisting();
        db.Add(existing);
        await db.SaveChangesAsync();
        Assert.Equal(AuthenticationOutcome.Rejected, (await Setup(service)).Outcome);
        Assert.Equal(AuthenticationEntry.Setup, await service.EntryAsync());
        Assert.Empty(await db.Set<SuperAdminAssignment>().ToListAsync());
        Assert.Empty(await db.Set<RecoveryCode>().ToListAsync());
        Assert.Single(await db.Set<User>().ToListAsync());
    }

    [Fact]
    public async Task ConcurrentSetupOnlyCommitsOnce()
    {
        var first = Service(); var second = Service();
        using var barrier = new Barrier(2);
        async Task<AuthenticationOutcome> Attempt(AuthenticationService app)
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return (await Setup(app)).Outcome;
        }
        var results = await Task.WhenAll(Task.Run(() => Attempt(first)), Task.Run(() => Attempt(second)));
        Assert.Single(results, r => r == AuthenticationOutcome.Success);
        Assert.Single(results, r => r == AuthenticationOutcome.AlreadyConfigured);
        await using var db = Database.OpenExisting();
        Assert.Single(await db.Set<SuperAdminAssignment>().ToListAsync());
        Assert.Single(await db.Set<User>().ToListAsync());
    }

    [Fact]
    public async Task AuditWriteFailureRollsBackSetup()
    {
        await Execute("CREATE TRIGGER TestRejectSetup BEFORE INSERT ON AuditEvents WHEN NEW.Action='SetupCompleted' BEGIN SELECT RAISE(ABORT,'Synthetic failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => Setup(Service()));
        await using var db = Database.OpenExisting();
        Assert.Empty(await db.Set<User>().ToListAsync());
        Assert.Empty(await db.Set<RecoveryCode>().ToListAsync());
        Assert.Empty(await db.Set<SuperAdminAssignment>().ToListAsync());
        Assert.Equal(AuthenticationEntry.Setup, await Service().EntryAsync());
    }

    [Fact]
    public async Task LoginUsesGenericFailureAndPersistentBoundedThrottling()
    {
        var service = Service();
        await Setup(service);
        Assert.Equal(AuthenticationOutcome.Rejected, (await service.LoginAsync(Username, NewPassword)).Outcome);
        Assert.Equal(AuthenticationOutcome.Rejected, (await service.LoginAsync("Unknown", NewPassword)).Outcome);
        for (int i = 0; i < 3; i++)
            Assert.Equal(AuthenticationOutcome.Rejected, (await service.LoginAsync("Unknown", NewPassword)).Outcome);
        Assert.Equal(AuthenticationOutcome.Throttled, (await Login(Service())).Outcome);
        clock.Advance(TimeSpan.FromSeconds(31));
        service = Service();
        Assert.Equal(AuthenticationOutcome.Success, (await Login(service)).Outcome);
        Assert.True((await service.GetCurrentAsync())?.IsSuperAdmin == true);
        await service.LogoutAsync();
        Assert.Null(await service.GetCurrentAsync());
        Assert.Equal(AuthenticationOutcome.SessionRequired, (await service.RegenerateAsync(Original)).Outcome);
        await using var db = Database.OpenExisting();
        var actions = await db.Set<AuditEvent>().Select(e => e.Action).ToListAsync();
        Assert.Contains("LoginFailed", actions); Assert.Contains("LoginThrottled", actions);
        Assert.Contains("LoginSucceeded", actions); Assert.Contains("Logout", actions);
    }

    [Fact]
    public async Task DisabledUserCannotLoginAndOrdinaryIdentityCannotRegenerate()
    {
        var service = Service();
        await Setup(service);
        var hasher = new Argon2PasswordHasher();
        await using var db = Database.OpenExisting();
        var employee = new User { Username = "Test.Employee", NormalizedUsername = "TEST.EMPLOYEE", PasswordHash = hasher.Hash(Original), IsActive = false };
        db.Add(employee); await db.SaveChangesAsync();
        Assert.Equal(AuthenticationOutcome.Rejected, (await service.LoginAsync(employee.Username, Original)).Outcome);
        employee.IsActive = true; await db.SaveChangesAsync();
        Assert.Equal(AuthenticationOutcome.Success, (await service.LoginAsync(employee.Username, Original)).Outcome);
        Assert.False((await service.GetCurrentAsync())!.IsSuperAdmin);
        Assert.Equal(AuthenticationOutcome.SessionRequired, (await service.RegenerateAsync(Original)).Outcome);
    }

    [Fact]
    public async Task RecoveryPreservesAdminIdentityAndInvalidatesPreviousSessionsWithoutChangingOtherUsers()
    {
        var service = Service();
        var setup = await Setup(service);
        var session = Service();
        await Login(session);
        var id = (await session.GetCurrentAsync())!.UserId;
        var employee = new User { Username = "Test.Other", NormalizedUsername = "TEST.OTHER", PasswordHash = new Argon2PasswordHasher().Hash(Original) };
        await using (var db = Database.OpenExisting()) { db.Add(employee); await db.SaveChangesAsync(); }
        Assert.Equal(AuthenticationOutcome.Success, (await service.RecoverAsync(setup.Codes![0].ToLowerInvariant(), NewPassword, NewPassword)).Outcome);
        Assert.Null(await session.GetCurrentAsync());
        Assert.Equal(AuthenticationOutcome.Rejected, (await Login(service)).Outcome);
        Assert.Equal(AuthenticationOutcome.Success, (await service.LoginAsync(Username, NewPassword)).Outcome);
        Assert.Equal(id, (await service.GetCurrentAsync())!.UserId);
        Assert.Equal(AuthenticationOutcome.Rejected, (await service.RecoverAsync(setup.Codes[0], Original, Original)).Outcome);
        await using var verify = Database.OpenExisting();
        Assert.Equal(id, (await verify.Set<SuperAdminAssignment>().SingleAsync()).UserId);
        Assert.True(employee.PasswordHash == (await verify.Set<User>().FindAsync(employee.Id))!.PasswordHash);
        Assert.Equal(1, await verify.Set<RecoveryCode>().CountAsync(x => x.ConsumedUtc != null));
        var events = await verify.Set<AuditEvent>().ToListAsync();
        Assert.Contains(events, e => e.Action == "SessionsInvalidated");
        Assert.Contains(events, e => e.Action == "PasswordChanged");
        Assert.Contains(events, e => e.Action == "RecoverySucceeded");
        Assert.Contains(events, e => e.Action == "RecoveryFailed");
    }

    [Fact]
    public async Task ConcurrentReuseIsRejected()
    {
        var app = Service();
        var code = (await Setup(app)).Codes![0];
        var second = Service();
        var results = await Task.WhenAll(Task.Run(() => app.RecoverAsync(code, NewPassword, NewPassword)),
            Task.Run(() => second.RecoverAsync(code, NewPassword, NewPassword)));
        Assert.Single(results, r => r.Outcome == AuthenticationOutcome.Success);
        Assert.Single(results, r => r.Outcome == AuthenticationOutcome.Rejected);
    }

    [Fact]
    public async Task FailedPasswordUpdateDoesNotConsumeCode()
    {
        var app = Service();
        var code = (await Setup(app)).Codes![0];
        await Execute("CREATE TRIGGER TestRejectReset BEFORE UPDATE OF PasswordHash ON Users BEGIN SELECT RAISE(ABORT,'Synthetic failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => app.RecoverAsync(code, NewPassword, NewPassword));
        await using var db = Database.OpenExisting();
        Assert.Equal(0, await db.Set<RecoveryCode>().CountAsync(c => c.ConsumedUtc != null));
        Assert.Equal(0, await db.Set<AuditEvent>().CountAsync(e => e.Action == "RecoverySucceeded"));
        await Execute("DROP TRIGGER TestRejectReset;");
        Assert.Equal(AuthenticationOutcome.Success, (await app.RecoverAsync(code, NewPassword, NewPassword)).Outcome);
    }

    [Fact]
    public async Task WrongCodesAreAuditedAndThrottledAcrossServices()
    {
        var app = Service();
        var code = (await Setup(app)).Codes![0];
        for (int i = 0; i < 5; i++)
            Assert.Equal(AuthenticationOutcome.Rejected, (await app.RecoverAsync("invalid", NewPassword, NewPassword)).Outcome);
        Assert.Equal(AuthenticationOutcome.Throttled, (await Service().RecoverAsync(code, NewPassword, NewPassword)).Outcome);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(AuthenticationOutcome.Success, (await Service().RecoverAsync(code, NewPassword, NewPassword)).Outcome);
    }

    [Fact]
    public async Task RegenerationRequiresPasswordAndRevokesOlderUnusedCodes()
    {
        var app = Service(); var previous = (await Setup(app)).Codes!;
        await Login(app);
        Assert.Equal(AuthenticationOutcome.Rejected, (await app.RegenerateAsync(NewPassword)).Outcome);
        var result = await app.RegenerateAsync(Original);
        Assert.Equal(AuthenticationOutcome.Success, result.Outcome);
        Assert.Equal(10, result.Codes!.Count);
        Assert.True(!previous.Intersect(result.Codes).Any());
        await using var db = Database.OpenExisting();
        Assert.Equal(10, await db.Set<RecoveryCode>().CountAsync(x => x.InvalidatedUtc != null));
        Assert.Equal(AuthenticationOutcome.Rejected, (await app.RecoverAsync(previous[0], NewPassword, NewPassword)).Outcome);
        Assert.Equal(AuthenticationOutcome.Success, (await app.RecoverAsync(result.Codes[0], NewPassword, NewPassword)).Outcome);
        Assert.Contains("RecoveryCodesRegenerated", await db.Set<AuditEvent>().Select(e => e.Action).ToListAsync());
    }

    [Fact]
    public async Task StaleRegenerationReturnsToLoginAndAcceptsNewPasswordWithoutRestart()
    {
        var original = Service();
        var setup = await Setup(original);
        var vm = new AuthenticationViewModel(original);
        await vm.StartAsync(true);
        await vm.SubmitAsync(Username, Original, "", "");
        Assert.Equal(AuthenticationPage.Shell, vm.Page);
        await vm.BeginRegenerationAsync();
        Assert.Equal(AuthenticationPage.Regenerate, vm.Page);

        var recovery = await Service().RecoverAsync(setup.Codes![0], NewPassword, NewPassword);
        Assert.Equal(AuthenticationOutcome.Success, recovery.Outcome);

        await vm.SubmitAsync("", NewPassword, "", "");
        Assert.Equal(AuthenticationPage.Login, vm.Page);
        Assert.Equal("Sign in again to continue.", vm.Message);
        Assert.False(vm.Lifetime.IsStopping);
        Assert.Empty(vm.Codes);
        Assert.Equal(AuthenticationOutcome.SessionRequired, (await original.RegenerateAsync(NewPassword)).Outcome);

        // GetCurrentAsync independently clears stale state, so don't call it before testing re-login.
        await vm.SubmitAsync(Username, Original, "", "");
        Assert.Equal(AuthenticationPage.Login, vm.Page);
        await vm.SubmitAsync(Username, NewPassword, "", "");
        Assert.Equal(AuthenticationPage.Shell, vm.Page);
        Assert.True((await original.GetCurrentAsync())?.IsSuperAdmin == true);

        await vm.LogoutAsync();
        Assert.Equal(AuthenticationPage.Closing, vm.Page);
        Assert.True(vm.Lifetime.IsStopped);
        Assert.Equal(AuthenticationOutcome.SessionRequired, (await original.RegenerateAsync(NewPassword)).Outcome);
    }

    [Fact]
    public async Task ViewModelGatesShellAndDiscardsDisplayedCodes()
    {
        var app = Service();
        var vm = new AuthenticationViewModel(app);
        await vm.StartAsync(false);
        Assert.Equal(AuthenticationPage.Unavailable, vm.Page);
        await vm.StartAsync(true);
        Assert.Equal(AuthenticationPage.Setup, vm.Page);
        await vm.SubmitAsync(Username, Original, Original, "");
        Assert.Equal(AuthenticationPage.Codes, vm.Page);
        Assert.Equal(10, vm.Codes.Count);
        var document = RecoveryCodeDocument.Format(vm.Codes);
        Assert.True(vm.Codes.All(c => document.Contains(c, StringComparison.Ordinal)));
        await vm.ContinueAsync();
        Assert.Empty(vm.Codes);
        Assert.Equal(AuthenticationPage.Login, vm.Page);
        await vm.SubmitAsync(Username, NewPassword, "", "");
        var failure = vm.Message;
        await vm.SubmitAsync("Unknown", NewPassword, "", "");
        Assert.Equal(failure, vm.Message);
        Assert.Equal(AuthenticationPage.Login, vm.Page);
        await vm.SubmitAsync(Username, Original, "", "");
        Assert.Equal(AuthenticationPage.Shell, vm.Page);
        Assert.True(vm.CanRegenerate);
        await vm.BeginRegenerationAsync();
        Assert.Equal(AuthenticationPage.Regenerate, vm.Page);
        await vm.SubmitAsync("", Original, "", "");
        Assert.Equal(AuthenticationPage.Codes, vm.Page);
        await vm.ContinueAsync();
        Assert.Equal(AuthenticationPage.Shell, vm.Page);
        await vm.LogoutAsync();
        Assert.Equal(AuthenticationPage.Closing, vm.Page);
        await vm.BeginRegenerationAsync();
        Assert.Equal(AuthenticationPage.Closing, vm.Page);
        Assert.Empty(vm.Codes);
        Assert.Null(await app.GetCurrentAsync());
    }

    private async Task Execute(string sql)
    {
        await using var db = Database.OpenExisting();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task WpfWiresRecoveryCommandsAndHidesShellUntilLogin()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var vm = new AuthenticationViewModel(Service());
                    await vm.StartAsync(true);
                    var window = new MainWindow(new ShellViewModel(), vm, new ConfiguredDatabaseLocation(DbPath));
                    var shell = Assert.IsType<System.Windows.Controls.DockPanel>(window.FindName("BillingShell"));
                    var view = Assert.IsType<AuthenticationView>(window.FindName("AuthenticationPanel"));
                    Assert.Equal(System.Windows.Visibility.Collapsed, shell.Visibility);
                    Assert.Equal(AuthenticationPage.Setup, vm.Page);
                    await vm.SubmitAsync(Username, Original, Original, "");
                    Assert.Equal(AuthenticationPage.Codes, vm.Page);
                    foreach (var name in new[] { "Print", "Save" })
                        Assert.NotNull(Assert.IsType<System.Windows.Controls.Button>(view.FindName(name)).Command);
                    await vm.ContinueAsync();
                    Assert.Equal(AuthenticationPage.Login, vm.Page);
                    Assert.Equal(System.Windows.Visibility.Collapsed, shell.Visibility);
                    await vm.SubmitAsync(Username, Original, "", "");
                    Assert.Equal(System.Windows.Visibility.Visible, shell.Visibility);
                    var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var accepted = vm.Lifetime.RunAsync(_ => finish.Task);
                    var logout = vm.LogoutAsync();
                    Assert.False(logout.IsCompleted);
                    Assert.Equal("Closing GTS Billing, please wait…",
                        Assert.IsType<System.Windows.Controls.TextBlock>(view.FindName("Heading")).Text);
                    Assert.Equal("Waiting for an active operation to finish. GTS Billing will close automatically.",
                        Assert.IsType<System.Windows.Controls.TextBlock>(view.FindName("Instructions")).Text);
                    Assert.Equal(System.Windows.Visibility.Collapsed,
                        Assert.IsType<System.Windows.Controls.StackPanel>(view.FindName("Form")).Visibility);
                    await vm.SubmitAsync(Username, Original, "", "");
                    Assert.Equal(AuthenticationPage.Closing, vm.Page);
                    finish.SetResult();
                    await Task.WhenAll(accepted, logout);
                    Assert.Equal("Closing GTS Billing, please wait…",
                        Assert.IsType<System.Windows.Controls.TextBlock>(view.FindName("Instructions")).Text);
                    Assert.Equal(System.Windows.Visibility.Collapsed, shell.Visibility);
                    window.Close();
                    completed.TrySetResult();
                }
                catch (Exception exception) { completed.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal); }
            });
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}

public sealed class PasswordHashTests(ITestOutputHelper output) : IDisposable
{
    private readonly DiagnosticsFixture diagnostics = new();
    public void Dispose() => diagnostics.Dispose();
    [Fact]
    public async Task DiagnosticsRecorderOmitsMessageAndExceptionPayload()
    {
        var marker = Guid.NewGuid().ToString("N");
        using var diagnostics = new SafeDiagnostics();
        var logger = diagnostics.CreateLogger("SyntheticTest");
        var eventId = new EventId(3203, "SyntheticTest");
        logger.Log(LogLevel.Warning, eventId, marker, new InvalidOperationException(marker), (state, _) => state);
        var path = Path.Combine(SafeDiagnostics.DirectoryPath, "events.jsonl");
        var data = await File.ReadAllTextAsync(path);
        Assert.Contains("event-3203", data);
        Assert.True(!data.Contains(marker, StringComparison.Ordinal));
    }

    [Fact]
    public void Argon2idSaltVersionAndUnicodeVerification()
    {
        var hasher = new Argon2PasswordHasher();
        const string pass = "Synthetic Ελληνικά 日本語 🌱";
        var stopwatch = Stopwatch.StartNew();
        var first = hasher.Hash(pass);
        var second = hasher.Hash(pass);
        stopwatch.Stop();
        output.WriteLine($"Argon2id synthetic hash operations (two, including cold start): {stopwatch.ElapsedMilliseconds} ms.");
        var profile = first.Split('$');
        Assert.Equal("argon2id", profile[1]);
        Assert.Equal("v=19", profile[2]);
        Assert.Equal("m=65536,t=3,p=1", profile[3]);
        Assert.True(first.Split('$')[4] != second.Split('$')[4]);
        Assert.True(hasher.Verify(pass, first));
        Assert.False(hasher.Verify(pass + "x", first));
        Assert.False(hasher.NeedsUpgrade(first));
        Assert.False(hasher.Verify(pass, "$argon2id$v=19$m=999999999,t=99,p=99$invalid"));
    }
}
