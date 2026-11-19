CREATE TABLE "BackupDestinations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupDestinations" PRIMARY KEY,
    "Path" TEXT NOT NULL,
    "VolumeIdentity" TEXT NULL,
    "Kind" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Destination_Kind" CHECK (Kind IN ('Internal','External','Usb'))
);


CREATE TABLE "Installations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Installations" PRIMARY KEY,
    "SetupState" TEXT NOT NULL,
    "DatabaseMode" TEXT NOT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "SingletonSlot" INTEGER NOT NULL DEFAULT 1,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Installation_Mode" CHECK (DatabaseMode = 'PlaintextDevelopment'),
    CONSTRAINT "CK_Installation_Setup" CHECK (SetupState IN ('NotConfigured','Configured')),
    CONSTRAINT "CK_Installation_Singleton" CHECK (SingletonSlot = 1)
);


CREATE TABLE "Modules" (
    "Code" TEXT NOT NULL CONSTRAINT "PK_Modules" PRIMARY KEY,
    "Name" TEXT NOT NULL
);


CREATE TABLE "Roles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Roles" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Role_Name" CHECK (length(trim(Name)) > 0)
);


CREATE TABLE "Users" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
    "Username" TEXT NOT NULL,
    "NormalizedUsername" TEXT NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_User_Hash" CHECK (length(PasswordHash) > 0),
    CONSTRAINT "CK_User_Name" CHECK (length(trim(Username)) > 0 AND length(trim(NormalizedUsername)) > 0 AND NormalizedUsername = upper(trim(Username)))
);


