using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class GlobalInstanceLockTests : IDisposable
{
    private readonly string directory = Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "stage3-5", Guid.NewGuid().ToString("N"));
    private readonly DeploymentIdentity identity = new(Guid.NewGuid(), [WindowsIdentity.GetCurrent().User!.Value]);
    private readonly List<Process> children = [];
    private string IdentityPath => Path.Combine(directory, "identity.json");

    public GlobalInstanceLockTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(IdentityPath, JsonSerializer.Serialize(identity));
    }

    private Process Start(string mode, params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "GTS.Billing.Tests.exe"))
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add(IdentityPath);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var child = Process.Start(info)!;
        children.Add(child);
        return child;
    }

    private static Task<string?> Line(Process child) => child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
    private static async Task<int> Exit(Process child)
    {
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return child.ExitCode;
    }

    [Fact]
    public async Task CompetingProcessesRejectSecondAndAcquireAfterRelease()
    {
        var first = Start("hold");
        Assert.Equal("Acquired", await Line(first));
        var second = Start("hold");
        Assert.Equal("Busy", await Line(second));
        Assert.Equal(2, await Exit(second));
        await first.StandardInput.WriteLineAsync("release");
        Assert.Equal(0, await Exit(first));
        var successor = Start("hold");
        Assert.Equal("Acquired", await Line(successor));
        await successor.StandardInput.WriteLineAsync("release");
        Assert.Equal(0, await Exit(successor));
    }

    [Fact]
    public async Task SimultaneousLaunchesHaveExactlyOneOwner()
    {
        var first = Start("hold");
        var second = Start("hold");
        var results = await Task.WhenAll(Line(first), Line(second));
        Assert.Equal(1, results.Count(r => r == "Acquired"));
        Assert.Equal(1, results.Count(r => r == "Busy"));
        foreach (var p in new[] { first, second })
            if (!p.HasExited) await p.StandardInput.WriteLineAsync("release");
        await Task.WhenAll(Exit(first), Exit(second));
    }

    [Theory]
    [InlineData("--initialize-development-database")]
    [InlineData("--export-schema")]
    [InlineData("--smoke-test")]
    public async Task EveryEntryModeRejectsBeforeDatabaseOrOutputCreation(string mode)
    {
        var owner = Start("hold");
        Assert.Equal("Acquired", await Line(owner));
        var target = Path.Combine(directory, "must-not-create.db");
        var args = mode == "--export-schema" ? new[] {mode, target} : new[] {mode, "--database", target};
        var rejected = Start("app", args);
        Assert.Equal(2, await Exit(rejected));
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".installation.json"));
        await owner.StandardInput.WriteLineAsync("release");
        await Exit(owner);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CrashRecoveryChecksDatabaseWithOrWithoutAbandonedSignal(bool retainHandle)
    {
        var path = Path.Combine(directory, "synthetic.db");
        var database = new DevelopmentDatabase(new ConfiguredDatabaseLocation(path));
        await database.InitializeAsync();
        var owner = Start("transaction-crash", path);
        Assert.Equal("Acquired", await Line(owner));
        Assert.Equal("TransactionReady", await Line(owner));
        using var observer = retainHandle ? new SafeWaitHandle(OpenMutex(0x00100000, false, identity.MutexName), true) : null;
        if (retainHandle) Assert.False(observer!.IsInvalid);
        await owner.StandardInput.WriteLineAsync("terminate");
        Assert.Equal(9, await Exit(owner));
        // The successor retains the observed ownership while WPF verifies the interrupted database.
        var verify = Start("recover-app", path);
        Assert.Equal(retainHandle ? "Abandoned" : "Acquired", await Line(verify));
        Assert.Equal(0, await Exit(verify));
        await using var db = database.OpenExisting();
        Assert.Empty(await db.Set<Company>().ToListAsync());
    }

    [Fact]
    public async Task SmokeWithPendingUpgradeExitsWithoutPromptOrDatabaseMutation()
    {
        var path=Path.Combine(directory,"pending-upgrade.db");var id=Guid.NewGuid();
        await using(var db=DevelopmentDatabase.CreateContext(path,true))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261118000100_CompanyMaster");
            db.AddRange(new Installation {Id=id},new DatabaseSecurityMetadata {InstallationId=id});
            await db.SaveChangesAsync();
        }
        var marker=path+".installation.json";
        await File.WriteAllTextAsync(marker,JsonSerializer.Serialize(new {InstallationId=id,DatabasePath=path,State="Ready"}));
        var before=await File.ReadAllBytesAsync(path);var markerBefore=await File.ReadAllBytesAsync(marker);
        Assert.Equal(1,await Exit(Start("app","--smoke-test","--database",path)));
        Assert.True(Enumerable.SequenceEqual(before,await File.ReadAllBytesAsync(path)));
        Assert.True(Enumerable.SequenceEqual(markerBefore,await File.ReadAllBytesAsync(marker)));
        Assert.False(Directory.Exists(Path.Combine(directory,".local","database-upgrades")));
    }

    [Fact]
    public async Task InstalledModeAndEnvironmentCannotEnableDatabaseOverride()
    {
        var path = Path.Combine(directory, "must-not-create.db");
        Assert.Equal(1, await Exit(Start("installed-app", "--initialize-development-database", "--database", path)));
        Assert.Equal(1, await Exit(Start("environment-app", "--smoke-test", "--database", path)));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".installation.json"));
    }

    [Fact]
    public async Task UnwritableRequiredSmokeEvidenceFailsVerification()
    {
        var path = Path.Combine(directory, "synthetic.db");
        var database = new DevelopmentDatabase(new ConfiguredDatabaseLocation(path));
        await database.InitializeAsync();
        Assert.Equal(1, await Exit(Start("missing-evidence-app", "--smoke-test", "--database", path)));
        await database.VerifyExistingAsync();
    }

    [Fact]
    public async Task WpfInitializationVerificationAndShutdownReleaseOwnership()
    {
        var path = Path.Combine(directory, "synthetic.db");
        Assert.Equal(0, await Exit(Start("app", "--initialize-development-database", "--database", path)));
        Assert.Equal(0, await Exit(Start("app", "--smoke-test", "--database", path)));
        var successor = Start("hold");
        Assert.Equal("Acquired", await Line(successor));
        await successor.StandardInput.WriteLineAsync("release");
        Assert.Equal(0, await Exit(successor));
    }

    [Fact]
    public async Task WpfSignOutRetainsOwnershipUntilApplicationExit()
    {
        var path = Path.Combine(directory, "synthetic.db");
        await SalesInvoiceWorkflowTests.ProvisionDisposableDatabaseAsync(path);
        var owner = Start("logout-app", path);
        Assert.Equal("Acquired", await Line(owner));
        Assert.Equal("Authenticated", await Line(owner));
        Assert.Equal(2, await Exit(Start("hold")));
        await owner.StandardInput.WriteLineAsync("logout");
        Assert.Equal("SignedOut", await Line(owner));
        Assert.Equal(2, await Exit(Start("hold")));
        await owner.StandardInput.WriteLineAsync("exit");
        Assert.Equal(0, await Exit(owner));
        var successor = Start("hold");
        Assert.Equal("Acquired", await Line(successor));
        await successor.StandardInput.WriteLineAsync("release");
        Assert.Equal(0, await Exit(successor));
    }

    [Fact]
    public async Task CorruptMigrationHistoryStopsBeforeLoginWithoutReinitialization()
    {
        var path = Path.Combine(directory, "synthetic.db");
        var database = new DevelopmentDatabase(new ConfiguredDatabaseLocation(path));
        await database.InitializeAsync();
        await using (var db = database.OpenExisting())
            await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory VALUES ('99999999999999_Unknown', '10.0.15')");
        Assert.Equal(1, await Exit(Start("app", "--smoke-test", "--database", path)));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ForeignObjectNameCollisionFailsClosed()
    {
        using var other = new SafeWaitHandle(CreateEvent(IntPtr.Zero, true, false, identity.MutexName), true);
        Assert.False(other.IsInvalid);
        Assert.Equal(3, await Exit(Start("hold")));
        Assert.Equal(OwnershipResult.InvalidConfiguration, GlobalInstanceLock.TryAcquire(identity, out var mutex));
        Assert.Null(mutex);
    }

    [Fact]
    public async Task AccessDeniedHasNoFallbackAndDoesNotCreateDatabase()
    {
        var sid = identity.AuthorizedSids[0];
        Assert.True(ConvertDescriptor($"D:P(D;;0x00100000;;;{sid})(A;;GA;;;SY)", 1, out var descriptor, out _));
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            using var denied = new SafeWaitHandle(CreateMutex(ref attributes, identity.MutexName, 0, 0), true);
            Assert.False(denied.IsInvalid);
            Assert.Equal(OwnershipResult.AccessDenied, GlobalInstanceLock.TryAcquire(identity, out var ownership));
            Assert.Null(ownership);
            var path = Path.Combine(directory, "must-not-create.db");
            Assert.Equal(3, await Exit(Start("app", "--initialize-development-database", "--database", path)));
            Assert.False(File.Exists(path));
        }
        finally { FreeLocal(descriptor); }
    }

    [Fact]
    public async Task ActiveTransactionKeepsGlobalOwnershipUntilShutdownDrainFinishes()
    {
        var path = Path.Combine(directory, "synthetic.db");
        await new DevelopmentDatabase(new ConfiguredDatabaseLocation(path)).InitializeAsync();
        var owner = Start("drain", path);
        Assert.Equal("Acquired", await Line(owner));
        Assert.Equal("WorkReady", await Line(owner));
        await owner.StandardInput.WriteLineAsync("shutdown");
        Assert.Equal("Draining", await Line(owner));
        Assert.Equal(2, await Exit(Start("hold")));
        await owner.StandardInput.WriteLineAsync("finish");
        Assert.Equal(0, await Exit(owner));
        var next = Start("hold");
        Assert.Equal("Acquired", await Line(next));
        await next.StandardInput.WriteLineAsync("release");
        Assert.Equal(0, await Exit(next));
    }

    [Fact]
    public void InvalidIdentityAndMissingFileFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => DeploymentIdentity.Load(Path.Combine(directory, "absent.json")));
        File.WriteAllText(IdentityPath, "{}");
        Assert.ThrowsAny<Exception>(() => DeploymentIdentity.Load(IdentityPath));
        Assert.Equal(OwnershipResult.InvalidConfiguration,
            GlobalInstanceLock.TryAcquire(new DeploymentIdentity(Guid.Empty, identity.AuthorizedSids), out var mutex));
        Assert.Null(mutex);
        Assert.Throws<InvalidDataException>(() => new DeploymentIdentity(identity.DeploymentId, ["S-1-1-0"]).Validate());
    }

    [Fact]
    public void CrossThreadReleaseIsRejectedAndOwnerCanReleaseOnce()
    {
        Assert.Equal(OwnershipResult.Acquired, GlobalInstanceLock.TryAcquire(identity, out var owner));
        Exception? failure = null;
        var thread = new Thread(() => { try { owner!.Dispose(); } catch (Exception error) { failure = error; } });
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(failure);
        owner!.Dispose();
        owner.Dispose();
    }

    public void Dispose()
    {
        foreach (var child in children)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            child.Dispose();
        }
        // Fixtures are isolated under Billing-System/.local; retain crash evidence for inspection.
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenMutexW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenMutex(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertDescriptor(string value, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateMutexExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutex(ref SecurityAttributes attributes, string name, uint flags, uint access);
    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    private static extern IntPtr FreeLocal(IntPtr memory);
}
