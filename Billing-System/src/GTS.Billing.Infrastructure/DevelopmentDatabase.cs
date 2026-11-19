using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class ConfiguredDatabaseLocation(string? path) : IDatabaseLocation
{
    public string GetAbsolutePath()
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("An explicit absolute development database path is required. No per-user fallback exists.");
        return DatabasePathPolicy.Validate(path);
    }
}

public sealed class DevelopmentDatabase(IDatabaseLocation location) : IDevelopmentDatabase
{
    private sealed record InstallationMarker(Guid InstallationId, string DatabasePath, string State);
    private string PathValue => location.GetAbsolutePath();
    private string MarkerPath => PathValue + ".installation.json";

    public BillingDbContext OpenExisting()
    {
        _ = ExpectedInstallationId();
        return CreateContext(PathValue, false);
    }

    internal Guid ExpectedInstallationId()
    {
        if (!File.Exists(PathValue)) throw new DatabaseStartupException(DatabaseStartupProblem.MissingDatabase);
        if (!File.Exists(MarkerPath)) throw new DatabaseStartupException(DatabaseStartupProblem.InvalidMarker);
        if (new FileInfo(MarkerPath).Length > 16384) throw new DatabaseStartupException(DatabaseStartupProblem.InvalidMarker);
        InstallationMarker? marker;
        try { marker = JsonSerializer.Deserialize<InstallationMarker>(File.ReadAllText(MarkerPath)); }
        catch (JsonException) { throw new DatabaseStartupException(DatabaseStartupProblem.InvalidMarker); }
        if (marker?.State == "Migrating") throw new DatabaseStartupException(DatabaseStartupProblem.MaintenanceInProgress);
        if (marker is null || marker.InstallationId == Guid.Empty || marker.State != "Ready" || !string.Equals(marker.DatabasePath, PathValue, StringComparison.OrdinalIgnoreCase))
            throw new DatabaseStartupException(DatabaseStartupProblem.InvalidMarker);
        return marker.InstallationId;
    }

    public static BillingDbContext CreateContext(string path, bool allowCreate = false)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = allowCreate ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        };
        return new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(connection.ToString()).Options);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Deliberate initialization only. CreateNew reserves the marker across processes;
        // interrupted initialization stays blocked instead of silently starting again.
        if (File.Exists(PathValue) || File.Exists(MarkerPath))
            throw new InvalidOperationException("Development initialization refuses an existing database or marker.");
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        var id = Guid.NewGuid();
        using (var reservation = new FileStream(MarkerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(reservation, new InstallationMarker(id, PathValue, "Initializing"));
        // Reserve the actual database file too; never migrate a file another initializer created.
        using (new FileStream(PathValue, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }
        await using var db = CreateContext(PathValue, true);
        await db.Database.MigrateAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Add(new Installation { Id = id });
        db.Add(new DatabaseSecurityMetadata { InstallationId = id });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var temp = MarkerPath + ".pending";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new InstallationMarker(id, PathValue, "Ready")), cancellationToken);
        File.Move(temp, MarkerPath, true);
    }

    public async Task VerifyExistingAsync(CancellationToken cancellationToken = default)
    {
        await using var db = OpenExisting();
        var expected = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (DatabaseStartupException.CompareHistory(expected, applied) is { } mismatch) throw mismatch;
        await VerifyHealthAsync(db, cancellationToken);
    }

    internal async Task VerifyHealthAsync(BillingDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new DatabaseStartupException(DatabaseStartupProblem.IntegrityFailure);
        command.CommandText = "PRAGMA integrity_check;";
        if (!Equals(await command.ExecuteScalarAsync(cancellationToken), "ok"))
            throw new DatabaseStartupException(DatabaseStartupProblem.IntegrityFailure);
        command.CommandText = "PRAGMA foreign_key_check;";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            if (await reader.ReadAsync(cancellationToken))
                throw new DatabaseStartupException(DatabaseStartupProblem.IntegrityFailure);
        var marker = JsonSerializer.Deserialize<InstallationMarker>(await File.ReadAllTextAsync(MarkerPath, cancellationToken))!;
        var installation = await db.Set<Installation>().SingleAsync(cancellationToken);
        if (installation.Id != marker.InstallationId || installation.DatabaseMode != "PlaintextDevelopment")
            throw new DatabaseStartupException(DatabaseStartupProblem.IdentityMismatch);
    }
}
