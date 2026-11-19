using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class SecuritySchema
{
    internal static void Configure(ModelBuilder m)
    {
        var user=m.Entity<User>();
        user.Property(x=>x.IsLocked).HasDefaultValue(false);
        user.Property(x=>x.MustChangePassword).HasDefaultValue(false);
        user.HasOne<User>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        user.HasOne<User>().WithMany().HasForeignKey(x=>x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        user.HasIndex(x=>x.EmployeeCode);
        var role=m.Entity<Role>();
        role.Property(x=>x.Code).UseCollation("NOCASE");
        role.Property(x=>x.IsSystemRole).HasDefaultValue(false);
        role.Property(x=>x.IsActive).HasDefaultValue(true).HasSentinel(true);
        role.HasIndex(x=>x.Code).IsUnique();
        role.HasOne<User>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        role.HasOne<User>().WithMany().HasForeignKey(x=>x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        var access=m.Entity<UserCompanyAccess>();
        access.ToTable("UserCompanyAccess");
        access.HasKey(x=>new {x.UserId,x.CompanyId});
        access.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        access.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        access.HasOne<User>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
