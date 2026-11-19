using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GTS.Billing.Infrastructure;

internal static class SchemaV6
{
    internal static void Configure(ModelBuilder model)
    {
        SchemaV5.Configure(model);
        var categories = ConfigureOwned<ProductCategory>(model, "ProductCategories");
        categories.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        categories.Property(x => x.Code).UseCollation("NOCASE");
        categories.HasIndex(x => new { x.CompanyId, x.ParentId, x.NormalizedName }).IsUnique().HasFilter("ParentId IS NOT NULL");
        categories.HasIndex(x => new { x.CompanyId, x.NormalizedName }).IsUnique().HasFilter("ParentId IS NULL");
        categories.HasOne<ProductCategory>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        categories.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Category_Code", "length(Code) BETWEEN 1 AND 30 AND Code = upper(Code) AND Code NOT GLOB '*[^A-Z0-9_-]*'");
            t.HasCheckConstraint("CK_Category_NameOrder", "length(trim(Name)) BETWEEN 1 AND 150 AND SortOrder >= 0");
            t.HasCheckConstraint("CK_Category_NotSelf", "ParentId IS NULL OR ParentId <> Id");
        });
        var products = ConfigureOwned<Product>(model, "Products");
        products.Property(x => x.Code).UseCollation("NOCASE");
        products.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        products.HasIndex(x => new { x.CompanyId, x.NormalizedName });
        products.HasIndex(x => new { x.CompanyId, x.Sku });
        products.HasIndex(x => new { x.CompanyId, x.Barcode });
        products.HasIndex(x => new { x.CompanyId, x.CategoryId, x.Active });
        products.HasOne<ProductCategory>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CategoryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        products.Property(x => x.SalesFactor).HasConversion(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v));
        products.Property(x => x.PurchaseFactor).HasConversion(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v));
        products.Property(x => x.GstRate).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v)));
        products.Property(x => x.PurchaseRate).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v)));
        products.Property(x => x.SalesRate).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v)));
        products.Property(x => x.Mrp).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v)));
        products.Property(x => x.ReorderLevel).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v)));
        products.Property(x => x.MinimumStock).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v)));
        products.Property(x => x.MaximumStock).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(v => CustomerMoneyStorage.Encode(v), v => CustomerMoneyStorage.Decode(v)));
        products.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Product_Code", "length(Code) BETWEEN 1 AND 30 AND Code = upper(Code) AND Code NOT GLOB '*[^A-Z0-9_-]*'");
            t.HasCheckConstraint("CK_Product_KindName", "Kind IN ('Goods','Services') AND length(trim(Name)) BETWEEN 1 AND 150");
            t.HasCheckConstraint("CK_Product_Stock", "(Kind = 'Goods' OR StockTracked = 0) AND ReorderLevel >= 0 OR (Kind = 'Goods' OR StockTracked = 0) AND ReorderLevel IS NULL");
            t.HasCheckConstraint("CK_Product_Thresholds", "MinimumStock >= 0 AND MaximumStock >= 0 AND MaximumStock >= MinimumStock OR MinimumStock IS NULL AND (MaximumStock IS NULL OR MaximumStock >= 0) OR MaximumStock IS NULL AND MinimumStock >= 0");
            t.HasCheckConstraint("CK_Product_Units", "length(trim(BaseUnit)) > 0 AND SalesFactor > 0 AND PurchaseFactor > 0 AND QuantityPrecision BETWEEN 0 AND 4");
            t.HasCheckConstraint("CK_Product_Gst", "GstRate IS NULL OR GstRate BETWEEN 0 AND 1000000");
            t.HasCheckConstraint("CK_Product_Prices", "(PurchaseRate IS NULL OR PurchaseRate >= 0) AND (SalesRate IS NULL OR SalesRate >= 0) AND (Mrp IS NULL OR Mrp >= 0)");
            t.HasCheckConstraint("CK_Product_Image", "Image IS NULL OR length(Image) BETWEEN 45 AND 1048576");
        });
    }

    private static EntityTypeBuilder<T> ConfigureOwned<T>(ModelBuilder model, string table) where T : CompanyRecord
    {
        var e = model.Entity<T>();
        e.ToTable(table);
        e.HasKey(x => x.Id);
        e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey("CreatedByUserId").OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey("UpdatedByUserId").OnDelete(DeleteBehavior.Restrict);
        return e;
    }
}