CREATE TABLE "BackupConfigurations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupConfigurations" PRIMARY KEY,
    "AutomaticEnabled" INTEGER NOT NULL DEFAULT 1,
    "IsConfigured" INTEGER NOT NULL,
    "Schedule" TEXT NULL,
    "TimeZoneId" TEXT NULL,
    "DestinationId" TEXT NULL,
    "RetainCount" INTEGER NULL,
    "ProtectionPolicy" TEXT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Backup_Retention" CHECK (RetainCount IS NULL OR RetainCount > 0),
    CONSTRAINT "FK_BackupConfigurations_BackupDestinations_DestinationId" FOREIGN KEY ("DestinationId") REFERENCES "BackupDestinations" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "DatabaseSecurityMetadata" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DatabaseSecurityMetadata" PRIMARY KEY,
    "InstallationId" TEXT NOT NULL,
    "Mode" TEXT NOT NULL,
    "KeyGenerationId" TEXT NULL,
    "CipherProfile" TEXT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Security_DevelopmentOnly" CHECK (Mode = 'PlaintextDevelopment' AND KeyGenerationId IS NULL AND CipherProfile IS NULL),
    CONSTRAINT "FK_DatabaseSecurityMetadata_Installations_InstallationId" FOREIGN KEY ("InstallationId") REFERENCES "Installations" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "RolePermissions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_RolePermissions" PRIMARY KEY,
    "RoleId" TEXT NOT NULL,
    "ModuleCode" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_RolePermission_Action" CHECK (Action IN ('View','Create','Edit','Delete','Cancel','Print','Export','ViewReports','ManageSettings','Backup','BackupSettings','Restore','ChangeSignature','SelectBank','CorrectClosedYear')),
    CONSTRAINT "FK_RolePermissions_Modules_ModuleCode" FOREIGN KEY ("ModuleCode") REFERENCES "Modules" ("Code") ON DELETE RESTRICT,
    CONSTRAINT "FK_RolePermissions_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "Companies" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Companies" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "LegalName" TEXT NOT NULL,
    "DisplayName" TEXT NULL,
    "Tagline" TEXT NULL,
    "Pan" TEXT NULL,
    "Iec" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "CreatedByUserId" TEXT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Company_Identity" CHECK (length(trim(Code)) > 0 AND Code = upper(trim(Code)) AND length(trim(LegalName)) > 0),
    CONSTRAINT "FK_Companies_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "RecoveryCodes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_RecoveryCodes" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "CodeHash" TEXT NOT NULL,
    "ConsumedUtc" TEXT NULL,
    "InvalidatedUtc" TEXT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Recovery_Hash" CHECK (length(CodeHash) > 0),
    CONSTRAINT "FK_RecoveryCodes_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "SessionHistory" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SessionHistory" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "WindowsSid" TEXT NOT NULL,
    "StartedUtc" TEXT NOT NULL,
    "EndedUtc" TEXT NULL,
    "Status" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Session_Status" CHECK (Status IN ('Active','Ended','Interrupted')),
    CONSTRAINT "FK_SessionHistory_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "SuperAdminAssignments" (
    "SingletonId" INTEGER NOT NULL CONSTRAINT "PK_SuperAdminAssignments" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "CK_Admin_ExactlyOneSlot" CHECK (SingletonId = 1),
    CONSTRAINT "FK_SuperAdminAssignments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "BackupJobs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupJobs" PRIMARY KEY,
    "ConfigurationId" TEXT NOT NULL,
    "OccurrenceKey" TEXT NOT NULL,
    "Trigger" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "StartedUtc" TEXT NOT NULL,
    "FinishedUtc" TEXT NULL,
    "ErrorCategory" TEXT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Job_Status" CHECK (Status IN ('Pending','Running','Succeeded','Failed','Interrupted')),
    CONSTRAINT "FK_BackupJobs_BackupConfigurations_ConfigurationId" FOREIGN KEY ("ConfigurationId") REFERENCES "BackupConfigurations" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "KeyEnvelopeMetadata" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_KeyEnvelopeMetadata" PRIMARY KEY,
    "SecurityMetadataId" TEXT NOT NULL,
    "Purpose" TEXT NOT NULL,
    "GenerationId" TEXT NOT NULL,
    "EnvelopeStorageId" TEXT NOT NULL,
    "FormatVersion" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "FK_KeyEnvelopeMetadata_DatabaseSecurityMetadata_SecurityMetadataId" FOREIGN KEY ("SecurityMetadataId") REFERENCES "DatabaseSecurityMetadata" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "AuditEvents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuditEvents" PRIMARY KEY,
    "CompanyId" TEXT NULL,
    "ActorUserId" TEXT NULL,
    "ActorKind" TEXT NOT NULL,
    "OccurredUtc" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "RecordType" TEXT NOT NULL,
    "RecordId" TEXT NULL,
    "ChangesJson" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Audit_Json" CHECK (json_valid(ChangesJson)),
    CONSTRAINT "FK_AuditEvents_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AuditEvents_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "BrandingAssets" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BrandingAssets" PRIMARY KEY,
    "Kind" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    "StorageId" TEXT NOT NULL,
    "Sha256" TEXT NOT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "ChangedByUserId" TEXT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_BrandingAssets_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Branding_Kind" CHECK (Kind IN ('Logo1','Logo2','Signature')),
    CONSTRAINT "CK_Branding_Metadata" CHECK (Revision > 0 AND length(StorageId) > 0 AND length(Sha256) = 64),
    CONSTRAINT "FK_BrandingAssets_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_BrandingAssets_Users_ChangedByUserId" FOREIGN KEY ("ChangedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "CompanyAddresses" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CompanyAddresses" PRIMARY KEY,
    "Purpose" TEXT NOT NULL,
    "Lines" TEXT NOT NULL,
    "City" TEXT NOT NULL,
    "StateCode" TEXT NOT NULL,
    "CountryCode" TEXT NOT NULL,
    "PostalCode" TEXT NOT NULL,
    "IsDefaultBilling" INTEGER NOT NULL,
    "IsDefaultDispatch" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_CompanyAddresses_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Address_Purpose" CHECK (Purpose IN ('RegisteredOffice','Billing','Factory','Dispatch')),
    CONSTRAINT "CK_Address_Required" CHECK (length(trim(Lines)) > 0 AND length(StateCode) > 0 AND length(CountryCode) = 2),
    CONSTRAINT "FK_CompanyAddresses_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "CompanyBankAccounts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CompanyBankAccounts" PRIMARY KEY,
    "BankName" TEXT NOT NULL,
    "AccountHolder" TEXT NOT NULL,
    "AccountNumber" TEXT NOT NULL,
    "Ifsc" TEXT NOT NULL,
    "Branch" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_CompanyBankAccounts_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Bank_DefaultActive" CHECK (IsDefault = 0 OR IsActive = 1),
    CONSTRAINT "CK_Bank_Required" CHECK (length(trim(BankName)) > 0 AND length(trim(AccountHolder)) > 0 AND length(AccountNumber) > 0 AND length(Ifsc) = 11),
    CONSTRAINT "FK_CompanyBankAccounts_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "CompanyContacts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CompanyContacts" PRIMARY KEY,
    "Kind" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "ContactName" TEXT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_CompanyContacts_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Contact_Kind" CHECK (Kind IN ('Phone','Email','Other')),
    CONSTRAINT "CK_Contact_Value" CHECK (length(trim(Value)) > 0),
    CONSTRAINT "FK_CompanyContacts_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "FinancialYears" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_FinancialYears" PRIMARY KEY,
    "Start" TEXT NOT NULL,
    "End" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_FinancialYears_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_FY_Boundaries" CHECK (strftime('%m-%d', Start) = '04-01' AND strftime('%m-%d', End) = '03-31' AND End = date(Start, '+1 year', '-1 day')),
    CONSTRAINT "CK_FY_Status" CHECK (Status IN ('Open','Closed')),
    CONSTRAINT "FK_FinancialYears_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "GstRegistrations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GstRegistrations" PRIMARY KEY,
    "Gstin" TEXT NOT NULL,
    "StateCode" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_GstRegistrations_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Gst_Format" CHECK (length(Gstin) = 15 AND Gstin = upper(Gstin) AND substr(Gstin,1,2) = StateCode),
    CONSTRAINT "FK_GstRegistrations_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "TermsVersions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_TermsVersions" PRIMARY KEY,
    "Revision" INTEGER NOT NULL,
    "Text" TEXT NOT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "ChangedByUserId" TEXT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_TermsVersions_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Terms_Revision" CHECK (Revision > 0),
    CONSTRAINT "FK_TermsVersions_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_TermsVersions_Users_ChangedByUserId" FOREIGN KEY ("ChangedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "UserPermissionOverrides" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UserPermissionOverrides" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "ModuleCode" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "IsAllowed" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_UserPermissionOverrides_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_UserOverride_Action" CHECK (Action IN ('View','Create','Edit','Delete','Cancel','Print','Export','ViewReports','ManageSettings','Backup','BackupSettings','Restore','ChangeSignature','SelectBank','CorrectClosedYear')),
    CONSTRAINT "FK_UserPermissionOverrides_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserPermissionOverrides_Modules_ModuleCode" FOREIGN KEY ("ModuleCode") REFERENCES "Modules" ("Code") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserPermissionOverrides_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "UserRoles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UserRoles" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    "CompanyId" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "FK_UserRoles_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserRoles_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserRoles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "BackupArtifacts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupArtifacts" PRIMARY KEY,
    "JobId" TEXT NOT NULL,
    "StorageId" TEXT NOT NULL,
    "ManifestHash" TEXT NOT NULL,
    "IsVerified" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "FK_BackupArtifacts_BackupJobs_JobId" FOREIGN KEY ("JobId") REFERENCES "BackupJobs" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "DocumentNumberingSeries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DocumentNumberingSeries" PRIMARY KEY,
    "GstRegistrationId" TEXT NOT NULL,
    "DocumentType" TEXT NOT NULL,
    "Prefix" TEXT NOT NULL,
    "StartingNumber" INTEGER NOT NULL,
    "Padding" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_DocumentNumberingSeries_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "AK_DocumentNumberingSeries_CompanyId_Id_GstRegistrationId" UNIQUE ("CompanyId", "Id", "GstRegistrationId"),
    CONSTRAINT "CK_Series_Range" CHECK (StartingNumber > 0 AND Padding BETWEEN 1 AND 9 AND length(Prefix) BETWEEN 1 AND 6),
    CONSTRAINT "FK_DocumentNumberingSeries_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_DocumentNumberingSeries_GstRegistrations_CompanyId_GstRegistrationId" FOREIGN KEY ("CompanyId", "GstRegistrationId") REFERENCES "GstRegistrations" ("CompanyId", "Id") ON DELETE RESTRICT
);


CREATE TABLE "GstRegistrationAddresses" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GstRegistrationAddresses" PRIMARY KEY,
    "GstRegistrationId" TEXT NOT NULL,
    "AddressId" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_GstRegistrationAddresses_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "FK_GstRegistrationAddresses_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_GstRegistrationAddresses_CompanyAddresses_CompanyId_AddressId" FOREIGN KEY ("CompanyId", "AddressId") REFERENCES "CompanyAddresses" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_GstRegistrationAddresses_GstRegistrations_CompanyId_GstRegistrationId" FOREIGN KEY ("CompanyId", "GstRegistrationId") REFERENCES "GstRegistrations" ("CompanyId", "Id") ON DELETE RESTRICT
);


