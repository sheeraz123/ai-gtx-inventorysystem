namespace GTS.Billing.Domain;

public sealed class UserCompanyAccess
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}
