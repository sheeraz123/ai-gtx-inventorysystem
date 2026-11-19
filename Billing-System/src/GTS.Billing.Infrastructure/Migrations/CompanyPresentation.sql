ALTER TABLE BrandingAssets ADD COLUMN LogoName TEXT NULL;
ALTER TABLE BrandingAssets ADD COLUMN LogoUsage TEXT NULL;
ALTER TABLE BrandingAssets ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1 CONSTRAINT CK_Branding_Active CHECK(IsActive IN (0,1));
ALTER TABLE BrandingAssets ADD COLUMN IsRetired INTEGER NOT NULL DEFAULT 0 CONSTRAINT CK_Branding_Retired CHECK(IsRetired IN (0,1) AND (IsRetired = 0 OR IsActive = 0));
ALTER TABLE BrandingAssets ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;
ALTER TABLE BrandingAssets ADD COLUMN Thumbnail BLOB NULL CONSTRAINT CK_Branding_Thumbnail CHECK(Thumbnail IS NULL OR length(Thumbnail) BETWEEN 45 AND 65536);
ALTER TABLE BrandingAssets ADD COLUMN UpdatedUtc TEXT NULL;
ALTER TABLE BrandingAssets ADD COLUMN UpdatedByUserId TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
CREATE INDEX IX_BrandingAssets_UpdatedByUserId ON BrandingAssets(UpdatedByUserId);
CREATE INDEX IX_BrandingAssets_CompanyId_SortOrder_Id ON BrandingAssets(CompanyId,SortOrder,Id);

ALTER TABLE CompanyBankAccounts ADD COLUMN BankAddress TEXT NULL;
ALTER TABLE CompanyBankAccounts ADD COLUMN PaymentInstructions TEXT NULL;
ALTER TABLE CompanyBankAccounts ADD COLUMN DisplayOrder INTEGER NOT NULL DEFAULT 0;
ALTER TABLE CompanyBankAccounts ADD COLUMN CreatedUtc TEXT NULL;
ALTER TABLE CompanyBankAccounts ADD COLUMN UpdatedUtc TEXT NULL;
ALTER TABLE CompanyBankAccounts ADD COLUMN CreatedByUserId TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
ALTER TABLE CompanyBankAccounts ADD COLUMN UpdatedByUserId TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
CREATE INDEX IX_CompanyBankAccounts_CreatedByUserId ON CompanyBankAccounts(CreatedByUserId);
CREATE INDEX IX_CompanyBankAccounts_UpdatedByUserId ON CompanyBankAccounts(UpdatedByUserId);
CREATE INDEX IX_CompanyBankAccounts_CompanyId_DisplayOrder_Id ON CompanyBankAccounts(CompanyId,DisplayOrder,Id);

ALTER TABLE CompanySettings ADD COLUMN ShowPrimaryLogo INTEGER NOT NULL DEFAULT 0 CONSTRAINT CK_CompanySettings_ShowPrimary CHECK(ShowPrimaryLogo IN (0,1));
ALTER TABLE CompanySettings ADD COLUMN ShowSecondaryLogo INTEGER NOT NULL DEFAULT 0 CONSTRAINT CK_CompanySettings_ShowSecondary CHECK(ShowSecondaryLogo IN (0,1));
ALTER TABLE CompanySettings ADD COLUMN ShowBankDetails INTEGER NOT NULL DEFAULT 0 CONSTRAINT CK_CompanySettings_ShowBank CHECK(ShowBankDetails IN (0,1));
ALTER TABLE CompanySettings ADD COLUMN ShowUpi INTEGER NOT NULL DEFAULT 0 CONSTRAINT CK_CompanySettings_ShowUpi CHECK(ShowUpi IN (0,1));
UPDATE CompanySettings SET
    ShowPrimaryLogo = Logo1AssetId IS NOT NULL,
    ShowSecondaryLogo = Logo2AssetId IS NOT NULL,
    ShowBankDetails = EXISTS (SELECT 1 FROM CompanyBankAccounts b WHERE b.CompanyId = CompanySettings.CompanyId AND b.IsDefault = 1);