CREATE TABLE "CompanySettings" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CompanySettings" PRIMARY KEY,
    "AutomaticallySaveInvoicePdf" INTEGER NOT NULL DEFAULT 0,
    "TermsVersionId" TEXT NULL,
    "Logo1AssetId" TEXT NULL,
    "Logo2AssetId" TEXT NULL,
    "SignatureAssetId" TEXT NULL,
    "BaseCurrency" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_CompanySettings_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "FK_CompanySettings_BrandingAssets_CompanyId_Logo1AssetId" FOREIGN KEY ("CompanyId", "Logo1AssetId") REFERENCES "BrandingAssets" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CompanySettings_BrandingAssets_CompanyId_Logo2AssetId" FOREIGN KEY ("CompanyId", "Logo2AssetId") REFERENCES "BrandingAssets" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CompanySettings_BrandingAssets_CompanyId_SignatureAssetId" FOREIGN KEY ("CompanyId", "SignatureAssetId") REFERENCES "BrandingAssets" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CompanySettings_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CompanySettings_TermsVersions_CompanyId_TermsVersionId" FOREIGN KEY ("CompanyId", "TermsVersionId") REFERENCES "TermsVersions" ("CompanyId", "Id") ON DELETE RESTRICT
);


CREATE TABLE "RestoreOperations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_RestoreOperations" PRIMARY KEY,
    "ArtifactId" TEXT NOT NULL,
    "SafetyArtifactId" TEXT NULL,
    "Status" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "FK_RestoreOperations_BackupArtifacts_ArtifactId" FOREIGN KEY ("ArtifactId") REFERENCES "BackupArtifacts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_RestoreOperations_BackupArtifacts_SafetyArtifactId" FOREIGN KEY ("SafetyArtifactId") REFERENCES "BackupArtifacts" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "DocumentNumberLedger" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DocumentNumberLedger" PRIMARY KEY,
    "SeriesId" TEXT NOT NULL,
    "FinancialYearId" TEXT NOT NULL,
    "GstRegistrationId" TEXT NOT NULL,
    "Sequence" INTEGER NOT NULL,
    "CompleteNumber" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "RecordedUtc" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_DocumentNumberLedger_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Ledger_Number" CHECK (Sequence > 0 AND length(CompleteNumber) BETWEEN 1 AND 16 AND CompleteNumber NOT GLOB '*[^A-Za-z0-9/-]*'),
    CONSTRAINT "CK_Ledger_Status" CHECK (Status IN ('Issued','Cancelled')),
    CONSTRAINT "FK_DocumentNumberLedger_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_DocumentNumberLedger_DocumentNumberingSeries_CompanyId_SeriesId_GstRegistrationId" FOREIGN KEY ("CompanyId", "SeriesId", "GstRegistrationId") REFERENCES "DocumentNumberingSeries" ("CompanyId", "Id", "GstRegistrationId") ON DELETE RESTRICT,
    CONSTRAINT "FK_DocumentNumberLedger_FinancialYears_CompanyId_FinancialYearId" FOREIGN KEY ("CompanyId", "FinancialYearId") REFERENCES "FinancialYears" ("CompanyId", "Id") ON DELETE RESTRICT
);


