using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class NumberingSchema
{
    internal static void Configure(ModelBuilder model)
    {
        var year=model.Entity<FinancialYear>();
        year.Metadata.RemoveCheckConstraint("CK_FY_Boundaries");
        year.ToTable(t=>
        {
            t.HasCheckConstraint("CK_FY_Boundaries","date(Start) IS NOT NULL AND date(End) IS NOT NULL AND End > Start");
            t.HasCheckConstraint("CK_FY_Current","IsCurrent IN (0,1) AND (IsCurrent=0 OR Status='Open')");
        });
        year.HasIndex(x=>new{x.CompanyId,x.Code}).IsUnique();
        year.HasIndex(x=>x.CompanyId,"CurrentFinancialYear").IsUnique().HasFilter("IsCurrent=1");
        foreach(var actor in new[]{nameof(FinancialYear.CreatedBy),nameof(FinancialYear.UpdatedBy),
            nameof(FinancialYear.ClosedBy),nameof(FinancialYear.ReopenedBy)})
            year.HasOne<User>().WithMany().HasForeignKey(actor).OnDelete(DeleteBehavior.Restrict);

        var series=model.Entity<DocumentNumberingSeries>();
        series.Metadata.RemoveCheckConstraint("CK_Series_Range");
        series.Metadata.RemoveIndex([series.Property(x=>x.CompanyId).Metadata,series.Property(x=>x.GstRegistrationId).Metadata,
            series.Property(x=>x.DocumentType).Metadata]);
        series.HasOne<FinancialYear>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.FinancialYearId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        series.HasIndex(x=>new{x.CompanyId,x.FinancialYearId,x.GstRegistrationId,x.DocumentType,x.Code}).IsUnique();
        series.HasIndex(x=>new{x.CompanyId,x.FinancialYearId,x.GstRegistrationId,x.DocumentType}).IsUnique().HasFilter("IsDefault=1");
        series.ToTable(t=>
        {
            t.HasCheckConstraint("CK_Series_Range","StartingNumber > 0 AND Padding BETWEEN 1 AND 9 AND length(Prefix)<=80 AND length(Suffix)<=80 AND Separator IN ('','-','/')");
            t.HasCheckConstraint("CK_Series_Default","IsDefault IN (0,1) AND IsActive IN (0,1) AND (IsDefault=0 OR IsActive=1)");
        });
        series.HasOne<User>().WithMany().HasForeignKey(x=>x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        series.HasOne<User>().WithMany().HasForeignKey(x=>x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
