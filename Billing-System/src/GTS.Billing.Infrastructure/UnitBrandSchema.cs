using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GTS.Billing.Infrastructure;

internal static class UnitBrandSchema
{
    internal static void Configure(ModelBuilder model)
    {
        var units = Master<UnitOfMeasure>(model, "Units");
        units.ToTable(t => t.HasCheckConstraint("CK_Units_Precision", "DecimalPrecision BETWEEN 0 AND 6"));
        Master<Brand>(model, "Brands");
        var profile = model.Entity<ProductMeasureProfile>().ToTable("ProductMeasureProfiles");
        profile.HasKey(x => x.Id);
        profile.Property(x => x.Version).IsConcurrencyToken();
        profile.HasIndex(x => new { x.CompanyId, x.ProductId }).IsUnique();
        profile.HasOne<Product>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ProductId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        profile.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BaseUnitId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        profile.HasOne<Brand>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BrandId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var conversion = model.Entity<ProductUnitConversion>().ToTable("ProductUnitConversions", t =>
        {
            t.HasCheckConstraint("CK_Conversions_Factor", "CAST(FactorToBaseUnit AS REAL) > 0");
            t.HasCheckConstraint("CK_Conversions_Defaults", "Active = 1 OR (DefaultSales = 0 AND DefaultPurchase = 0)");
        });
        conversion.HasKey(x => x.Id);
        conversion.Property(x => x.Version).IsConcurrencyToken();
        conversion.HasIndex(x => new { x.CompanyId, x.ProductId, x.UnitId }).IsUnique();
        conversion.HasIndex(x => new { x.CompanyId, x.ProductId }, "DefaultSales").IsUnique().HasFilter("DefaultSales = 1");
        conversion.HasIndex(x => new { x.CompanyId, x.ProductId }, "DefaultPurchase").IsUnique().HasFilter("DefaultPurchase = 1");
        conversion.HasOne<Product>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ProductId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        conversion.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => new { x.CompanyId, x.UnitId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }

    private static EntityTypeBuilder<T> Master<T>(ModelBuilder model, string table) where T : CompanyRecord, ICatalogMaster
    {
        var entity = model.Entity<T>().ToTable(table);
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.NormalizedName }).IsUnique();
        entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        return entity;
    }
}
