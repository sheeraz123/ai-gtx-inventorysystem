using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class SettingsSchema
{
    public static void Configure(ModelBuilder model)
    {
        var setting=model.Entity<SettingOverride>();
        setting.ToTable("SettingOverrides",table=>
        {
            table.HasCheckConstraint("CK_Settings_Owner","(Scope = 'Application' AND CompanyId IS NULL AND UserId IS NULL) OR (Scope = 'Company' AND CompanyId IS NOT NULL AND UserId IS NULL) OR (Scope = 'User' AND CompanyId IS NULL AND UserId IS NOT NULL)");
            table.HasCheckConstraint("CK_Settings_Value","length(Key) BETWEEN 1 AND 150 AND (Value IS NULL OR length(Value) <= 800)");
        });
        setting.HasKey(x=>x.Id);
        setting.Property(x=>x.Version).IsConcurrencyToken();
        setting.HasIndex(x=>x.Key).IsUnique().HasFilter("Scope = 'Application'");
        setting.HasIndex(x=>new {x.CompanyId,x.Key}).IsUnique().HasFilter("Scope = 'Company'");
        setting.HasIndex(x=>new {x.UserId,x.Key}).IsUnique().HasFilter("Scope = 'User'");
        setting.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        setting.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        setting.HasOne<User>().WithMany().HasForeignKey(x=>x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
