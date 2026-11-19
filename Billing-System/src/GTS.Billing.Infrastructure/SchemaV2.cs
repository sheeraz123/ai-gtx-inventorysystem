using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

// V2 additions only; the original migration and V1 definition remain unchanged.
internal static class SchemaV2
{
    public static void Configure(ModelBuilder m)
    {
        SchemaV1.Configure(m);
        m.Entity<Company>().Property(x=>x.RegistrationType).HasDefaultValue("Unregistered");
        m.Entity<Company>().HasOne<User>().WithMany().HasForeignKey(x=>x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<CompanyContact>().HasIndex(x=>new{x.CompanyId,x.Purpose}).IsUnique().HasFilter("Purpose IS NOT NULL");
        m.Entity<BrandingAsset>().ToTable(t=>t.HasCheckConstraint("CK_Branding_Content", "Content IS NULL OR length(Content) BETWEEN 45 AND 1048576"));
        var selection=m.Entity<CompanySelection>();
        selection.ToTable("CurrentCompany",t=>t.HasCheckConstraint("CK_CurrentCompany_Slot","Slot = 1"));
        selection.HasKey(x=>x.Slot);
        selection.Property(x=>x.Slot).ValueGeneratedNever();
        selection.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        selection.HasIndex(x=>x.CompanyId).IsUnique();
    }
}

