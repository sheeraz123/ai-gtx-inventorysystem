namespace GTS.Billing.Domain;

// Schema records only. Business workflows and authentication are not implemented.
public abstract class Record
{
    public Guid Id { get; set; } = Guid.NewGuid(); public long Version
    {
        get; set;
    }
}
public abstract class CompanyRecord : Record
{
    public Guid CompanyId
    {
        get; set;
    }
}
public sealed class Installation : Record
{
    public string SetupState { get; set; } = "NotConfigured"; public string DatabaseMode { get; set; } = "PlaintextDevelopment"; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public sealed class User : Record
{
    public string? FullName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? MobileNumber { get; set; }
    public string? EmployeeCode { get; set; }
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public bool IsLocked { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? LastSuccessfulLoginUtc { get; set; }
    public DateTime? LastPasswordChangedUtc { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public string Username { get; set; } = ""; public string NormalizedUsername { get; set; } = ""; public string PasswordHash { get; set; } = ""; public bool IsActive { get; set; } = true;
}
public sealed class SuperAdminAssignment
{
    public int SingletonId { get; set; } = 1; public Guid UserId
    {
        get; set;
    }
}
public sealed class Role : Record
{
    public string? Code { get; set; }
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class Module
{
    public string Code { get; set; } = ""; public string Name { get; set; } = "";
}
public sealed class UserRole : Record
{
    public Guid UserId
    {
        get; set;
    }
    public Guid RoleId
    {
        get; set;
    }
    public Guid CompanyId
    {
        get; set;
    }
}
public sealed class RolePermission : Record
{
    public Guid RoleId
    {
        get; set;
    }
    public string ModuleCode { get; set; } = ""; public string Action { get; set; } = "";
}
public sealed class UserPermissionOverride : CompanyRecord
{
    public Guid UserId
    {
        get; set;
    }
    public string ModuleCode { get; set; } = ""; public string Action { get; set; } = ""; public bool IsAllowed
    {
        get; set;
    }
}
public sealed class RecoveryCode : Record
{
    public Guid UserId
    {
        get; set;
    }
    public string CodeHash { get; set; } = ""; public DateTime? ConsumedUtc
    {
        get; set;
    }
    public DateTime? InvalidatedUtc
    {
        get; set;
    }
}
public sealed class Company : Record
{
    public string? BusinessType { get; set; }
    public DateOnly? EstablishedOn { get; set; }
    public string RegistrationType { get; set; } = "Unregistered";
    public string? Tan { get; set; }
    public string? Cin { get; set; }
    public string? Udyam { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public string Code { get; set; } = ""; public string LegalName { get; set; } = ""; public string? DisplayName
    {
        get; set;
    }
    public string? Tagline
    {
        get; set;
    }
    public string? Pan
    {
        get; set;
    }
    public string? Iec
    {
        get; set;
    }
    public bool IsActive { get; set; } = true; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow; public Guid? CreatedByUserId
    {
        get; set;
    }
}
public sealed class CompanyContact : CompanyRecord
{
    public string? Purpose { get; set; }
    public string Kind { get; set; } = "Phone"; public string Value { get; set; } = ""; public string? ContactName
    {
        get; set;
    }
}
public sealed class CompanyAddress : CompanyRecord
{
    public string? Line2 { get; set; }
    public string? Locality { get; set; }
    public string? District { get; set; }
    public string Purpose { get; set; } = "RegisteredOffice"; public string Lines { get; set; } = ""; public string City { get; set; } = ""; public string StateCode { get; set; } = ""; public string CountryCode { get; set; } = "IN"; public string PostalCode { get; set; } = ""; public bool IsDefaultBilling
    {
        get; set;
    }
    public bool IsDefaultDispatch
    {
        get; set;
    }
}
public sealed class GstRegistration : CompanyRecord
{
    public bool IsInvoiceDefault { get; set; }
    public string Gstin { get; set; } = ""; public string StateCode { get; set; } = ""; public bool IsActive { get; set; } = true;
}
public sealed class GstRegistrationAddress : CompanyRecord
{
    public Guid GstRegistrationId
    {
        get; set;
    }
    public Guid AddressId
    {
        get; set;
    }
}
public sealed class CompanyBankAccount : CompanyRecord
{
    public string? BankAddress { get; set; }
    public string? PaymentInstructions { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public string? AccountType { get; set; }
    public string? UpiId { get; set; }
    public string BankName { get; set; } = ""; public string AccountHolder { get; set; } = ""; public string AccountNumber { get; set; } = ""; public string Ifsc { get; set; } = ""; public string? Branch
    {
        get; set;
    }
    public bool IsActive { get; set; } = true; public bool IsDefault
    {
        get; set;
    }
}
public sealed class BrandingAsset : CompanyRecord
{
    public string? LogoName { get; set; }
    public string? LogoUsage { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRetired { get; set; }
    public int SortOrder { get; set; }
    public byte[]? Thumbnail { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[]? Content { get; set; }
    public string? ContentType { get; set; }
    public string Kind { get; set; } = "Logo1"; public int Revision { get; set; } = 1; public string StorageId { get; set; } = ""; public string Sha256 { get; set; } = ""; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow; public Guid? ChangedByUserId
    {
        get; set;
    }
}
public sealed class TermsVersion : CompanyRecord
{
    public int Revision { get; set; } = 1; public string Text { get; set; } = ""; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow; public Guid? ChangedByUserId
    {
        get; set;
    }
}
public sealed class CompanySettings : CompanyRecord
{
    public bool ShowPrimaryLogo { get; set; }
    public bool ShowSecondaryLogo { get; set; }
    public bool ShowBankDetails { get; set; }
    public bool ShowUpi { get; set; }
    public string? InvoiceHeader { get; set; }
    public string? InvoiceFooter { get; set; }
    public string? SignatoryName { get; set; }
    public bool AutomaticallySaveInvoicePdf
    {
        get; set;
    }
    public Guid? TermsVersionId
    {
        get; set;
    }
    public Guid? Logo1AssetId
    {
        get; set;
    }
    public Guid? Logo2AssetId
    {
        get; set;
    }
    public Guid? SignatureAssetId
    {
        get; set;
    }
    public string BaseCurrency { get; set; } = "INR";
}
public sealed class FinancialYear : CompanyRecord
{
    public DateOnly Start
    {
        get; set;
    }
    public DateOnly End
    {
        get; set;
    }
    public string Status { get; set; } = "Open";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsCurrent { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTime? ReopenedUtc { get; set; }
    public Guid? ReopenedBy { get; set; }
    public string? ReopenReason { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
public sealed class DocumentNumberingSeries : CompanyRecord
{
    public Guid GstRegistrationId
    {
        get; set;
    }
    public string DocumentType { get; set; } = "TaxInvoice"; public string Prefix { get; set; } = "GT"; public long StartingNumber { get; set; } = 1; public int Padding { get; set; } = 4;
    public Guid FinancialYearId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Separator { get; set; } = "";
    public string Suffix { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
public sealed class NumberCounter : CompanyRecord
{
    public Guid SeriesId
    {
        get; set;
    }
    public Guid FinancialYearId
    {
        get; set;
    }
    public long NextNumber { get; set; } = 1;
}
public sealed class DocumentNumberLedger : CompanyRecord
{
    public Guid SeriesId
    {
        get; set;
    }
    public Guid FinancialYearId
    {
        get; set;
    }
    public Guid GstRegistrationId
    {
        get; set;
    }
    public long Sequence
    {
        get; set;
    }
    public string CompleteNumber { get; set; } = ""; public string Status { get; set; } = "Issued"; public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
}
public sealed class InvoiceSnapshot : CompanyRecord
{
    public Guid LedgerId
    {
        get; set;
    }
    public int FormatVersion { get; set; } = 1; public string SnapshotJson { get; set; } = "{}"; public string LayoutVersion { get; set; } = "Unspecified"; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public sealed class SnapshotAsset : CompanyRecord
{
    public Guid SnapshotId
    {
        get; set;
    }
    public Guid BrandingAssetId
    {
        get; set;
    }
}
public sealed class AuditEvent : Record
{
    public Guid? CompanyId
    {
        get; set;
    }
    public Guid? ActorUserId
    {
        get; set;
    }
    public string ActorKind { get; set; } = "Application"; public DateTime OccurredUtc { get; set; } = DateTime.UtcNow; public string Action { get; set; } = ""; public string RecordType { get; set; } = ""; public string? RecordId
    {
        get; set;
    }
    public string ChangesJson { get; set; } = "{}";
}
public sealed class SessionHistory : Record
{
    public Guid UserId
    {
        get; set;
    }
    public string WindowsSid { get; set; } = ""; public DateTime StartedUtc { get; set; } = DateTime.UtcNow; public DateTime? EndedUtc
    {
        get; set;
    }
    public string Status { get; set; } = "Interrupted";
}
public sealed class BackupConfiguration : Record
{
    public bool AutomaticEnabled { get; set; } = true; public bool IsConfigured
    {
        get; set;
    }
    public string? Schedule
    {
        get; set;
    }
    public string? TimeZoneId
    {
        get; set;
    }
    public Guid? DestinationId
    {
        get; set;
    }
    public int? RetainCount
    {
        get; set;
    }
    public string? ProtectionPolicy
    {
        get; set;
    }
}
public sealed class BackupDestination : Record
{
    public string Path { get; set; } = ""; public string? VolumeIdentity
    {
        get; set;
    }
    public string Kind { get; set; } = "Internal";
}
public sealed class BackupJob : Record
{
    public Guid ConfigurationId
    {
        get; set;
    }
    public string OccurrenceKey { get; set; } = ""; public string Trigger { get; set; } = "Manual"; public string Status { get; set; } = "Pending"; public DateTime StartedUtc { get; set; } = DateTime.UtcNow; public DateTime? FinishedUtc
    {
        get; set;
    }
    public string? ErrorCategory
    {
        get; set;
    }
}
public sealed class BackupArtifact : Record
{
    public Guid JobId
    {
        get; set;
    }
    public string StorageId { get; set; } = ""; public string ManifestHash { get; set; } = ""; public bool IsVerified
    {
        get; set;
    }
}
public sealed class RestoreOperation : Record
{
    public Guid ArtifactId
    {
        get; set;
    }
    public Guid? SafetyArtifactId
    {
        get; set;
    }
    public string Status { get; set; } = "Pending";
}
public sealed class DatabaseSecurityMetadata : Record
{
    public Guid InstallationId
    {
        get; set;
    }
    public string Mode { get; set; } = "PlaintextDevelopment"; public string? KeyGenerationId
    {
        get; set;
    }
    public string? CipherProfile
    {
        get; set;
    }
}
public sealed class KeyEnvelopeMetadata : Record
{
    public Guid SecurityMetadataId
    {
        get; set;
    }
    public string Purpose { get; set; } = ""; public string GenerationId { get; set; } = ""; public string EnvelopeStorageId { get; set; } = ""; public int FormatVersion { get; set; } = 1;
}
