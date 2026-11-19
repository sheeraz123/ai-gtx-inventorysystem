using System.Globalization;
using System.Text.RegularExpressions;

namespace GTS.Billing.Domain;

public sealed class ProductCategory : CompanyRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public Guid? ParentId { get; set; }
    public string NormalizedName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
}

public sealed class Product : CompanyRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ShortName { get; set; }
    public string? Description { get; set; }
    public string Kind { get; set; } = "Goods";
    public bool Active { get; set; } = true;
    public string? Brand { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? PartNumber { get; set; }
    public string? ModelNumber { get; set; }
    public string BaseUnit { get; set; } = "";
    public string? SalesUnit { get; set; }
    public decimal SalesFactor { get; set; } = 1;
    public string? PurchaseUnit { get; set; }
    public decimal PurchaseFactor { get; set; } = 1;
    public int QuantityPrecision { get; set; }
    public string? Hsn { get; set; }
    public decimal? GstRate { get; set; }
    public bool TaxExempt { get; set; }
    public string? TaxNotes { get; set; }
    public decimal? PurchaseRate { get; set; }
    public decimal? SalesRate { get; set; }
    public decimal? Mrp { get; set; }
    public bool SalesInclusive { get; set; }
    public bool PurchaseInclusive { get; set; }
    public string Currency { get; set; } = "INR";
    public DateOnly? PricingDate { get; set; }
    public bool StockTracked { get; set; }
    public decimal? ReorderLevel { get; set; }
    public decimal? MinimumStock { get; set; }
    public decimal? MaximumStock { get; set; }
    public string? StorageLocation { get; set; }
    public string? Specifications { get; set; }
    public string? Warranty { get; set; }
    public string? Remarks { get; set; }
    public Guid CategoryId { get; set; }
    public string NormalizedName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public byte[]? Image { get; set; }
}

public sealed record CategoryDraft
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public Guid? ParentId { get; init; }
}
public sealed record ProductDraft
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ShortName { get; set; }
    public string? Description { get; set; }
    public string Kind { get; set; } = "Goods";
    public bool Active { get; set; } = true;
    public string? Brand { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? PartNumber { get; set; }
    public string? ModelNumber { get; set; }
    public string BaseUnit { get; set; } = "";
    public string? SalesUnit { get; set; }
    public decimal SalesFactor { get; set; } = 1;
    public string? PurchaseUnit { get; set; }
    public decimal PurchaseFactor { get; set; } = 1;
    public int QuantityPrecision { get; set; }
    public string? Hsn { get; set; }
    public decimal? GstRate { get; set; }
    public bool TaxExempt { get; set; }
    public string? TaxNotes { get; set; }
    public decimal? PurchaseRate { get; set; }
    public decimal? SalesRate { get; set; }
    public decimal? Mrp { get; set; }
    public bool SalesInclusive { get; set; }
    public bool PurchaseInclusive { get; set; }
    public string Currency { get; set; } = "INR";
    public DateOnly? PricingDate { get; set; }
    public bool StockTracked { get; set; }
    public decimal? ReorderLevel { get; set; }
    public decimal? MinimumStock { get; set; }
    public decimal? MaximumStock { get; set; }
    public string? StorageLocation { get; set; }
    public string? Specifications { get; set; }
    public string? Warranty { get; set; }
    public string? Remarks { get; set; }
    public Guid CategoryId { get; init; }
}
public static class CatalogRules
{
    public static string Normalize(string? text) => (text ?? "").Trim().Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant();
    public static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static void ValidateIdentity(string? code, string? name, Dictionary<string,string> errors)
    {
        if (!Regex.IsMatch(code?.Trim() ?? "", @"\A[A-Za-z0-9_-]{1,30}\z"))
            errors["Code"] = "Use 1–30 ASCII letters, digits, hyphens or underscores.";
        CheckText("Name", name, 150, errors, required: true);
    }

