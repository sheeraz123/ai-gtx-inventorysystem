namespace GTS.Billing.Domain;

public sealed class SalesInvoice : CompanyRecord
{
    public DateOnly Date { get; set; }
    public Guid? FinancialYearId { get; set; }
    public Guid? SeriesId { get; set; }
    public Guid? RegistrationId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? LedgerId { get; set; }
    public string? Number { get; set; }
    public string CustomerName { get; set; } = "";
    public string PurchaseOrder { get; set; } = "";
    public string Reference { get; set; } = "";
    public string DraftJson { get; set; } = "{}";
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTime? FinalizedUtc { get; set; }
    public Guid? FinalizedBy { get; set; }
}
