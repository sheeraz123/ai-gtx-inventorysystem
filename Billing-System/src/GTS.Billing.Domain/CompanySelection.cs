namespace GTS.Billing.Domain;

// One installation-wide choice, independent of whether other company rows are active.
public sealed class CompanySelection
{
    public int Slot { get; set; } = 1;
    public Guid CompanyId { get; set; }
}
