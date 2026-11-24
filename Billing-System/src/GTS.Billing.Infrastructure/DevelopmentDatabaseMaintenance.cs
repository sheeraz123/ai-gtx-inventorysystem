using System.Security.Cryptography;
using System.Text.Json;
using GTS.Billing.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed record DevelopmentUpgradeReceipt(string BackupPath,string EvidencePath,IReadOnlyList<string> AppliedMigrations);

// Caller must retain the shipped global ownership for this entire operation.
// Only reviewed plaintext DEVELOPMENT schema changes are supported, after explicit operator consent.
public sealed class DevelopmentDatabaseMaintenance(DevelopmentDatabase database,IDatabaseLocation location)
{
    private static readonly HashSet<string> Reviewed = new(StringComparer.Ordinal)
    {
        "20261118000100_CompanyMaster", "20261118000200_CompanyInputFlexibility",
        "20261119000100_CustomerMaster", "20261119000200_CompanyPresentation",
        "20261119000300_ProductCatalog", "20261119000400_InvoiceTaxDefault",
        "20261119000500_CustomerInputFlexibility", "20261119000600_SecurityAdministration",
        "20261119000700_ApplicationSettings", "20261119000800_UnitBrandMaster", TaxCatalogMigration, NumberingMigration,
        SalesInvoiceMigration, ProductVariantMigration, "20261124000100_CustomerAadhaarSuffix"
    };
    private sealed record Marker(Guid InstallationId,string DatabasePath,string State);
    private const string UnitBrandMigration="20261119000800_UnitBrandMaster";
    private const string TaxCatalogMigration="20261119000900_TaxCatalog";
    private const string NumberingMigration="20261119001000_FinancialYearNumbering";
    private const string SalesInvoiceMigration="20261119001100_SalesInvoiceEntry";
    private const string ProductVariantMigration="20261120000100_ProductVariants";
    private sealed record BackupEvidence(string Status,string BackupSha256,string MarkerSha256,
        string[] ApprovedMigrations,bool PlaintextDevelopmentOnly);

