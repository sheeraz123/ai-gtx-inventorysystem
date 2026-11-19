using System.Data.Common;
using System.IO;
using GTS.Billing.Domain;
using GTS.Billing.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly string directory = System.IO.Path.Combine(FindBillingRoot(), ".local", "tests", Guid.NewGuid().ToString("N"));
    private string PathValue => System.IO.Path.Combine(directory, "synthetic.db");
    private DevelopmentDatabase Database => new(new ConfiguredDatabaseLocation(PathValue));
    public Task InitializeAsync() => Database.InitializeAsync();
    public Task DisposeAsync()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
        return Task.CompletedTask;
    }
    public static string FindBillingRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(System.IO.Path.Combine(d.FullName, "GTS.Billing.slnx")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Tests must run from the Billing workspace.");
    }
    private static Company Company(string code = "SYNTHETIC") => new() { Code = code, LegalName = "Synthetic test company" };
    private static User User(string name = "TESTUSER") => new() { Username = name, NormalizedUsername = name, PasswordHash = "SYNTHETIC-HASH-NOT-A-CREDENTIAL" };
    private static GstRegistration Gst(Company c) => new() { CompanyId = c.Id, Gstin = "27AAAAA0000A1Z5", StateCode = "27" };
    private static CompanyAddress Address(Company c, string state = "27") => new() { CompanyId = c.Id, Lines = "Synthetic address", StateCode = state };
    private static CompanyBankAccount Bank(Company c) => new() { CompanyId = c.Id, BankName = "Synthetic bank", AccountHolder = "Synthetic", AccountNumber = "TEST-ONLY-001", Ifsc = "TEST0000001", IsDefault = true };

    [Fact]
    public async Task InitialMigrationCreatesAllTablesAndForeignKeysAreEnabled()
    {
        await Database.VerifyExistingAsync();
        await using var db = Database.OpenExisting();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(new[] { "20261116000100_InitialDevelopmentSchema", "20261118000100_CompanyMaster", "20261118000200_CompanyInputFlexibility" }, await db.Database.GetAppliedMigrationsAsync());
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        var names = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        var expected = new[] { "Installations", "Users", "SuperAdminAssignments", "Roles", "Modules", "UserRoles", "RolePermissions", "UserPermissionOverrides", "RecoveryCodes", "Companies", "CompanyContacts", "CompanyAddresses", "GstRegistrations", "GstRegistrationAddresses", "CompanyBankAccounts", "BrandingAssets", "TermsVersions", "CompanySettings", "FinancialYears", "DocumentNumberingSeries", "NumberCounters", "DocumentNumberLedger", "InvoiceSnapshots", "SnapshotAssets", "AuditEvents", "SessionHistory", "BackupConfigurations", "BackupDestinations", "BackupJobs", "BackupArtifacts", "RestoreOperations", "DatabaseSecurityMetadata", "KeyEnvelopeMetadata" };
        foreach (var name in expected)
            Assert.Contains(name, names);
        foreach (var entity in db.Model.GetEntityTypes())
            Assert.Contains(entity.GetTableName()!, names);
        command.CommandText = "PRAGMA foreign_keys";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        Assert.Equal(34, db.Model.GetEntityTypes().Count());
    }
    [Fact]
    public async Task RecordsSurviveIndependentContextRestart()
    {
        var c = Company();
        await using (var db = Database.OpenExisting())
        {
            db.Add(c);
            await db.SaveChangesAsync();
        }
        await Database.VerifyExistingAsync();
        await using var reopened = Database.OpenExisting();
        Assert.Equal(c.LegalName, (await reopened.FindAsync(c.Id))!.LegalName);
    }
    [Fact]
    public async Task ExistingInitializationCannotOverwriteRecords()
    {
        await using var db = Database.OpenExisting();
        db.Add(Company());
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Database.InitializeAsync());
        Assert.Single(await db.Set<Company>().ToListAsync());
    }
    [Fact]
    public async Task MissingEstablishedDatabaseFailsWithoutCreatingReplacement()
    {
        File.Delete(PathValue);
        Assert.Equal(DatabaseStartupProblem.MissingDatabase,(await Assert.ThrowsAsync<DatabaseStartupException>(() => Database.VerifyExistingAsync())).Problem);
        Assert.False(File.Exists(PathValue));
    }
    [Fact]
    public async Task MissingOrCorruptMarkerFailsClosed()
    {
        await File.WriteAllTextAsync(PathValue + ".installation.json", "{}");
        Assert.Equal(DatabaseStartupProblem.InvalidMarker,(await Assert.ThrowsAsync<DatabaseStartupException>(() => Database.VerifyExistingAsync())).Problem);
    }
    [Fact]
    public async Task ForeignKeyRejectsUnknownCompany()
    {
        await using var db = Database.OpenExisting();
        db.Add(new CompanyContact { CompanyId = Guid.NewGuid(), Value = "Synthetic" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task DuplicateCompanyCodeRejected()
    {
        await using var db = Database.OpenExisting();
        db.AddRange(Company(), Company());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task DuplicateNormalizedUsernameRejected()
    {
        await using var db = Database.OpenExisting();
        db.AddRange(User(), User());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task SingletonSuperAdminRejectsSecondSlot()
    {
        await using var db = Database.OpenExisting();
        var a = User("A");
        var b = User("B");
        db.AddRange(a, b);
        await db.SaveChangesAsync();
        db.Add(new SuperAdminAssignment { UserId = a.Id });
        await db.SaveChangesAsync();
        db.Add(new SuperAdminAssignment { SingletonId = 2, UserId = b.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task CrossCompanyGstAddressMappingRejected()
    {
        await using var db = Database.OpenExisting();
        var a = Company("A");
        var b = Company("B");
        var gst = Gst(a);
        var address = Address(b);
        db.AddRange(a, b, gst, address);
        await db.SaveChangesAsync();
        db.Add(new GstRegistrationAddress { CompanyId = a.Id, GstRegistrationId = gst.Id, AddressId = address.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task SameCompanyGstAddressMappingPersists()
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        var gst = Gst(a);
        var address = Address(a);
        db.AddRange(a, gst, address);
        await db.SaveChangesAsync();
        db.Add(new GstRegistrationAddress { CompanyId = a.Id, GstRegistrationId = gst.Id, AddressId = address.Id });
        await db.SaveChangesAsync();
        Assert.Single(await db.Set<GstRegistrationAddress>().ToListAsync());
    }
    [Fact]
    public async Task GstAddressStateValidationRemovedWhileOwnershipRemainsScoped()
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        var gst = Gst(a);
        var address = Address(a, "29");
        db.AddRange(a, gst, address);
        await db.SaveChangesAsync();
        db.Add(new GstRegistrationAddress { CompanyId = a.Id, GstRegistrationId = gst.Id, AddressId = address.Id });
        await db.SaveChangesAsync();
        Assert.Single(await db.Set<GstRegistrationAddress>().ToListAsync());
    }
    [Fact]
    public async Task TwoDefaultBanksRejected()
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        var first = Bank(a);
        var second = Bank(a);
        second.AccountNumber = "TEST-ONLY-002";
        db.AddRange(a, first, second);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task InactiveDefaultBankRejected()
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        var bank = Bank(a);
        bank.IsActive = false;
        db.AddRange(a, bank);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task CrossCompanyBrandingReferenceRejected()
    {
        await using var db = Database.OpenExisting();
        var a = Company("A");
        var b = Company("B");
        var asset = new BrandingAsset { CompanyId = b.Id, StorageId = "synthetic", Sha256 = new string('A', 64) };
        db.AddRange(a, b, asset);
        await db.SaveChangesAsync();
        db.Add(new CompanySettings { CompanyId = a.Id, Logo1AssetId = asset.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Theory]
    [InlineData("2026-04-01", "2027-03-31", true)]
    [InlineData("2026-01-01", "2026-12-31", false)]
    [InlineData("2026-04-01", "2028-03-31", false)]
    public async Task FinancialYearBoundariesEnforced(string start, string end, bool valid)
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        db.AddRange(a, new FinancialYear { CompanyId = a.Id, Start = DateOnly.Parse(start), End = DateOnly.Parse(end) });
        if (valid)
            await db.SaveChangesAsync();
        else
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task TransactionRollbackRestoresEntireBatch()
    {
        await using var db = Database.OpenExisting();
        await using (var tx = await db.BeginTransactionAsync())
        {
            db.Add(Company());
            await db.SaveChangesAsync();
            db.Add(Company());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            await tx.RollbackAsync();
        }
        await using var verify = Database.OpenExisting();
        Assert.Empty(await verify.Set<Company>().ToListAsync());
    }
    [Fact]
    public async Task StaleConcurrentUpdateRejected()
    {
        await using (var seed = Database.OpenExisting())
        {
            seed.Add(Company());
            await seed.SaveChangesAsync();
        }
        await using var a = Database.OpenExisting();
        await using var b = Database.OpenExisting();
        var ca = await a.Set<Company>().SingleAsync();
        var cb = await b.Set<Company>().SingleAsync();
        ca.LegalName = "Synthetic A";
        cb.LegalName = "Synthetic B";
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }
    [Fact]
    public async Task BackupAndPdfDefaultsAreIndependentAndInactive()
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        db.AddRange(a, new CompanySettings { CompanyId = a.Id }, new BackupConfiguration());
        await db.SaveChangesAsync();
        Assert.False((await db.Set<CompanySettings>().SingleAsync()).AutomaticallySaveInvoicePdf);
        var backup = await db.Set<BackupConfiguration>().SingleAsync();
        Assert.True(backup.AutomaticEnabled);
        Assert.False(backup.IsConfigured);
        Assert.Null(backup.Schedule);
    }
    [Fact]
    public async Task NumberLedgerCannotReuseCancelledNumberOrDeleteHistory()
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        var gst = Gst(a);
        var fy = new FinancialYear { CompanyId = a.Id, Start = new(2026, 4, 1), End = new(2027, 3, 31) };
        var series = new DocumentNumberingSeries { CompanyId = a.Id, GstRegistrationId = gst.Id };
        db.AddRange(a, gst, fy, series);
        await db.SaveChangesAsync();
        var ledger = new DocumentNumberLedger { CompanyId = a.Id, GstRegistrationId = gst.Id, SeriesId = series.Id, FinancialYearId = fy.Id, CompleteNumber = "GT/26-27/0001", Sequence = 1, Status = "Cancelled" };
        db.Add(ledger);
        await db.SaveChangesAsync();
        db.Add(new DocumentNumberLedger { CompanyId = a.Id, GstRegistrationId = gst.Id, SeriesId = series.Id, FinancialYearId = fy.Id, CompleteNumber = ledger.CompleteNumber, Sequence = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM DocumentNumberLedger"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE DocumentNumberLedger SET CompleteNumber = 'CHANGED'"));
    }
    [Fact]
    public async Task HistoricalTermsAndAuditCannotBeRewritten()
    {
        await using var db = Database.OpenExisting();
        var a = Company();
        db.AddRange(a, new TermsVersion { CompanyId = a.Id, Text = "Synthetic terms" }, new AuditEvent { Action = "Synthetic" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE TermsVersions SET Text = 'changed'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM AuditEvents"));
    }
}
