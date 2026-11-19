# Company input revision — date picker, state search and relaxed number validation

## Requested behavior

- Established on uses a native WPF DatePicker, with an optional date and invariant yyyy-MM-dd persistence. Existing date validity/future-date checks remain in the service/domain boundary.
- State dropdown supports case-insensitive substring searching by name or numeric code. Choosing an option preserves its actual StateCode; arbitrary search text is not persisted as a state.
- GSTIN is optional for every registration type. Format, checksum, embedded-PAN, state-prefix and registration/address geographic-consistency validation are removed.
- IFSC is optional and has no format/length-11 validation. Blank is represented as an empty string in the existing non-null SQLite column and as null in the optional form DTO.

Generic field-size bounds, independent PAN syntax, supported state selection, required address/company fields, other required bank details, account-number/UPI checks, duplicate constraints and company ownership foreign keys remain. Existing uppercase/trim normalization remains. Stored GSTIN/IFSC values are not represented as verified identifiers.

## Database change

New migration: **20261118000200_CompanyInputFlexibility**.

SchemaV3 extends the existing model without editing either previous migration. It removes CK_Gst_Format and the IFSC portion of CK_Bank_Required. The four GST geographic-validation triggers are dropped:

- GstAddress_State_Insert
- GstAddress_State_Update
- Address_State_Update
- Gst_State_Update

The EF Core SQLite provider rebuilds GstRegistrations and CompanyBankAccounts, copying existing columns and restoring keys/indexes. Company-owned composite foreign keys, GSTIN uniqueness, account uniqueness, eligible-default bank rules and all unrelated audit/snapshot/ledger/company-selection protections remain.

SQLite table rebuilding uses multiple transaction phases and temporarily disables foreign-key enforcement on the maintenance connection. It must not be described as an entirely atomic single-transaction upgrade. Fixture tests confirm restored foreign-key enforcement and retained related records.

**The installed development database has not been migrated in this revision.** The earlier approval applied to the CompanyMaster migration, which was completed separately. Existing instructions require separate approval before another installed-database migration. This new migration is prepared for review and tested only on disposable databases.

For an approved installed upgrade: close GTS normally; acquire the shipped machine-wide mutex; validate identity/history/integrity; create and verify a consistent SQLite backup and marker copy; mark the installation unavailable during maintenance; apply the prechecked migration; verify all prior data, identities, foreign keys, indexes and protected triggers; restore Ready only after successful verification. An interruption must remain blocked for reviewed recovery rather than silently reopening or initializing a database. No installed migration, maintenance-marker modification, provisioning or deployment has been performed here.

## Implementation

Changed existing files:

- src/GTS.Billing.Domain/CompanyRules.cs
- src/GTS.Billing.Infrastructure/CompanyStore.cs
- src/GTS.Billing.Infrastructure/BillingDbContext.cs
- src/GTS.Billing.Infrastructure/Migrations/BillingDbContextModelSnapshot.cs
- src/GTS.Billing.Presentation.Wpf/CompanyViewModel.cs
- src/GTS.Billing.Presentation.Wpf/CompanyView.xaml
- src/GTS.Billing.Presentation.Wpf/CompanyView.xaml.cs
- tests/GTS.Billing.Tests/AuthenticationTests.cs
- tests/GTS.Billing.Tests/PersistenceTests.cs
- tests/GTS.Billing.Tests/MigrationArtifacts.cs
- tests/GTS.Billing.Tests/CompanyValidationTests.cs
- tests/GTS.Billing.Tests/CompanyPresentationTests.cs
- README.md

Created:

- src/GTS.Billing.Infrastructure/SchemaV3.cs
- src/GTS.Billing.Infrastructure/Migrations/20261118000200_CompanyInputFlexibility.cs
- src/GTS.Billing.Infrastructure/Migrations/20261118000200_CompanyInputFlexibility.Designer.cs
- tests/GTS.Billing.Tests/CompanyInputRevisionTests.cs
- docs/stage4a-company-input-revision.md

No dependencies, application projects or authentication architecture were added. Date/search controls use WPF and the existing MVVM infrastructure. Mutex ownership, installation/path checks, authorization and shutdown tracking remain unchanged.

## Verification

Build/test outputs are isolated under .local/input-revision-artifacts so the running application binaries remain untouched. Existing pinned dependencies were restored from the project-local package cache; the offline restore invocation disabled the unavailable network vulnerability lookup without changing project/NuGet configuration.

~~~powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build GTS.Billing.slnx -c Release --artifacts-path .local/input-revision-artifacts --source .packages -p:NuGetAudit=false
dotnet test GTS.Billing.slnx -c Release --artifacts-path .local/input-revision-artifacts --no-restore --logger 'trx;LogFileName=company-input-revision-accepted.trx' --results-directory TestResults
~~~

Tests cover optional/free-form GSTIN/IFSC save/update/reload, empty-IFSC idempotence, editor dirty-state reversion, date conversion, state name/code filtering, selection preservation and real WPF DatePicker bindings. Upgrade fixtures retain a GST-linked number series/ledger/snapshot, bank record, company selection, installation identity and audit protections across the table rebuilds. Existing regression tests continue to verify authentication, mutex/crash behavior, company isolation, path restrictions and shutdown.

Generated SQL for review is in .local/migration-artifacts/CompanyInputFlexibility.sql. No generated SQL was executed against the installed database.

The original Stage 4A report remains historical; its descriptions of mandatory GSTIN/IFSC validation are superseded by this revision. Human keyboard/layout acceptance remains pending; automated WPF checks are not a manual visual sign-off. SQLite remains plaintext and limited to synthetic development data.

Final complete suite: **135 passed, 0 failed, 0 skipped** (company-input-revision-accepted.trx), including disposable child-process crash tests. Final Release build: **0 warnings, 0 errors**, using --artifacts-path .local/input-revision-artifacts --no-restore. The focused UI/input run passed 6 tests before the final full regression; final full regression includes the subsequent date-only field guard.