    public static void CheckText(string field, string? value, int max, Dictionary<string,string> errors, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value) || value is not null && (value.Trim().Length > max || value.Contains('\0')))
            errors[field] = $"Enter {(required ? "1–" : "up to ")}{max} characters.";
    }

    public static void CheckDecimal(string field, decimal? value, Dictionary<string,string> errors, bool positive = false)
    {
        if (value is decimal number && (number < 0 || positive && number == 0 || number > 99999999999999.9999m || decimal.Round(number,4) != number))
            errors[field] = $"Enter a {(positive ? "positive" : "nonnegative")} value with up to four decimal places and 14 integral digits.";
    }

    public static void ValidateProduct(ProductDraft draft, Dictionary<string,string> errors)
    {
        ValidateIdentity(draft.Code, draft.Name, errors);
        CheckText(nameof(draft.ShortName), draft.ShortName, 100, errors);
        CheckText(nameof(draft.Description), draft.Description, 2000, errors);
        CheckText(nameof(draft.Brand), draft.Brand, 100, errors);
        CheckText(nameof(draft.Sku), draft.Sku, 100, errors);
        CheckText(nameof(draft.Barcode), draft.Barcode, 100, errors);
        CheckText(nameof(draft.PartNumber), draft.PartNumber, 100, errors);
        CheckText(nameof(draft.ModelNumber), draft.ModelNumber, 100, errors);
        CheckText(nameof(draft.SalesUnit), draft.SalesUnit, 100, errors);
        CheckText(nameof(draft.PurchaseUnit), draft.PurchaseUnit, 100, errors);
        CheckText(nameof(draft.TaxNotes), draft.TaxNotes, 2000, errors);
        CheckText(nameof(draft.StorageLocation), draft.StorageLocation, 100, errors);
        CheckText(nameof(draft.Specifications), draft.Specifications, 2000, errors);
        CheckText(nameof(draft.Warranty), draft.Warranty, 2000, errors);
        CheckText(nameof(draft.Remarks), draft.Remarks, 2000, errors);
        CheckText(nameof(draft.BaseUnit), draft.BaseUnit, 100, errors, required: true);
        CheckDecimal(nameof(draft.SalesFactor), draft.SalesFactor, errors, positive: true);
        CheckDecimal(nameof(draft.PurchaseFactor), draft.PurchaseFactor, errors, positive: true);
        CheckDecimal(nameof(draft.GstRate), draft.GstRate, errors);
        CheckDecimal(nameof(draft.PurchaseRate), draft.PurchaseRate, errors);
        CheckDecimal(nameof(draft.SalesRate), draft.SalesRate, errors);
        CheckDecimal(nameof(draft.Mrp), draft.Mrp, errors);
        CheckDecimal(nameof(draft.ReorderLevel), draft.ReorderLevel, errors);
        CheckDecimal(nameof(draft.MinimumStock), draft.MinimumStock, errors);
        CheckDecimal(nameof(draft.MaximumStock), draft.MaximumStock, errors);
        if (draft.Kind is not ("Goods" or "Services")) errors["Kind"] = "Choose Goods or Services.";
        if (draft.CategoryId == Guid.Empty) errors["CategoryId"] = "Choose an active category.";
        if (draft.QuantityPrecision is < 0 or > 4) errors["QuantityPrecision"] = "Use zero to four decimal places.";
        var baseUnit = Normalize(draft.BaseUnit);
        var sales = Normalize(Optional(draft.SalesUnit) ?? draft.BaseUnit);
        var purchase = Normalize(Optional(draft.PurchaseUnit) ?? draft.BaseUnit);
        if (sales == baseUnit && draft.SalesFactor != 1) errors["SalesFactor"] = "The base unit must have a factor of 1.";
        if (purchase == baseUnit && draft.PurchaseFactor != 1 || purchase == sales && draft.SalesFactor != draft.PurchaseFactor)
            errors["PurchaseFactor"] = "The same unit must always have the same factor. The base unit has factor 1.";
        if (Optional(draft.Hsn) is { } code && (!code.All(char.IsAsciiDigit) ||
            (draft.Kind == "Services" ? code.Length != 6 || !code.StartsWith("99", StringComparison.Ordinal) : code.Length is not (2 or 4 or 6 or 8))))
            errors["Hsn"] = "Enter a 2, 4, 6 or 8 digit goods HSN or a 6 digit service SAC starting with 99.";
        if (draft.GstRate > 100) errors["GstRate"] = "Enter a percentage no greater than 100.";
        var currency = draft.Currency?.Trim().ToUpperInvariant();
        if (currency is null || !CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Any(culture => new RegionInfo(culture.Name).ISOCurrencySymbol == currency))
            errors["Currency"] = "Enter a recognized currency code such as INR.";
        if (draft.StockTracked && draft.Kind == "Services") errors["StockTracked"] = "Services cannot be stock-tracked.";
        if (draft.MinimumStock > draft.MaximumStock) errors["MaximumStock"] = "Maximum cannot be below minimum.";
    }
}