CREATE TABLE "NumberCounters" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_NumberCounters" PRIMARY KEY,
    "SeriesId" TEXT NOT NULL,
    "FinancialYearId" TEXT NOT NULL,
    "NextNumber" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_NumberCounters_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Counter_Positive" CHECK (NextNumber > 0),
    CONSTRAINT "FK_NumberCounters_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_NumberCounters_DocumentNumberingSeries_CompanyId_SeriesId" FOREIGN KEY ("CompanyId", "SeriesId") REFERENCES "DocumentNumberingSeries" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_NumberCounters_FinancialYears_CompanyId_FinancialYearId" FOREIGN KEY ("CompanyId", "FinancialYearId") REFERENCES "FinancialYears" ("CompanyId", "Id") ON DELETE RESTRICT
);


CREATE TABLE "InvoiceSnapshots" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_InvoiceSnapshots" PRIMARY KEY,
    "LedgerId" TEXT NOT NULL,
    "FormatVersion" INTEGER NOT NULL,
    "SnapshotJson" TEXT NOT NULL,
    "LayoutVersion" TEXT NOT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_InvoiceSnapshots_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Snapshot_Format" CHECK (FormatVersion > 0 AND json_valid(SnapshotJson)),
    CONSTRAINT "FK_InvoiceSnapshots_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_InvoiceSnapshots_DocumentNumberLedger_CompanyId_LedgerId" FOREIGN KEY ("CompanyId", "LedgerId") REFERENCES "DocumentNumberLedger" ("CompanyId", "Id") ON DELETE RESTRICT
);


