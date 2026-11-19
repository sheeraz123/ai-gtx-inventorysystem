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
        "20261119000500_CustomerInputFlexibility"
    };
    private sealed record Marker(Guid InstallationId,string DatabasePath,string State);

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
            await db.Database.MigrateAsync(ct);
            await DatabasePreservation.VerifyIntegrityAsync(source,ct);
            DatabasePreservation.Verify(before,await DatabasePreservation.CaptureAsync(source,before,ct));
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

