using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class AuthenticationStore(DevelopmentDatabase database) : IAuthenticationStore
{
    public async Task<T> InTransactionAsync<T>(Func<IAuthenticationData, Task<T>> operation, CancellationToken ct)
    {
        await using var db = database.OpenExisting();
        // Microsoft.Data.Sqlite's default transaction is non-deferred: acquire the writer
        // reservation before reading setup state, throttling history, or unused codes.
        await using var transaction = await db.BeginTransactionAsync(ct);
        var result = await operation(new Data(db, database.ExpectedInstallationId(), ct));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private sealed class Data(BillingDbContext db, Guid installationId, CancellationToken ct) : IAuthenticationData
    {
        public async Task<Installation> InstallationAsync()
        {
            var row = await db.Set<Installation>().SingleAsync(ct);
            if (row.Id != installationId || row.DatabaseMode != "PlaintextDevelopment")
                throw new InvalidOperationException("Installation identity or database mode mismatch.");
            return row;
        }

        public Task<User?> FindUserAsync(string normalized) => db.Set<User>().SingleOrDefaultAsync(u => u.NormalizedUsername == normalized, ct);
        public Task<User?> FindUserAsync(Guid id) => db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, ct);
        public Task<SuperAdminAssignment?> AdminAsync() => db.Set<SuperAdminAssignment>().SingleOrDefaultAsync(ct);
        public Task<List<RecoveryCode>> UnusedCodesAsync(Guid id) =>
            db.Set<RecoveryCode>().Where(c => c.UserId == id && c.ConsumedUtc == null && c.InvalidatedUtc == null).ToListAsync(ct);
        public Task<int> RecentFailuresAsync(string action, DateTime since) =>
            db.Set<AuditEvent>().Where(e => e.CompanyId == null && e.OccurredUtc >= since && e.RecordType == "Installation" &&
                e.RecordId == installationId.ToString() && e.Action == action).Take(5).CountAsync(ct);
        public void Add(object record) => db.Add(record);
    }
}