CREATE TABLE "SnapshotAssets" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SnapshotAssets" PRIMARY KEY,
    "SnapshotId" TEXT NOT NULL,
    "BrandingAssetId" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_SnapshotAssets_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "FK_SnapshotAssets_BrandingAssets_CompanyId_BrandingAssetId" FOREIGN KEY ("CompanyId", "BrandingAssetId") REFERENCES "BrandingAssets" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SnapshotAssets_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SnapshotAssets_InvoiceSnapshots_CompanyId_SnapshotId" FOREIGN KEY ("CompanyId", "SnapshotId") REFERENCES "InvoiceSnapshots" ("CompanyId", "Id") ON DELETE RESTRICT
);


CREATE INDEX "IX_AuditEvents_ActorUserId" ON "AuditEvents" ("ActorUserId");


CREATE INDEX "IX_AuditEvents_CompanyId_OccurredUtc" ON "AuditEvents" ("CompanyId", "OccurredUtc");


CREATE INDEX "IX_BackupArtifacts_JobId" ON "BackupArtifacts" ("JobId");


CREATE INDEX "IX_BackupConfigurations_DestinationId" ON "BackupConfigurations" ("DestinationId");


CREATE UNIQUE INDEX "IX_BackupJobs_ConfigurationId_OccurrenceKey" ON "BackupJobs" ("ConfigurationId", "OccurrenceKey");


CREATE INDEX "IX_BrandingAssets_ChangedByUserId" ON "BrandingAssets" ("ChangedByUserId");


CREATE UNIQUE INDEX "IX_BrandingAssets_CompanyId_Kind_Revision" ON "BrandingAssets" ("CompanyId", "Kind", "Revision");


CREATE UNIQUE INDEX "IX_Companies_Code" ON "Companies" ("Code");


CREATE INDEX "IX_Companies_CreatedByUserId" ON "Companies" ("CreatedByUserId");


CREATE UNIQUE INDEX "UX_Address_DefaultBilling" ON "CompanyAddresses" ("CompanyId") WHERE IsDefaultBilling = 1;


CREATE UNIQUE INDEX "UX_Address_DefaultDispatch" ON "CompanyAddresses" ("CompanyId", "IsDefaultDispatch") WHERE IsDefaultDispatch = 1;


CREATE UNIQUE INDEX "IX_CompanyBankAccounts_CompanyId" ON "CompanyBankAccounts" ("CompanyId") WHERE IsDefault = 1 AND IsActive = 1;


CREATE UNIQUE INDEX "IX_CompanyBankAccounts_CompanyId_AccountNumber_Ifsc" ON "CompanyBankAccounts" ("CompanyId", "AccountNumber", "Ifsc");


CREATE UNIQUE INDEX "IX_CompanySettings_CompanyId" ON "CompanySettings" ("CompanyId");


CREATE INDEX "IX_CompanySettings_CompanyId_Logo1AssetId" ON "CompanySettings" ("CompanyId", "Logo1AssetId");


CREATE INDEX "IX_CompanySettings_CompanyId_Logo2AssetId" ON "CompanySettings" ("CompanyId", "Logo2AssetId");


CREATE INDEX "IX_CompanySettings_CompanyId_SignatureAssetId" ON "CompanySettings" ("CompanyId", "SignatureAssetId");


CREATE INDEX "IX_CompanySettings_CompanyId_TermsVersionId" ON "CompanySettings" ("CompanyId", "TermsVersionId");


CREATE UNIQUE INDEX "IX_DatabaseSecurityMetadata_InstallationId" ON "DatabaseSecurityMetadata" ("InstallationId");


CREATE UNIQUE INDEX "IX_DocumentNumberingSeries_CompanyId_GstRegistrationId_DocumentType" ON "DocumentNumberingSeries" ("CompanyId", "GstRegistrationId", "DocumentType");


CREATE INDEX "IX_DocumentNumberLedger_CompanyId_FinancialYearId" ON "DocumentNumberLedger" ("CompanyId", "FinancialYearId");


CREATE UNIQUE INDEX "IX_DocumentNumberLedger_CompanyId_GstRegistrationId_FinancialYearId_CompleteNumber" ON "DocumentNumberLedger" ("CompanyId", "GstRegistrationId", "FinancialYearId", "CompleteNumber");


CREATE UNIQUE INDEX "IX_DocumentNumberLedger_CompanyId_SeriesId_FinancialYearId_Sequence" ON "DocumentNumberLedger" ("CompanyId", "SeriesId", "FinancialYearId", "Sequence");


