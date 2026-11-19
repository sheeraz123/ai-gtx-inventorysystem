using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GTS.Billing.Infrastructure;

internal static class TaxSchema
{
    public static void Configure(ModelBuilder model)
    {
        var classification = Catalog<TaxClassification>(model,"TaxClassifications");
        classification.HasIndex(x=>new{x.CompanyId,x.Type,x.Code}).IsUnique();
        classification.ToTable(t=>t.HasCheckConstraint("CK_TaxClassification_Code",
            "Code NOT GLOB '*[^0-9]*' AND ((Type='HSN' AND length(Code) IN (2,4,6,8)) OR (Type='SAC' AND length(Code)=6 AND substr(Code,1,2)='99'))"));

        var rate = Catalog<GstRateDefinition>(model,"GstRateDefinitions");
        rate.HasIndex(x=>new{x.CompanyId,x.Code,x.From}).IsUnique();
        rate.Property(x=>x.Treatment).HasConversion<string>();
        Percentage(rate.Property(x=>x.Total));
        Percentage(rate.Property(x=>x.Cgst));
        Percentage(rate.Property(x=>x.Sgst));
        Percentage(rate.Property(x=>x.Igst));
        Percentage(rate.Property(x=>x.Cess));
        rate.Property(x=>x.Utgst).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal,long>(
            x=>decimal.ToInt64(x*10000m), x=>x/10000m));
        rate.ToTable(t=>
        {
            t.HasCheckConstraint("CK_GstRate_Dates","\"To\" IS NULL OR \"To\" > \"From\"");
            t.HasCheckConstraint("CK_GstRate_Percentages","Total BETWEEN 0 AND 1000000 AND Cgst BETWEEN 0 AND 1000000 AND Sgst BETWEEN 0 AND 1000000 AND Igst BETWEEN 0 AND 1000000 AND Cess BETWEEN 0 AND 1000000 AND (Utgst IS NULL OR Utgst BETWEEN 0 AND 1000000)");
            t.HasCheckConstraint("CK_GstRate_Treatment",
                "(Treatment='Taxable' AND Total>0 AND Cgst+Sgst=Total AND Igst=Total AND (Utgst IS NULL OR Cgst+Utgst=Total)) OR (Treatment IN ('NilRated','Exempt','ZeroRated','NonGst') AND Total=0 AND Cgst=0 AND Sgst=0 AND Igst=0 AND Cess=0 AND coalesce(Utgst,0)=0)");
        });

        var assignment = Catalog<TaxClassificationRateAssignment>(model,"TaxClassificationRateAssignments");
        assignment.HasIndex(x=>new{x.CompanyId,x.ClassificationId,x.Context,x.From}).IsUnique();
        assignment.HasOne<TaxClassification>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.ClassificationId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<GstRateDefinition>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.RateId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        assignment.ToTable(t=>t.HasCheckConstraint("CK_TaxAssignment_Dates","\"To\" IS NULL OR \"To\" > \"From\""));

        var profile = model.Entity<ProductTaxProfile>().ToTable("ProductTaxProfiles");
        profile.HasKey(x=>x.Id);
        profile.Property(x=>x.Version).IsConcurrencyToken();
        profile.HasIndex(x=>new{x.CompanyId,x.ProductId}).IsUnique();
        profile.HasOne<Product>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.ProductId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        profile.HasOne<TaxClassification>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.ClassificationId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }

    private static void Percentage(PropertyBuilder<decimal> property) =>
        property.HasConversion(x=>decimal.ToInt64(x*10000m), x=>x/10000m);

    private static EntityTypeBuilder<T> Catalog<T>(ModelBuilder model,string table) where T:TaxCatalogRecord
    {
        var entity=model.Entity<T>().ToTable(table);
        entity.HasKey(x=>x.Id);
        entity.HasAlternateKey(x=>new{x.CompanyId,x.Id});
        entity.Property(x=>x.Version).IsConcurrencyToken();
        entity.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<User>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<User>().WithMany().HasForeignKey(x=>x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        return entity;
    }
}