-- Keep the original asset identity and payload immutable. Only catalog metadata may change.
DROP TRIGGER Branding_NoUpdate;
UPDATE BrandingAssets SET IsActive = 0
WHERE Kind IN ('Logo1','Logo2') AND NOT EXISTS (
    SELECT 1 FROM CompanySettings s WHERE s.CompanyId = BrandingAssets.CompanyId
    AND (s.Logo1AssetId = BrandingAssets.Id OR s.Logo2AssetId = BrandingAssets.Id));
CREATE TRIGGER Branding_NoUpdate BEFORE UPDATE ON BrandingAssets
WHEN OLD.Kind = 'Signature'
 OR NEW.Id IS NOT OLD.Id OR NEW.CompanyId IS NOT OLD.CompanyId OR NEW.Kind IS NOT OLD.Kind
 OR NEW.Revision IS NOT OLD.Revision OR NEW.StorageId IS NOT OLD.StorageId OR NEW.Sha256 IS NOT OLD.Sha256
 OR NEW.Content IS NOT OLD.Content OR NEW.ContentType IS NOT OLD.ContentType OR NEW.Thumbnail IS NOT OLD.Thumbnail
 OR NEW.CreatedUtc IS NOT OLD.CreatedUtc OR NEW.ChangedByUserId IS NOT OLD.ChangedByUserId
BEGIN SELECT RAISE(ABORT,'Original branding asset data is immutable'); END;

CREATE TRIGGER Branding_SelectedActive BEFORE UPDATE OF IsActive ON BrandingAssets
WHEN NEW.IsActive = 0 AND EXISTS (SELECT 1 FROM CompanySettings s WHERE s.CompanyId = OLD.CompanyId
    AND (s.Logo1AssetId = OLD.Id OR s.Logo2AssetId = OLD.Id))
BEGIN SELECT RAISE(ABORT,'Clear the invoice logo selection first'); END;

CREATE TRIGGER CompanySettings_InvoiceLogos_Insert BEFORE INSERT ON CompanySettings
WHEN (NEW.Logo1AssetId IS NOT NULL AND NEW.Logo1AssetId = NEW.Logo2AssetId)
 OR (NEW.Logo1AssetId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM BrandingAssets a WHERE a.CompanyId = NEW.CompanyId
    AND a.Id = NEW.Logo1AssetId AND a.IsActive = 1 AND a.Kind IN ('Logo1','Logo2')))
 OR (NEW.Logo2AssetId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM BrandingAssets a WHERE a.CompanyId = NEW.CompanyId
    AND a.Id = NEW.Logo2AssetId AND a.IsActive = 1 AND a.Kind IN ('Logo1','Logo2')))
BEGIN SELECT RAISE(ABORT,'Invoice logos must be distinct active company logos'); END;

CREATE TRIGGER CompanySettings_InvoiceLogos_Update BEFORE UPDATE OF CompanyId, Logo1AssetId, Logo2AssetId ON CompanySettings
WHEN (NEW.Logo1AssetId IS NOT NULL AND NEW.Logo1AssetId = NEW.Logo2AssetId)
 OR (NEW.Logo1AssetId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM BrandingAssets a WHERE a.CompanyId = NEW.CompanyId
    AND a.Id = NEW.Logo1AssetId AND a.IsActive = 1 AND a.Kind IN ('Logo1','Logo2')))
 OR (NEW.Logo2AssetId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM BrandingAssets a WHERE a.CompanyId = NEW.CompanyId
    AND a.Id = NEW.Logo2AssetId AND a.IsActive = 1 AND a.Kind IN ('Logo1','Logo2')))
BEGIN SELECT RAISE(ABORT,'Invoice logos must be distinct active company logos'); END;

CREATE TRIGGER CompanyBankAccounts_NoDelete BEFORE DELETE ON CompanyBankAccounts
BEGIN SELECT RAISE(ABORT,'Bank accounts are retained; deactivate instead'); END;

CREATE TRIGGER CompanyBankAccounts_OwnerImmutable BEFORE UPDATE OF CompanyId, Id ON CompanyBankAccounts
WHEN NEW.CompanyId IS NOT OLD.CompanyId OR NEW.Id IS NOT OLD.Id
BEGIN SELECT RAISE(ABORT,'Bank account ownership is immutable'); END;
