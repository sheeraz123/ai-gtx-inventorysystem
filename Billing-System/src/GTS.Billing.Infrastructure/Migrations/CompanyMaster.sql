ALTER TABLE "Companies" ADD COLUMN "BusinessType" TEXT NULL;
ALTER TABLE "Companies" ADD COLUMN "EstablishedOn" TEXT NULL;
ALTER TABLE "Companies" ADD COLUMN "RegistrationType" TEXT NOT NULL DEFAULT 'Unregistered';
ALTER TABLE "Companies" ADD COLUMN "Tan" TEXT NULL;
ALTER TABLE "Companies" ADD COLUMN "Cin" TEXT NULL;
ALTER TABLE "Companies" ADD COLUMN "Udyam" TEXT NULL;
ALTER TABLE "Companies" ADD COLUMN "UpdatedUtc" TEXT NULL;
ALTER TABLE "Companies" ADD COLUMN "UpdatedByUserId" TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
ALTER TABLE "CompanyContacts" ADD COLUMN "Purpose" TEXT NULL;
ALTER TABLE "CompanyAddresses" ADD COLUMN "Line2" TEXT NULL;
ALTER TABLE "CompanyAddresses" ADD COLUMN "Locality" TEXT NULL;
ALTER TABLE "CompanyAddresses" ADD COLUMN "District" TEXT NULL;
ALTER TABLE "CompanyBankAccounts" ADD COLUMN "AccountType" TEXT NULL;
ALTER TABLE "CompanyBankAccounts" ADD COLUMN "UpiId" TEXT NULL;
ALTER TABLE "BrandingAssets" ADD COLUMN "Content" BLOB NULL CONSTRAINT CK_Branding_Content CHECK(Content IS NULL OR length(Content) BETWEEN 45 AND 1048576);
ALTER TABLE "BrandingAssets" ADD COLUMN "ContentType" TEXT NULL;
ALTER TABLE "CompanySettings" ADD COLUMN "InvoiceHeader" TEXT NULL;
ALTER TABLE "CompanySettings" ADD COLUMN "InvoiceFooter" TEXT NULL;
ALTER TABLE "CompanySettings" ADD COLUMN "SignatoryName" TEXT NULL;
CREATE INDEX IX_Companies_UpdatedByUserId ON Companies(UpdatedByUserId);
CREATE UNIQUE INDEX IX_CompanyContacts_CompanyId_Purpose ON CompanyContacts(CompanyId,Purpose) WHERE Purpose IS NOT NULL;
CREATE TABLE CurrentCompany (
    Slot INTEGER NOT NULL CONSTRAINT PK_CurrentCompany PRIMARY KEY,
    CompanyId TEXT NOT NULL,
    CONSTRAINT CK_CurrentCompany_Slot CHECK(Slot = 1),
    CONSTRAINT FK_CurrentCompany_Companies_CompanyId FOREIGN KEY(CompanyId) REFERENCES Companies(Id) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IX_CurrentCompany_CompanyId ON CurrentCompany(CompanyId);
CREATE TRIGGER CurrentCompany_Active_Insert BEFORE INSERT ON CurrentCompany
WHEN NOT EXISTS(SELECT 1 FROM Companies WHERE Id=NEW.CompanyId AND IsActive=1)
BEGIN SELECT RAISE(ABORT,'Current company must be active'); END;
CREATE TRIGGER CurrentCompany_Active_Update BEFORE UPDATE ON CurrentCompany
WHEN NOT EXISTS(SELECT 1 FROM Companies WHERE Id=NEW.CompanyId AND IsActive=1)
BEGIN SELECT RAISE(ABORT,'Current company must be active'); END;
CREATE TRIGGER Company_Current_Deactivation BEFORE UPDATE OF IsActive ON Companies
WHEN NEW.IsActive=0 AND EXISTS(SELECT 1 FROM CurrentCompany WHERE CompanyId=NEW.Id)
BEGIN SELECT RAISE(ABORT,'Clear current selection before deactivation'); END;
CREATE TRIGGER Company_Code_Immutable BEFORE UPDATE OF Code ON Companies
WHEN OLD.Code != NEW.Code
BEGIN SELECT RAISE(ABORT,'Company code cannot be changed'); END;
CREATE TRIGGER Company_Code_Ascii_Insert BEFORE INSERT ON Companies
WHEN length(NEW.Code) NOT BETWEEN 1 AND 30 OR NEW.Code GLOB '*[^A-Z0-9_-]*' OR substr(NEW.Code,1,1) NOT GLOB '[A-Z0-9]'
BEGIN SELECT RAISE(ABORT,'Company code must be normalized ASCII'); END;

