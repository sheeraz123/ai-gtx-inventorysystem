using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class SchemaV5
{
    public static void Configure(ModelBuilder model)
    {
        SchemaV4.Configure(model);
        var logo = model.Entity<BrandingAsset>();
        logo.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
        logo.Property(x => x.IsRetired).HasDefaultValue(false);
        logo.Property(x => x.SortOrder).HasDefaultValue(0);
        logo.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        logo.HasIndex(x => new { x.CompanyId, x.SortOrder, x.Id });
        logo.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Branding_Thumbnail", "Thumbnail IS NULL OR length(Thumbnail) BETWEEN 45 AND 65536");
            t.HasCheckConstraint("CK_Branding_Active", "IsActive IN (0,1)");
            t.HasCheckConstraint("CK_Branding_Retired", "IsRetired IN (0,1) AND (IsRetired = 0 OR IsActive = 0)");
        });

        var bank = model.Entity<CompanyBankAccount>();
        bank.Property(x => x.DisplayOrder).HasDefaultValue(0);
        bank.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        bank.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        bank.HasIndex(x => new { x.CompanyId, x.DisplayOrder, x.Id });

        var settings = model.Entity<CompanySettings>();
        settings.Property(x => x.ShowPrimaryLogo).HasDefaultValue(false);
        settings.Property(x => x.ShowSecondaryLogo).HasDefaultValue(false);
        settings.Property(x => x.ShowBankDetails).HasDefaultValue(false);
        settings.Property(x => x.ShowUpi).HasDefaultValue(false);
        settings.ToTable(t =>
        {
            t.HasCheckConstraint("CK_CompanySettings_ShowPrimary", "ShowPrimaryLogo IN (0,1)");
            t.HasCheckConstraint("CK_CompanySettings_ShowSecondary", "ShowSecondaryLogo IN (0,1)");
            t.HasCheckConstraint("CK_CompanySettings_ShowBank", "ShowBankDetails IN (0,1)");
            t.HasCheckConstraint("CK_CompanySettings_ShowUpi", "ShowUpi IN (0,1)");
        });
    }
}
