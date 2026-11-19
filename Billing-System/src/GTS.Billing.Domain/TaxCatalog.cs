namespace GTS.Billing.Domain;

public enum TaxTreatment { Taxable, NilRated, Exempt, ZeroRated, NonGst }

public abstract class TaxCatalogRecord : CompanyRecord
{
    public bool Active { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
}

public sealed class TaxClassification : TaxCatalogRecord
{
    public string Type { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ShortDescription { get; set; }
    public string? Notes { get; set; }
}

public sealed class GstRateDefinition : TaxCatalogRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public TaxTreatment Treatment { get; set; }
    public decimal Total { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal? Utgst { get; set; }
    public decimal Cess { get; set; }
    public DateOnly From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed class TaxClassificationRateAssignment : TaxCatalogRecord
{
    public Guid ClassificationId { get; set; }
    public Guid RateId { get; set; }
    public string Context { get; set; } = "";
    public DateOnly From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed class ProductTaxProfile : CompanyRecord
{
    public Guid ProductId { get; set; }
    public Guid ClassificationId { get; set; }
    public string Context { get; set; } = "";
}
