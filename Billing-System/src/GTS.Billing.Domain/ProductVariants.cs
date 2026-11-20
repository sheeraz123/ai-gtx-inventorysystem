namespace GTS.Billing.Domain;

public sealed class VariantOption : CompanyRecord
{
    public string Axis { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Hex { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid UpdatedBy { get; set; }
}

public sealed class ProductVariant : CompanyRecord
{
    public Guid ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Combination { get; set; } = "";
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public decimal? Mrp { get; set; }
    public decimal? SalesRate { get; set; }
    public decimal? PurchaseRate { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid UpdatedBy { get; set; }
}

public sealed class ProductVariantOption
{
    public Guid CompanyId { get; set; }
    public Guid VariantId { get; set; }
    public Guid OptionId { get; set; }
    public string Axis { get; set; } = "";
}
