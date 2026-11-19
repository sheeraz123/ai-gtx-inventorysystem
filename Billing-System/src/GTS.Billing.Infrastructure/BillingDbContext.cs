using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GTS.Billing.Infrastructure;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options), IUnitOfWork, ICompanyReader
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        SchemaV6.Configure(modelBuilder);
        var gst=modelBuilder.Entity<GstRegistration>();
        gst.Property(x=>x.IsInvoiceDefault).HasDefaultValue(false);
        gst.HasIndex(x=>x.CompanyId,"InvoiceTaxDefault").HasDatabaseName("UX_GstRegistrations_InvoiceDefault").IsUnique().HasFilter("IsInvoiceDefault = 1");
        gst.ToTable(t=>t.HasCheckConstraint("CK_Gst_InvoiceDefault","IsInvoiceDefault IN (0,1) AND (IsInvoiceDefault = 0 OR IsActive = 1)"));
        SecuritySchema.Configure(modelBuilder);
        SettingsSchema.Configure(modelBuilder);
        UnitBrandSchema.Configure(modelBuilder);
        TaxSchema.Configure(modelBuilder);
        NumberingSchema.Configure(modelBuilder);
        SalesInvoiceSchema.Configure(modelBuilder);
    }
    public async Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Set<Company>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IDataTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new DataTransaction(await Database.BeginTransactionAsync(cancellationToken));
    private sealed class DataTransaction(IDbContextTransaction transaction) : IDataTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default) => transaction.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Record>().Where(e => e.State == EntityState.Modified))
            entry.Entity.Version = checked(entry.Property(x => x.Version).OriginalValue + 1);
        return base.SaveChangesAsync(cancellationToken);
    }
}
