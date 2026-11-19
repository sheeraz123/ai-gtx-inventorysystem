using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GTS.Billing.Infrastructure;

// Immutable V1 model shared by the initial migration snapshot. Future schema changes
// must introduce a new model version instead of editing this historical definition.
internal static class SchemaV1
{
    private static EntityTypeBuilder<T> Record<T>(ModelBuilder m, string table) where T : Record
    {
        var e = m.Entity<T>();
        e.ToTable(table);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Version).IsConcurrencyToken();
        return e;
    }
    private static EntityTypeBuilder<T> Owned<T>(ModelBuilder m, string table) where T : CompanyRecord
    {
        var e = Record<T>(m, table);
        e.HasAlternateKey(x => new { x.CompanyId, x.Id });
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        return e;
    }
    private static void Check<T>(EntityTypeBuilder<T> e, string name, string sql) where T : class => e.ToTable(t => t.HasCheckConstraint(name, sql));
    private static void Scope<T, TTarget>(EntityTypeBuilder<T> e, string id) where T : CompanyRecord where TTarget : CompanyRecord =>
        e.HasOne<TTarget>().WithMany().HasForeignKey(nameof(CompanyRecord.CompanyId), id).HasPrincipalKey(nameof(CompanyRecord.CompanyId), nameof(GTS.Billing.Domain.Record.Id)).OnDelete(DeleteBehavior.Restrict);
    private static void UserRef<T>(EntityTypeBuilder<T> e, string property) where T : class =>
        e.HasOne<User>().WithMany().HasForeignKey(property).OnDelete(DeleteBehavior.Restrict);

    public static void Configure(ModelBuilder m)
    {
        var installation = Record<Installation>(m, "Installations");
        installation.Property<int>("SingletonSlot").HasDefaultValue(1);
        installation.HasIndex("SingletonSlot").IsUnique();
        Check(installation, "CK_Installation_Singleton", "SingletonSlot = 1");
        Check(installation, "CK_Installation_Mode", "DatabaseMode = 'PlaintextDevelopment'");
        Check(installation, "CK_Installation_Setup", "SetupState IN ('NotConfigured','Configured')");

        var user = Record<User>(m, "Users");
        user.HasIndex(x => x.NormalizedUsername).IsUnique();
        Check(user, "CK_User_Name", "length(trim(Username)) > 0 AND length(trim(NormalizedUsername)) > 0 AND NormalizedUsername = upper(trim(Username))");
        Check(user, "CK_User_Hash", "length(PasswordHash) > 0");
        var admin = m.Entity<SuperAdminAssignment>();
        admin.ToTable("SuperAdminAssignments");
        admin.HasKey(x => x.SingletonId);
        admin.Property(x => x.SingletonId).ValueGeneratedNever();
        admin.HasIndex(x => x.UserId).IsUnique();
        UserRef(admin, nameof(SuperAdminAssignment.UserId));
        Check(admin, "CK_Admin_ExactlyOneSlot", "SingletonId = 1");
        var role = Record<Role>(m, "Roles");
        role.HasIndex(x => x.Name).IsUnique();
        Check(role, "CK_Role_Name", "length(trim(Name)) > 0");
        var module = m.Entity<Module>();
        module.ToTable("Modules");
        module.HasKey(x => x.Code);
        var ur = Record<UserRole>(m, "UserRoles");
        UserRef(ur, "UserId");
        ur.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        ur.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        ur.HasIndex(x => new { x.UserId, x.RoleId, x.CompanyId }).IsUnique();
        const string actions = "Action IN ('View','Create','Edit','Delete','Cancel','Print','Export','ViewReports','ManageSettings','Backup','BackupSettings','Restore','ChangeSignature','SelectBank','CorrectClosedYear')";
        var rp = Record<RolePermission>(m, "RolePermissions");
        rp.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        rp.HasOne<Module>().WithMany().HasForeignKey(x => x.ModuleCode).OnDelete(DeleteBehavior.Restrict);
        rp.HasIndex(x => new { x.RoleId, x.ModuleCode, x.Action }).IsUnique();
        Check(rp, "CK_RolePermission_Action", actions);
        var up = Owned<UserPermissionOverride>(m, "UserPermissionOverrides");
        UserRef(up, "UserId");
        up.HasOne<Module>().WithMany().HasForeignKey(x => x.ModuleCode).OnDelete(DeleteBehavior.Restrict);
        up.HasIndex(x => new { x.UserId, x.CompanyId, x.ModuleCode, x.Action }).IsUnique();
        Check(up, "CK_UserOverride_Action", actions);
        var recovery = Record<RecoveryCode>(m, "RecoveryCodes");
        UserRef(recovery, "UserId");
        recovery.HasIndex(x => x.CodeHash).IsUnique();
        Check(recovery, "CK_Recovery_Hash", "length(CodeHash) > 0");

        var company = Record<Company>(m, "Companies");
        company.HasIndex(x => x.Code).IsUnique();
        UserRef(company, "CreatedByUserId");
        Check(company, "CK_Company_Identity", "length(trim(Code)) > 0 AND Code = upper(trim(Code)) AND length(trim(LegalName)) > 0");
        var contact = Owned<CompanyContact>(m, "CompanyContacts");
        Check(contact, "CK_Contact_Kind", "Kind IN ('Phone','Email','Other')");
        Check(contact, "CK_Contact_Value", "length(trim(Value)) > 0");
        var address = Owned<CompanyAddress>(m, "CompanyAddresses");
        Check(address, "CK_Address_Purpose", "Purpose IN ('RegisteredOffice','Billing','Factory','Dispatch')");
        Check(address, "CK_Address_Required", "length(trim(Lines)) > 0 AND length(StateCode) > 0 AND length(CountryCode) = 2");
        address.HasIndex(x => x.CompanyId).HasDatabaseName("UX_Address_DefaultBilling").IsUnique().HasFilter("IsDefaultBilling = 1");
        address.HasIndex(x => new { x.CompanyId, x.IsDefaultDispatch }).HasDatabaseName("UX_Address_DefaultDispatch").IsUnique().HasFilter("IsDefaultDispatch = 1");
        var gst = Owned<GstRegistration>(m, "GstRegistrations");
        gst.HasIndex(x => x.Gstin).IsUnique();
        Check(gst, "CK_Gst_Format", "length(Gstin) = 15 AND Gstin = upper(Gstin) AND substr(Gstin,1,2) = StateCode");
        var mapping = Owned<GstRegistrationAddress>(m, "GstRegistrationAddresses");
        Scope<GstRegistrationAddress, GstRegistration>(mapping, "GstRegistrationId");
        Scope<GstRegistrationAddress, CompanyAddress>(mapping, "AddressId");
        mapping.HasIndex(x => new { x.CompanyId, x.GstRegistrationId, x.AddressId }).IsUnique();
        var bank = Owned<CompanyBankAccount>(m, "CompanyBankAccounts");
        bank.HasIndex(x => x.CompanyId).IsUnique().HasFilter("IsDefault = 1 AND IsActive = 1");
        bank.HasIndex(x => new { x.CompanyId, x.AccountNumber, x.Ifsc }).IsUnique();
        Check(bank, "CK_Bank_DefaultActive", "IsDefault = 0 OR IsActive = 1");
        Check(bank, "CK_Bank_Required", "length(trim(BankName)) > 0 AND length(trim(AccountHolder)) > 0 AND length(AccountNumber) > 0 AND length(Ifsc) = 11");
        var branding = Owned<BrandingAsset>(m, "BrandingAssets");
        UserRef(branding, "ChangedByUserId");
        branding.HasIndex(x => new { x.CompanyId, x.Kind, x.Revision }).IsUnique();
        Check(branding, "CK_Branding_Kind", "Kind IN ('Logo1','Logo2','Signature')");
        Check(branding, "CK_Branding_Metadata", "Revision > 0 AND length(StorageId) > 0 AND length(Sha256) = 64");
        var terms = Owned<TermsVersion>(m, "TermsVersions");
        UserRef(terms, "ChangedByUserId");
        terms.HasIndex(x => new { x.CompanyId, x.Revision }).IsUnique();
        Check(terms, "CK_Terms_Revision", "Revision > 0");
        var settings = Owned<CompanySettings>(m, "CompanySettings");
        settings.HasIndex(x => x.CompanyId).IsUnique();
        settings.Property(x => x.AutomaticallySaveInvoicePdf).HasDefaultValue(false);
        Scope<CompanySettings, TermsVersion>(settings, "TermsVersionId");
        Scope<CompanySettings, BrandingAsset>(settings, "Logo1AssetId");
        Scope<CompanySettings, BrandingAsset>(settings, "Logo2AssetId");
        Scope<CompanySettings, BrandingAsset>(settings, "SignatureAssetId");
        var fy = Owned<FinancialYear>(m, "FinancialYears");
        fy.HasIndex(x => new { x.CompanyId, x.Start }).IsUnique();
        Check(fy, "CK_FY_Status", "Status IN ('Open','Closed')");
        Check(fy, "CK_FY_Boundaries", "strftime('%m-%d', Start) = '04-01' AND strftime('%m-%d', End) = '03-31' AND End = date(Start, '+1 year', '-1 day')");
        var series = Owned<DocumentNumberingSeries>(m, "DocumentNumberingSeries");
        Scope<DocumentNumberingSeries, GstRegistration>(series, "GstRegistrationId");
        series.HasAlternateKey(x => new { x.CompanyId, x.Id, x.GstRegistrationId });
        series.HasIndex(x => new { x.CompanyId, x.GstRegistrationId, x.DocumentType }).IsUnique();
        Check(series, "CK_Series_Range", "StartingNumber > 0 AND Padding BETWEEN 1 AND 9 AND length(Prefix) BETWEEN 1 AND 6");
        var counter = Owned<NumberCounter>(m, "NumberCounters");
        Scope<NumberCounter, DocumentNumberingSeries>(counter, "SeriesId");
        Scope<NumberCounter, FinancialYear>(counter, "FinancialYearId");
        counter.HasIndex(x => new { x.CompanyId, x.SeriesId, x.FinancialYearId }).IsUnique();
        Check(counter, "CK_Counter_Positive", "NextNumber > 0");
        var ledger = Owned<DocumentNumberLedger>(m, "DocumentNumberLedger");
        Scope<DocumentNumberLedger, FinancialYear>(ledger, "FinancialYearId");
        ledger.HasOne<DocumentNumberingSeries>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SeriesId, x.GstRegistrationId }).HasPrincipalKey(x => new { x.CompanyId, x.Id, x.GstRegistrationId }).OnDelete(DeleteBehavior.Restrict);
        ledger.HasIndex(x => new { x.CompanyId, x.GstRegistrationId, x.FinancialYearId, x.CompleteNumber }).IsUnique();
        ledger.HasIndex(x => new { x.CompanyId, x.SeriesId, x.FinancialYearId, x.Sequence }).IsUnique();
        Check(ledger, "CK_Ledger_Number", "Sequence > 0 AND length(CompleteNumber) BETWEEN 1 AND 16 AND CompleteNumber NOT GLOB '*[^A-Za-z0-9/-]*'");
        Check(ledger, "CK_Ledger_Status", "Status IN ('Issued','Cancelled')");
        var snapshot = Owned<InvoiceSnapshot>(m, "InvoiceSnapshots");
        Scope<InvoiceSnapshot, DocumentNumberLedger>(snapshot, "LedgerId");
        snapshot.HasIndex(x => new { x.CompanyId, x.LedgerId }).IsUnique();
        Check(snapshot, "CK_Snapshot_Format", "FormatVersion > 0 AND json_valid(SnapshotJson)");
        var sa = Owned<SnapshotAsset>(m, "SnapshotAssets");
        Scope<SnapshotAsset, InvoiceSnapshot>(sa, "SnapshotId");
        Scope<SnapshotAsset, BrandingAsset>(sa, "BrandingAssetId");
        sa.HasIndex(x => new { x.CompanyId, x.SnapshotId, x.BrandingAssetId }).IsUnique();
        var audit = Record<AuditEvent>(m, "AuditEvents");
        UserRef(audit, "ActorUserId");
        audit.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        Check(audit, "CK_Audit_Json", "json_valid(ChangesJson)");
        audit.HasIndex(x => new { x.CompanyId, x.OccurredUtc });
        var session = Record<SessionHistory>(m, "SessionHistory");
        UserRef(session, "UserId");
        Check(session, "CK_Session_Status", "Status IN ('Active','Ended','Interrupted')");
        var destination = Record<BackupDestination>(m, "BackupDestinations");
        Check(destination, "CK_Destination_Kind", "Kind IN ('Internal','External','Usb')");
        var config = Record<BackupConfiguration>(m, "BackupConfigurations");
        config.Property(x => x.AutomaticEnabled).HasDefaultValue(true);
        config.HasOne<BackupDestination>().WithMany().HasForeignKey(x => x.DestinationId).OnDelete(DeleteBehavior.Restrict);
        Check(config, "CK_Backup_Retention", "RetainCount IS NULL OR RetainCount > 0");
        var job = Record<BackupJob>(m, "BackupJobs");
        job.HasOne<BackupConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
        job.HasIndex(x => new { x.ConfigurationId, x.OccurrenceKey }).IsUnique();
        Check(job, "CK_Job_Status", "Status IN ('Pending','Running','Succeeded','Failed','Interrupted')");
        var artifact = Record<BackupArtifact>(m, "BackupArtifacts");
        artifact.HasOne<BackupJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        var restore = Record<RestoreOperation>(m, "RestoreOperations");
        restore.HasOne<BackupArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
        restore.HasOne<BackupArtifact>().WithMany().HasForeignKey(x => x.SafetyArtifactId).OnDelete(DeleteBehavior.Restrict);
        var security = Record<DatabaseSecurityMetadata>(m, "DatabaseSecurityMetadata");
        security.HasOne<Installation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
        security.HasIndex(x => x.InstallationId).IsUnique();
        Check(security, "CK_Security_DevelopmentOnly", "Mode = 'PlaintextDevelopment' AND KeyGenerationId IS NULL AND CipherProfile IS NULL");
        var envelope = Record<KeyEnvelopeMetadata>(m, "KeyEnvelopeMetadata");
        envelope.HasOne<DatabaseSecurityMetadata>().WithMany().HasForeignKey(x => x.SecurityMetadataId).OnDelete(DeleteBehavior.Restrict);
        envelope.HasIndex(x => new { x.SecurityMetadataId, x.Purpose, x.GenerationId }).IsUnique();
    }
}
