namespace GTS.Billing.Domain;

public interface ICatalogMaster
{
    string Code { get; set; }
    string Name { get; set; }
    string NormalizedName { get; set; }
    string? Description { get; set; }
    int SortOrder { get; set; }
    bool Active { get; set; }
    DateTime CreatedUtc { get; set; }
    DateTime UpdatedUtc { get; set; }
    Guid CreatedByUserId { get; set; }
    Guid UpdatedByUserId { get; set; }
}

public sealed class UnitOfMeasure : CompanyRecord, ICatalogMaster
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public string? Symbol { get; set; }
    public string UnitType { get; set; } = "Count";
    public int DecimalPrecision { get; set; }
    public string? Uqc { get; set; }
}

public sealed class Brand : CompanyRecord, ICatalogMaster
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public string? Manufacturer { get; set; }
    public byte[]? Logo { get; set; }
}

public sealed class ProductMeasureProfile : CompanyRecord
{
    public Guid ProductId { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? BaseUnitId { get; set; }
    public string? Classification { get; set; }
}

public sealed class ProductUnitConversion : CompanyRecord
{
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    public decimal FactorToBaseUnit { get; set; } = 1;
    public bool DefaultSales { get; set; }
    public bool DefaultPurchase { get; set; }
    public bool Active { get; set; } = true;
}
