# Initial plaintext development schema

Migration: **20261116000100_InitialDevelopmentSchema**. 33 domain tables plus EF-managed migration bookkeeping. Guid identifiers are stored as SQLite TEXT; versions are application-managed concurrency tokens. Foreign Keys=True is explicit on every connection and checked during verification. Parent deletion uses RESTRICT, not cascading business-history deletion.

| Group | Tables |
|---|---|
| Installation | Installations, DatabaseSecurityMetadata, KeyEnvelopeMetadata |
| Identity/authorization | Users, SuperAdminAssignments, Roles, Modules, UserRoles, RolePermissions, UserPermissionOverrides, RecoveryCodes |
| Company | Companies, CompanyContacts, CompanyAddresses, GstRegistrations, GstRegistrationAddresses, CompanyBankAccounts, BrandingAssets, TermsVersions, CompanySettings |
| Years/documents | FinancialYears, DocumentNumberingSeries, NumberCounters, DocumentNumberLedger, InvoiceSnapshots, SnapshotAssets |
| History | AuditEvents, SessionHistory |
| Backup foundation | BackupConfigurations, BackupDestinations, BackupJobs, BackupArtifacts, RestoreOperations |

## Relationships and database invariants

- Installation has a unique checked singleton slot. Security metadata references its installation. PlaintextDevelopment mode is explicit; cipher/key generation values must remain null in this stage. KeyEnvelopeMetadata contains references only; no keys/envelopes are generated or stored.
- Users have unique normalized usernames. PasswordHash/CodeHash are fields for future encoded hashes, not functioning authentication. Test placeholders are conspicuously synthetic. SuperAdminAssignments has a primary key constrained to 1 and a unique User FK: at most one assignment. Zero is expected before the separately approved setup workflow; exactly-one-after-setup is NOT implemented.
- UserRoles bind a user and role to a company; role permissions bind module/action; user overrides bind user/company/module/action with a unique key. Actions have a database allowlist. No permission evaluator exists yet.
- Every company-owned record has a (CompanyId, Id) alternate key and Company FK. GST/address mapping, settings/terms/branding, counters/years/series, ledger/series/year and snapshot/assets use composite company-scoped FKs, rejecting cross-company links.
- GSTIN must have length 15, uppercase and matching state prefix. Triggers prevent mapped address/registration state disagreement, including subsequent state edits. Full GST/PAN/checksum/business validation awaits future workflows.
- Company codes are unique, uppercase and nonblank. Contacts and required address/bank fields have basic checks. At most one default billing/dispatch address exists per company.
- Bank checks require an active default and at most one active default per company, with duplicate account/IFSC prevention. A company may have zero banks while unconfigured. Exactly one eligible default at activation requires later application validation and is not claimed here.
- Branding has Logo1/Logo2/Signature kinds with unique revision numbers. Terms revisions are unique. Terms, branding, snapshots and audit rows have immutable/append-only database triggers. These are correctness controls, not tamper-proof protection against a raw-file writer who can remove triggers.
- Financial years require April 1 through March 31 of the following year and unique company/start; status Open/Closed. No creation/closing/correction workflow is implemented.
- Number series are unique by company/GST/document type. Counters are unique per series/year. Ledger enforces issuer/year/complete-number uniqueness and series/year/sequence uniqueness; recorded numbers cannot be rewritten or deleted, and cancelled status cannot revert. Length/character checks are a foundation, not legal compliance certification. Automatic allocation, FY token formatting and tax workflows are not implemented.
- InvoiceSnapshots preserve a versioned JSON payload and layout version; SnapshotAssets retain company-scoped immutable branding references. Full snapshot completeness/issuance validation is future work.
- AutomaticEnabled defaults true on backup configuration; IsConfigured remains false, schedule/destination/protection remain unset. This is inert schema, not a scheduler. CompanySettings.AutomaticallySaveInvoicePdf defaults false independently.
- Backup jobs have unique configuration/occurrence keys. Artifacts/restore records retain foreign-key links. No files are backed up or restored.

## Transactions and migration handling

IUnitOfWork wraps EF SaveChangesAsync and async transactions. Tests commit a valid first write, provoke a uniqueness failure and roll back the entire transaction. Separate-context restart tests verify persistence; stale updates reject rather than overwrite silently.

Fresh explicit initialization applies the migration and seeds only Installation and plaintext DatabaseSecurityMetadata. No users, companies, modules, roles, accounts or secrets are seeded. The marker records Initializing before creating the file and Ready only after successful migration/identity commit. Failure leaves evidence and requires deliberate recovery, not automatic recreation.

Existing startup is read/write without creation, checks applied migration history, PRAGMA foreign_keys, quick_check, foreign_key_check and installation identity. Integrity checks do not prove production security. Future SQLCipher will require a separate encrypted connection factory, key lifecycle and migration—not just changing a configuration flag.
