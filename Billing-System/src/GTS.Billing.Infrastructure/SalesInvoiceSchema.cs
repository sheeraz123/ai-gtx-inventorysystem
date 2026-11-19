using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class SalesInvoiceSchema
{
    internal static void Configure(ModelBuilder model)
    {
        var invoice=model.Entity<SalesInvoice>().ToTable("SalesInvoices",table=>
        {
            table.HasCheckConstraint("CK_Sales_Json","json_valid(DraftJson)");
            table.HasCheckConstraint("CK_Sales_Finalization","(LedgerId IS NULL AND Number IS NULL AND FinalizedUtc IS NULL AND FinalizedBy IS NULL) OR (LedgerId IS NOT NULL AND Number IS NOT NULL AND FinalizedUtc IS NOT NULL AND FinalizedBy IS NOT NULL AND FinancialYearId IS NOT NULL AND SeriesId IS NOT NULL AND RegistrationId IS NOT NULL AND CustomerId IS NOT NULL)");
        });
        invoice.HasKey(x=>x.Id);
        invoice.Property(x=>x.Version).IsConcurrencyToken();
        invoice.HasIndex(x=>new{x.CompanyId,x.Date});
        invoice.HasIndex(x=>x.LedgerId).IsUnique().HasFilter("LedgerId IS NOT NULL");
        invoice.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        invoice.HasOne<Customer>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.CustomerId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        invoice.HasOne<FinancialYear>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.FinancialYearId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        invoice.HasOne<DocumentNumberingSeries>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.SeriesId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        invoice.HasOne<GstRegistration>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.RegistrationId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        invoice.HasOne<DocumentNumberLedger>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.LedgerId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        foreach(var actor in new[]{"CreatedBy","UpdatedBy","FinalizedBy"})
            invoice.HasOne<User>().WithMany().HasForeignKey(actor).OnDelete(DeleteBehavior.Restrict);
    }
}