CREATE INDEX "IX_DocumentNumberLedger_CompanyId_SeriesId_GstRegistrationId" ON "DocumentNumberLedger" ("CompanyId", "SeriesId", "GstRegistrationId");


CREATE UNIQUE INDEX "IX_FinancialYears_CompanyId_Start" ON "FinancialYears" ("CompanyId", "Start");


CREATE INDEX "IX_GstRegistrationAddresses_CompanyId_AddressId" ON "GstRegistrationAddresses" ("CompanyId", "AddressId");


CREATE UNIQUE INDEX "IX_GstRegistrationAddresses_CompanyId_GstRegistrationId_AddressId" ON "GstRegistrationAddresses" ("CompanyId", "GstRegistrationId", "AddressId");


CREATE UNIQUE INDEX "IX_GstRegistrations_Gstin" ON "GstRegistrations" ("Gstin");


CREATE UNIQUE INDEX "IX_Installations_SingletonSlot" ON "Installations" ("SingletonSlot");


CREATE UNIQUE INDEX "IX_InvoiceSnapshots_CompanyId_LedgerId" ON "InvoiceSnapshots" ("CompanyId", "LedgerId");


CREATE UNIQUE INDEX "IX_KeyEnvelopeMetadata_SecurityMetadataId_Purpose_GenerationId" ON "KeyEnvelopeMetadata" ("SecurityMetadataId", "Purpose", "GenerationId");


CREATE INDEX "IX_NumberCounters_CompanyId_FinancialYearId" ON "NumberCounters" ("CompanyId", "FinancialYearId");


CREATE UNIQUE INDEX "IX_NumberCounters_CompanyId_SeriesId_FinancialYearId" ON "NumberCounters" ("CompanyId", "SeriesId", "FinancialYearId");


CREATE UNIQUE INDEX "IX_RecoveryCodes_CodeHash" ON "RecoveryCodes" ("CodeHash");


CREATE INDEX "IX_RecoveryCodes_UserId" ON "RecoveryCodes" ("UserId");


CREATE INDEX "IX_RestoreOperations_ArtifactId" ON "RestoreOperations" ("ArtifactId");


CREATE INDEX "IX_RestoreOperations_SafetyArtifactId" ON "RestoreOperations" ("SafetyArtifactId");


CREATE INDEX "IX_RolePermissions_ModuleCode" ON "RolePermissions" ("ModuleCode");


CREATE UNIQUE INDEX "IX_RolePermissions_RoleId_ModuleCode_Action" ON "RolePermissions" ("RoleId", "ModuleCode", "Action");


CREATE UNIQUE INDEX "IX_Roles_Name" ON "Roles" ("Name");


CREATE INDEX "IX_SessionHistory_UserId" ON "SessionHistory" ("UserId");


CREATE INDEX "IX_SnapshotAssets_CompanyId_BrandingAssetId" ON "SnapshotAssets" ("CompanyId", "BrandingAssetId");


CREATE UNIQUE INDEX "IX_SnapshotAssets_CompanyId_SnapshotId_BrandingAssetId" ON "SnapshotAssets" ("CompanyId", "SnapshotId", "BrandingAssetId");


CREATE UNIQUE INDEX "IX_SuperAdminAssignments_UserId" ON "SuperAdminAssignments" ("UserId");


CREATE INDEX "IX_TermsVersions_ChangedByUserId" ON "TermsVersions" ("ChangedByUserId");


CREATE UNIQUE INDEX "IX_TermsVersions_CompanyId_Revision" ON "TermsVersions" ("CompanyId", "Revision");


CREATE INDEX "IX_UserPermissionOverrides_ModuleCode" ON "UserPermissionOverrides" ("ModuleCode");


CREATE UNIQUE INDEX "IX_UserPermissionOverrides_UserId_CompanyId_ModuleCode_Action" ON "UserPermissionOverrides" ("UserId", "CompanyId", "ModuleCode", "Action");


CREATE INDEX "IX_UserRoles_CompanyId" ON "UserRoles" ("CompanyId");


CREATE INDEX "IX_UserRoles_RoleId" ON "UserRoles" ("RoleId");


CREATE UNIQUE INDEX "IX_UserRoles_UserId_RoleId_CompanyId" ON "UserRoles" ("UserId", "RoleId", "CompanyId");


CREATE UNIQUE INDEX "IX_Users_NormalizedUsername" ON "Users" ("NormalizedUsername");


