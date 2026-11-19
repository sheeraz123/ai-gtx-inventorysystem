-- Constraints that require observing other rows. No business workflows are activated.
CREATE TRIGGER GstAddress_State_Insert BEFORE INSERT ON GstRegistrationAddresses
WHEN (SELECT StateCode FROM GstRegistrations WHERE Id=NEW.GstRegistrationId AND CompanyId=NEW.CompanyId)
 <> (SELECT StateCode FROM CompanyAddresses WHERE Id=NEW.AddressId AND CompanyId=NEW.CompanyId)
BEGIN SELECT RAISE(ABORT, 'GST registration/address state mismatch'); END;
CREATE TRIGGER GstAddress_State_Update BEFORE UPDATE ON GstRegistrationAddresses
WHEN (SELECT StateCode FROM GstRegistrations WHERE Id=NEW.GstRegistrationId AND CompanyId=NEW.CompanyId)
 <> (SELECT StateCode FROM CompanyAddresses WHERE Id=NEW.AddressId AND CompanyId=NEW.CompanyId)
BEGIN SELECT RAISE(ABORT, 'GST registration/address state mismatch'); END;
CREATE TRIGGER Address_State_Update BEFORE UPDATE OF StateCode ON CompanyAddresses
WHEN EXISTS (SELECT 1 FROM GstRegistrationAddresses m JOIN GstRegistrations g ON g.Id=m.GstRegistrationId AND g.CompanyId=m.CompanyId WHERE m.AddressId=OLD.Id AND m.CompanyId=OLD.CompanyId AND g.StateCode<>NEW.StateCode)
BEGIN SELECT RAISE(ABORT, 'Address state would invalidate GST mapping'); END;
CREATE TRIGGER Gst_State_Update BEFORE UPDATE OF StateCode ON GstRegistrations
WHEN EXISTS (SELECT 1 FROM GstRegistrationAddresses m JOIN CompanyAddresses a ON a.Id=m.AddressId AND a.CompanyId=m.CompanyId WHERE m.GstRegistrationId=OLD.Id AND m.CompanyId=OLD.CompanyId AND a.StateCode<>NEW.StateCode)
BEGIN SELECT RAISE(ABORT, 'GST state would invalidate address mapping'); END;
CREATE TRIGGER Ledger_NoDelete BEFORE DELETE ON DocumentNumberLedger
BEGIN SELECT RAISE(ABORT, 'Number ledger is permanent'); END;
CREATE TRIGGER Ledger_ImmutableNumber BEFORE UPDATE ON DocumentNumberLedger
WHEN NEW.Id<>OLD.Id OR NEW.CompanyId<>OLD.CompanyId OR NEW.SeriesId<>OLD.SeriesId OR NEW.FinancialYearId<>OLD.FinancialYearId OR NEW.GstRegistrationId<>OLD.GstRegistrationId OR NEW.CompleteNumber<>OLD.CompleteNumber OR NEW.Sequence<>OLD.Sequence OR NEW.RecordedUtc<>OLD.RecordedUtc OR (OLD.Status='Cancelled' AND NEW.Status<>'Cancelled')
BEGIN SELECT RAISE(ABORT, 'Issued/cancelled number cannot be rewritten'); END;
CREATE TRIGGER Snapshot_NoUpdate BEFORE UPDATE ON InvoiceSnapshots
BEGIN SELECT RAISE(ABORT, 'Snapshot is immutable'); END;
CREATE TRIGGER Snapshot_NoDelete BEFORE DELETE ON InvoiceSnapshots
BEGIN SELECT RAISE(ABORT, 'Snapshot is immutable'); END;
CREATE TRIGGER SnapshotAsset_NoUpdate BEFORE UPDATE ON SnapshotAssets
BEGIN SELECT RAISE(ABORT, 'Snapshot asset reference is immutable'); END;
CREATE TRIGGER SnapshotAsset_NoDelete BEFORE DELETE ON SnapshotAssets
BEGIN SELECT RAISE(ABORT, 'Snapshot asset reference is immutable'); END;
CREATE TRIGGER Terms_NoUpdate BEFORE UPDATE ON TermsVersions
BEGIN SELECT RAISE(ABORT, 'Terms versions are immutable'); END;
CREATE TRIGGER Terms_NoDelete BEFORE DELETE ON TermsVersions
BEGIN SELECT RAISE(ABORT, 'Terms versions are retained'); END;
CREATE TRIGGER Branding_NoUpdate BEFORE UPDATE ON BrandingAssets
BEGIN SELECT RAISE(ABORT, 'Branding versions are immutable'); END;
CREATE TRIGGER Branding_NoDelete BEFORE DELETE ON BrandingAssets
BEGIN SELECT RAISE(ABORT, 'Branding versions are retained'); END;
CREATE TRIGGER Audit_NoUpdate BEFORE UPDATE ON AuditEvents
BEGIN SELECT RAISE(ABORT, 'Audit is append-only'); END;
CREATE TRIGGER Audit_NoDelete BEFORE DELETE ON AuditEvents
BEGIN SELECT RAISE(ABORT, 'Audit is append-only'); END;
