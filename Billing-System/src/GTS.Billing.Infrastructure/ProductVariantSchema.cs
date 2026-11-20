using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class ProductVariantSchema
{
    internal static void Configure(ModelBuilder model)
    {
        var options=model.Entity<VariantOption>();
        options.ToTable("VariantOptions");
        options.HasKey(x=>x.Id);
        options.HasAlternateKey(x=>new {x.CompanyId,x.Axis,x.Id});
        options.HasIndex(x=>new{x.CompanyId,x.Axis,x.Code}).IsUnique();
        options.Property(x=>x.Version).IsConcurrencyToken();
        options.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);

        var variants=model.Entity<ProductVariant>();
        variants.ToTable("ProductVariants");
        variants.HasKey(x=>x.Id);
        variants.HasAlternateKey(x=>new {x.CompanyId,x.Id});
        variants.HasIndex(x=>new{x.CompanyId,x.ProductId,x.Code}).IsUnique();
        variants.HasIndex(x=>new{x.CompanyId,x.ProductId,x.Combination}).IsUnique();
        variants.HasIndex(x=>new{x.CompanyId,x.Sku}).IsUnique().HasFilter("Sku IS NOT NULL");
        variants.HasIndex(x=>new{x.CompanyId,x.Barcode}).IsUnique().HasFilter("Barcode IS NOT NULL");
        variants.Property(x=>x.Barcode).UseCollation("NOCASE");
        variants.Property(x=>x.Version).IsConcurrencyToken();
        variants.HasOne<Product>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.ProductId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);

        var links=model.Entity<ProductVariantOption>();
        links.ToTable("ProductVariantOptions");
        links.HasKey(x=>new{x.CompanyId,x.VariantId,x.Axis});
        links.HasOne<ProductVariant>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.VariantId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        links.HasOne<VariantOption>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.Axis,x.OptionId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Axis,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
