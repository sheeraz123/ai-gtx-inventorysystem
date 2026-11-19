using System.IO;
using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class StartupRepairTests : IAsyncLifetime
{
    private const string Pending="20261118000200_CompanyInputFlexibility";
    private readonly DiagnosticsFixture diagnostics=new();
    private readonly string directory=Path.Combine(PersistenceTests.FindBillingRoot(),".local","startup-repair-tests",Guid.NewGuid().ToString("N"));
    private string PathValue=>Path.Combine(directory,"synthetic.db");
    private string Marker=>PathValue+".installation.json";
    private IDatabaseLocation Location=>new ConfiguredDatabaseLocation(PathValue);
    private DevelopmentDatabase Database=>new(Location);
    private DevelopmentDatabaseMaintenance Maintenance=>new(Database,Location);
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        var id=Guid.NewGuid();
        await using(var db=DevelopmentDatabase.CreateContext(PathValue,true))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261118000100_CompanyMaster");
            db.AddRange(new Installation {Id=id},new DatabaseSecurityMetadata {InstallationId=id});
            var company=new Company {Code="SYNTHETIC",LegalName="Synthetic retained company"};
            db.AddRange(company,new CompanyBankAccount {CompanyId=company.Id,BankName="Synthetic bank",AccountHolder="Synthetic",
                AccountNumber="1234567890",Ifsc="TEST0000001",IsDefault=true},new GstRegistration {CompanyId=company.Id,Gstin="27AAAAA0000A1Z2",StateCode="27"},
                new CompanySelection {CompanyId=company.Id});
            await db.SaveChangesAsync();
        }
        await File.WriteAllTextAsync(Marker,JsonSerializer.Serialize(new {InstallationId=id,DatabasePath=PathValue,State="Ready"}));
        var auth=new AuthenticationService(new AuthenticationStore(Database),new Argon2PasswordHasher(),TimeProvider.System);
        Assert.Equal(AuthenticationOutcome.Success,(await auth.SetupAsync("Synthetic.Admin",AuthenticationTests.Original,AuthenticationTests.Original)).Outcome);
    }
    public Task DisposeAsync() {diagnostics.Dispose();Directory.Delete(directory,true);return Task.CompletedTask;}

    [Fact]
    public async Task InspectionDoesNotUpgradeAndStaleApprovalCannotModifyDatabase()
    {
        var before=await File.ReadAllBytesAsync(PathValue);var marker=await File.ReadAllBytesAsync(Marker);
        var error=await Assert.ThrowsAsync<DatabaseStartupException>(()=>Database.VerifyExistingAsync());
        Assert.Equal(DatabaseStartupProblem.UpgradeRequired,error.Problem);
        Assert.Equal(new[]{Pending},await Maintenance.GetReviewedPendingAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Maintenance.ApplyReviewedAsync(["20261118000100_CompanyMaster"]));
        Assert.True(Enumerable.SequenceEqual(before,await File.ReadAllBytesAsync(PathValue)));
        Assert.True(Enumerable.SequenceEqual(marker,await File.ReadAllBytesAsync(Marker)));
        Assert.False(Directory.Exists(Path.Combine(directory,".local","database-upgrades")));
    }

    [Fact]
    public async Task ReviewedUpgradeVerifiesBackupPreservesAccountsAndDoesNotRepeatOnRestart()
    {
        var marker=await File.ReadAllBytesAsync(Marker);
        string password;List<string> codes;
        await using(var before=Database.OpenExisting())
        {
            password=(await before.Set<User>().SingleAsync()).PasswordHash;
            codes=await before.Set<RecoveryCode>().OrderBy(x=>x.Id).Select(x=>x.CodeHash).ToListAsync();
        }
        var receipt=await Maintenance.ApplyReviewedAsync([Pending]);
        Assert.True(File.Exists(receipt.BackupPath));Assert.True(File.Exists(receipt.EvidencePath));
        Assert.True(Enumerable.SequenceEqual(marker,await File.ReadAllBytesAsync(Marker)));
        await Database.VerifyExistingAsync();Assert.Empty(await Maintenance.GetReviewedPendingAsync());
        await Database.VerifyExistingAsync(); // Repeat launch verification makes no further upgrade.
        await using(var after=Database.OpenExisting())
        {
            Assert.True(password==(await after.Set<User>().SingleAsync()).PasswordHash);
            Assert.True(codes.SequenceEqual(await after.Set<RecoveryCode>().OrderBy(x=>x.Id).Select(x=>x.CodeHash).ToListAsync()));
            Assert.Single(await after.Set<Company>().ToListAsync());Assert.Single(await after.Set<CompanySelection>().ToListAsync());
        }
        await using(var copy=DevelopmentDatabase.CreateContext(receipt.BackupPath))
        {
            Assert.Equal(new[]{"20261116000100_InitialDevelopmentSchema","20261118000100_CompanyMaster"},await copy.Database.GetAppliedMigrationsAsync());
            Assert.Single(await copy.Set<User>().ToListAsync());Assert.Equal(10,await copy.Set<RecoveryCode>().CountAsync());
        }
        var auth=new AuthenticationService(new AuthenticationStore(Database),new Argon2PasswordHasher(),TimeProvider.System);
        Assert.Equal(AuthenticationOutcome.Success,(await auth.LoginAsync("Synthetic.Admin",AuthenticationTests.Original)).Outcome);
        var evidence=await File.ReadAllTextAsync(receipt.EvidencePath);
        Assert.True(!evidence.Contains(AuthenticationTests.Original,StringComparison.Ordinal));
        Assert.True(!evidence.Contains(password,StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailureAfterTableRebuildKeepsInstallationBlockedAndVerifiedBackupAvailable()
    {
        await using(var fault=Database.OpenExisting())
            await fault.Database.ExecuteSqlRawAsync("CREATE TRIGGER SyntheticUpgradeFailure BEFORE INSERT ON __EFMigrationsHistory WHEN NEW.MigrationId='20261118000200_CompanyInputFlexibility' BEGIN SELECT RAISE(ABORT,'synthetic fault'); END;");
        await Assert.ThrowsAnyAsync<Exception>(()=>Maintenance.ApplyReviewedAsync([Pending]));
        var error=Assert.Throws<DatabaseStartupException>(()=>Database.OpenExisting());
        Assert.Equal(DatabaseStartupProblem.MaintenanceInProgress,error.Problem);
        Assert.True(File.Exists(PathValue));
        var backup=Assert.Single(Directory.GetFiles(Path.Combine(directory,".local","database-upgrades"),"before-upgrade.db",SearchOption.AllDirectories));
        await using var copy=DevelopmentDatabase.CreateContext(backup);
        Assert.Equal(2,(await copy.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Single(await copy.Set<User>().ToListAsync());Assert.Equal(10,await copy.Set<RecoveryCode>().CountAsync());
    }

    [Fact]
    public async Task BackupFailureLeavesEstablishedDatabaseAndReadyMarkerUntouched()
    {
        var before=await File.ReadAllBytesAsync(PathValue);var marker=await File.ReadAllBytesAsync(Marker);
        await File.WriteAllTextAsync(Path.Combine(directory,".local"),"Synthetic obstruction");
        await Assert.ThrowsAnyAsync<Exception>(()=>Maintenance.ApplyReviewedAsync([Pending]));
        Assert.True(Enumerable.SequenceEqual(before,await File.ReadAllBytesAsync(PathValue)));
        Assert.True(Enumerable.SequenceEqual(marker,await File.ReadAllBytesAsync(Marker)));
        Assert.Equal(new[]{Pending},await Maintenance.GetReviewedPendingAsync());
    }

    [Fact]
    public async Task NewerAndUnrecognizedHistoryNeverOfferDowngradeOrUnreviewedUpgrade()
    {
        await using(var db=Database.OpenExisting())
            await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory VALUES ('99999999999999_Unknown','10.0.15')");
        Assert.Equal(DatabaseStartupProblem.UnrecognizedHistory,(await Assert.ThrowsAsync<DatabaseStartupException>(()=>Maintenance.GetReviewedPendingAsync())).Problem);
        Assert.False(Directory.Exists(Path.Combine(directory,".local","database-upgrades")));
        var newer=DatabaseStartupException.CompareHistory(["001","002"],["001","002","003"]);
        Assert.Equal(DatabaseStartupProblem.ApplicationTooOld,newer!.Problem);
        Assert.Equal(DatabaseStartupProblem.UnrecognizedHistory,DatabaseStartupException.CompareHistory(["001"],[])!.Problem);
    }

    [Theory]
    [InlineData(DatabaseStartupProblem.MissingDatabase)]
    [InlineData(DatabaseStartupProblem.InvalidMarker)]
    [InlineData(DatabaseStartupProblem.MaintenanceInProgress)]
    [InlineData(DatabaseStartupProblem.UpgradeRequired)]
    [InlineData(DatabaseStartupProblem.ApplicationTooOld)]
    [InlineData(DatabaseStartupProblem.UnrecognizedHistory)]
    [InlineData(DatabaseStartupProblem.IntegrityFailure)]
    [InlineData(DatabaseStartupProblem.IdentityMismatch)]
    public void StartupMessagesIdentifyTheFailureWithoutExposingExceptionPayloads(DatabaseStartupProblem problem)
    {
        var notice=StartupFailurePresentation.Describe(new DatabaseStartupException(problem),"database verification");
        Assert.NotEmpty(notice.Code);Assert.Contains("No replacement database was created.",notice.Message);
        var secret=Guid.NewGuid().ToString("N");
        Assert.True(!StartupFailurePresentation.Describe(new IOException(secret),"database verification").Message.Contains(secret));
        if(problem==DatabaseStartupProblem.ApplicationTooOld) Assert.Contains("older",notice.Message);
        if(problem==DatabaseStartupProblem.UpgradeRequired) Assert.Contains("update",notice.Message);
    }
}