    public async Task<IReadOnlyList<string>> GetReviewedPendingAsync(CancellationToken ct=default)
    {
        await using var db=database.OpenExisting();
        await database.VerifyHealthAsync(db,ct);
        var expected=db.Database.GetMigrations().ToArray();
        var applied=(await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var mismatch=DatabaseStartupException.CompareHistory(expected,applied);
        if(mismatch is null) return [];
        if(mismatch.Problem!=DatabaseStartupProblem.UpgradeRequired || !mismatch.PendingMigrations.All(Reviewed.Contains))
            throw mismatch;
        return mismatch.PendingMigrations;
    }

    public async Task<DevelopmentUpgradeReceipt> ApplyReviewedAsync(IReadOnlyList<string> approvedMigrations,CancellationToken ct=default)
    {
        var pending=await GetReviewedPendingAsync(ct);
        if(pending.Count==0 || !pending.SequenceEqual(approvedMigrations))
            throw new InvalidOperationException("Database upgrade approval is stale.");
        var path=location.GetAbsolutePath();var markerPath=path+".installation.json";
        var originalMarker=await File.ReadAllBytesAsync(markerPath,ct);
        var marker=JsonSerializer.Deserialize<Marker>(originalMarker) ?? throw new DatabaseStartupException(DatabaseStartupProblem.InvalidMarker);
        var migratingMarker=JsonSerializer.SerializeToUtf8Bytes(marker with {State="Migrating"});
        var directory=Path.Combine(Path.GetDirectoryName(path)!,".local","database-upgrades",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        var backup=DatabasePathPolicy.Validate(Path.Combine(directory,"before-upgrade.db"));
        Directory.CreateDirectory(directory);
        await using var db=database.OpenExisting();
        await db.Database.OpenConnectionAsync(ct);
        var source=(SqliteConnection)db.Database.GetDbConnection();
        var before=await DatabasePreservation.CaptureAsync(source,null,ct);
        using(new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { }
        await using(var destination=Connection(backup,SqliteOpenMode.ReadWrite))
        {
            await destination.OpenAsync(ct);source.BackupDatabase(destination);
        }
        var backupMarker=Path.Combine(directory,"before-upgrade.installation.json");
        await WriteNewAsync(backupMarker,originalMarker,ct);
        await using(var copy=Connection(backup,SqliteOpenMode.ReadOnly))
        {
            await copy.OpenAsync(ct);
            await DatabasePreservation.VerifyIntegrityAsync(copy,ct);
            DatabasePreservation.Verify(before,await DatabasePreservation.CaptureAsync(copy,before,ct));
        }
        var backupHash=await HashAsync(backup,ct);
        await WriteJsonAsync(Path.Combine(directory,"verified-backup.json"),new {
            Status="VerifiedBeforeMigration",CreatedUtc=DateTime.UtcNow,BackupSha256=backupHash,
            MarkerSha256=Convert.ToHexString(SHA256.HashData(originalMarker)),ApprovedMigrations=pending,PlaintextDevelopmentOnly=true
        },ct);
        // Recheck history and identity after making the backup; approval never authorizes another pending set.
        var stillPending=await GetReviewedPendingAsync(ct);
        if(!stillPending.SequenceEqual(pending) || !Enumerable.SequenceEqual(originalMarker,await File.ReadAllBytesAsync(markerPath,ct)))
            throw new InvalidOperationException("Database state changed before maintenance.");
        var maintenanceStarted=false;
        try
        {
            ct.ThrowIfCancellationRequested();
            await ReplaceMarkerAsync(markerPath,originalMarker,migratingMarker);
            maintenanceStarted=true;
            // No application entry point can open a Migrating installation, including older builds.
            if(pending.Contains(NumberingMigration,StringComparer.Ordinal))
            {
                if(pending.Count!=1) throw new InvalidOperationException("The numbering rebuild requires a dedicated migration run.");
                await MigrateNumberingAsync(db,source,ct);
            }
            else await db.Database.MigrateAsync(ct);
            await DatabasePreservation.VerifyIntegrityAsync(source,ct);
            await VerifyPreservedAsync(source,backup,before,pending,ct);
            if(!(await db.Database.GetAppliedMigrationsAsync(ct)).SequenceEqual(db.Database.GetMigrations()))
                throw new DatabaseStartupException(DatabaseStartupProblem.UnrecognizedHistory);
            if(backupHash!=await HashAsync(backup,ct)) throw new InvalidOperationException("Safety backup changed.");
            await ReplaceMarkerAsync(markerPath,migratingMarker,originalMarker);
            await database.VerifyExistingAsync(ct);
            var evidence=Path.Combine(directory,"completed-upgrade.json");
            await WriteJsonAsync(evidence,new {
                Status="Complete",VerifiedUtc=DateTime.UtcNow,AppliedMigrations=pending,BackupSha256=backupHash,
                PriorRowsAndValuesPreserved=true,InstallationAndMarkerPreserved=true,Integrity="ok",ForeignKeys="ok",
                PlaintextDevelopmentOnly=true
            },ct);
            maintenanceStarted=false;
            return new(backup,evidence,pending);
        }
        catch
        {
            if(maintenanceStarted)
            {
                // Never silently restore over uncertain data. Keep both old/new executables blocked.
                var current=await File.ReadAllBytesAsync(markerPath,CancellationToken.None);
                if(current.SequenceEqual(originalMarker)) await ReplaceMarkerAsync(markerPath,originalMarker,migratingMarker);
            }
            throw;
        }
    }

    private static async Task VerifyPreservedAsync(SqliteConnection live,string backup,
        IReadOnlyList<DatabaseTableFingerprint> before,IReadOnlyList<string> pending,CancellationToken ct)
    {
        var added=new List<string>();
        var catalogAdditions=new List<(string Code,string Name)>();
        if(pending.Contains(UnitBrandMigration,StringComparer.Ordinal))
            catalogAdditions.AddRange([("unit","Unit Master"),("brand","Brand Master")]);
        if(pending.Contains(TaxCatalogMigration,StringComparer.Ordinal))
            catalogAdditions.AddRange([("hsnsac","HSN/SAC Master"),("tax","GST Rate Master")]);
        if(pending.Contains(NumberingMigration,StringComparer.Ordinal))
            catalogAdditions.AddRange([("financialyear","Financial Year Master"),("documentseries","Document Series Master")]);
        if(pending.Contains(SalesInvoiceMigration,StringComparer.Ordinal))
            catalogAdditions.Add(("invoice","Sales Invoice"));
        if(pending.Contains(ProductVariantMigration,StringComparer.Ordinal))
            catalogAdditions.AddRange([("product-variants","Product Variants"),("product-sizes","Size Master"),
                ("product-colors","Color Master"),("variant-attributes","Variant Attributes")]);
        if(catalogAdditions.Count>0)
        {
            await using var copy=Connection(backup,SqliteOpenMode.ReadOnly);
            await copy.OpenAsync(ct);
            foreach(var (code,name) in catalogAdditions)
            {
                async Task<string?> ReadName(SqliteConnection connection)
                {
                    await using var command=connection.CreateCommand();
                    command.CommandText="SELECT Name FROM Modules WHERE Code=$code";
                    command.Parameters.AddWithValue("$code",code);
                    return await command.ExecuteScalarAsync(ct) as string;
                }
                if(await ReadName(copy) is not null) continue;
                if(await ReadName(live)!=name) throw new InvalidOperationException("Expected permission catalog entry is missing or changed.");
                added.Add(code);
            }
        }
        DatabasePreservation.Verify(before,await DatabasePreservation.CaptureAsync(live,before,ct,added));
    }

    public async Task CompleteUnitBrandRecoveryAsync(string backupFolder,CancellationToken ct)
    {
        if(!Guid.TryParseExact(backupFolder.Length==48?backupFolder[16..]:"","N",out _) ||
            !DateTime.TryParseExact(backupFolder[..15],"yyyyMMdd-HHmmss",null,System.Globalization.DateTimeStyles.None,out _) ||
            backupFolder[15]!='-')
            throw new ArgumentException("Invalid backup folder name.");
        var path=location.GetAbsolutePath();
        var markerPath=path+".installation.json";
        var directory=Path.Combine(Path.GetDirectoryName(path)!,".local","database-upgrades",backupFolder);
        var backup=DatabasePathPolicy.Validate(Path.Combine(directory,"before-upgrade.db"));
        var receipt=JsonSerializer.Deserialize<BackupEvidence>(await File.ReadAllBytesAsync(Path.Combine(directory,"verified-backup.json"),ct));
        var original=await File.ReadAllBytesAsync(Path.Combine(directory,"before-upgrade.installation.json"),ct);
        var current=await File.ReadAllBytesAsync(markerPath,ct);
        var marker=JsonSerializer.Deserialize<Marker>(original);
        if(receipt is not {Status:"VerifiedBeforeMigration",PlaintextDevelopmentOnly:true} ||
            !receipt.ApprovedMigrations.SequenceEqual([UnitBrandMigration]) ||
            receipt.BackupSha256!=await HashAsync(backup,ct) ||
            receipt.MarkerSha256!=Convert.ToHexString(SHA256.HashData(original)) ||
            marker is not {State:"Ready"} || marker.InstallationId==Guid.Empty ||
            !string.Equals(marker.DatabasePath,path,StringComparison.OrdinalIgnoreCase) ||
            !current.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(marker with {State="Migrating"})))
            throw new InvalidOperationException("Recovery evidence does not match this installation.");

        await using(var copy=Connection(backup,SqliteOpenMode.ReadOnly))
        await using(var live=Connection(path,SqliteOpenMode.ReadOnly))
        {
            await copy.OpenAsync(ct);
            await live.OpenAsync(ct);
            await DatabasePreservation.VerifyIntegrityAsync(copy,ct);
            await DatabasePreservation.VerifyIntegrityAsync(live,ct);
            using var model=DevelopmentDatabase.CreateContext(path,false);
            var expected=model.Database.GetMigrations().ToArray();
            async Task<string[]> History(SqliteConnection connection)
            {
                await using var command=connection.CreateCommand();
                command.CommandText="SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
                await using var rows=await command.ExecuteReaderAsync(ct);
                var history=new List<string>();
                while(await rows.ReadAsync(ct)) history.Add(rows.GetString(0));
                return history.ToArray();
            }
            if(expected.LastOrDefault()!=UnitBrandMigration ||
                !(await History(copy)).SequenceEqual(expected[..^1]) ||
                !(await History(live)).SequenceEqual(expected))
                throw new InvalidOperationException("Migration history does not match the approved recovery.");
            var before=await DatabasePreservation.CaptureAsync(copy,null,ct);
            await VerifyPreservedAsync(live,backup,before,[UnitBrandMigration],ct);
        }

        ct.ThrowIfCancellationRequested();
        await ReplaceMarkerAsync(markerPath,current,original);
        try
        {
            await database.VerifyExistingAsync(ct);
            await WriteJsonAsync(Path.Combine(directory,"completed-recovery.json"),new
            {
                Status="Complete",VerifiedUtc=DateTime.UtcNow,AppliedMigration=UnitBrandMigration,
                receipt.BackupSha256,PriorRowsAndValuesPreserved=true,Integrity="ok",ForeignKeys="ok"
            },ct);
        }
        catch
        {
            await ReplaceMarkerAsync(markerPath,original,current);
            throw;
        }
    }

    private static async Task MigrateNumberingAsync(BillingDbContext db,SqliteConnection connection,CancellationToken ct)
    {
        // OpenExisting uses a non-pooled connection. The caller holds exclusive ownership and a verified backup.
        // EF owns the single DDL and history transaction. PRAGMA changes belong outside that transaction.
        await using var command=connection.CreateCommand();
        command.CommandText="PRAGMA foreign_keys=OFF";
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            await db.Database.MigrateAsync(NumberingMigration,ct);
        }
        finally
        {
            command.CommandText="PRAGMA foreign_keys=ON";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            command.CommandText="PRAGMA foreign_keys";
            if(Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None))!=1)
                throw new InvalidOperationException("Foreign-key enforcement could not be restored.");
        }
    }

    private static SqliteConnection Connection(string path,SqliteOpenMode mode)=>new(new SqliteConnectionStringBuilder {
        DataSource=path,Mode=mode,ForeignKeys=true,Pooling=false,DefaultTimeout=5 }.ToString());
    private static async Task<string> HashAsync(string path,CancellationToken ct)
    {
        await using var input=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(input,ct));
    }
    private static Task WriteJsonAsync(string path,object value,CancellationToken ct)=>
        WriteNewAsync(path,JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions {WriteIndented=true}),ct);
    private static async Task WriteNewAsync(string path,byte[] bytes,CancellationToken ct)
    {
        await using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        await file.WriteAsync(bytes,ct);file.Flush(flushToDisk:true);
    }
    private static async Task ReplaceMarkerAsync(string path,byte[] expected,byte[] replacement)
    {
        if(!Enumerable.SequenceEqual(expected,await File.ReadAllBytesAsync(path)))
            throw new InvalidOperationException("Installation marker changed.");
        var temporary=path+".maintenance-"+Guid.NewGuid().ToString("N")+".installation.json";
        await WriteNewAsync(temporary,replacement,CancellationToken.None);
        File.Replace(temporary,path,null); // Atomic same-volume replacement; retains destination security metadata on Windows.
    }
}

