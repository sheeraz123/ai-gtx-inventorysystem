namespace GTS.Billing.Domain;

public sealed class SettingOverride : Record
{
    public string Scope { get; set; }="";
    public string Key { get; set; }="";
    public Guid? CompanyId { get; set; }
    public Guid? UserId { get; set; }
    public string? Value { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
}
