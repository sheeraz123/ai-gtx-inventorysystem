# Stage 4C — Customer Master implementation

19 November 2026. Implementation and compilation only. **No automated tests were created, modified, generated or run. No application was launched and no database migration was executed.**

## A. Repository assessment and implementation

The five-project .NET 10/WPF architecture, CommunityToolkit.Mvvm, EF Core SQLite, Super Admin session gate, checked DevelopmentDatabase factory, persisted CurrentCompany selection, native global mutex and ApplicationLifetime are present.

Company Master is implemented. Product Category Master remains a navigation placeholder. There was no existing Customer/Party entity or customer grouping master to reuse. Customer classification is stored as optional customer-specific group/category labels, independently of Product Category. FinancialYear and generic company invoice/numbering/snapshot tables are schema foundations only; there is no operational customer accounting or invoicing system.

Implemented source paths cover WPF → CustomerService → domain validation → CustomerStore → EF Core → SQLite → DTO reload. This is real persistence code, not mock data. Runtime behavior is **unverified** in this phase.

## B. Architecture and company scope

- Domain: Customer and owned records, immutable request drafts and centralized CustomerRules.
- Application: typed CRUD/child/default/status/credit/opening operations, selection and invoice-snapshot contracts, explicit financial-source dependency.
- Infrastructure: short-lived contexts and writer transactions, authoritative authorization/company checks, persistence, queries, audit events and schema configuration.
- Presentation.Wpf: customer list, filters, pagination, nine editor/detail tabs, commands, duplicate confirmation, validation, discard prompts and scope invalidation.
- Printing: unchanged; no invoice printing workflow is activated.

Every operation runs through the existing AuthenticationService gate. CustomerService entry points also use IApplicationOperationCoordinator, an adapter to the same existing ApplicationLifetime singleton, so calls outside the WPF view model still participate in admission/cancellation/draining. The view model tracks complete UI workflows as well. No second lifetime coordinator was introduced. Both the service and transaction boundary enforce Super Admin access. No granular permission evaluator currently exists; other users are denied, including bank/internal-remarks access.

The store rechecks user activation/version, authoritative Super Admin assignment, installation identity and PlaintextDevelopment mode. It derives CompanyId from CurrentCompany and an active Company row inside its writer transaction. ExpectedCompanyId is only an optimistic scope check, never an owner assignment. Customer/address/contact IDs are resolved within that company and customer. Stale identity rejection clears the cached session through the existing gate.

CompanyViewModel now publishes the selected company ID as a notification property, avoiding same-name-company ambiguity. Customer UI selections, editor state and pending view work are cleared when that ID changes or authentication ends. The backend independently rejects stale company context regardless of UI state.

## C. Database design

New additive migration: **20261119000100_CustomerMaster**. New model configuration: SchemaV4. The previous migrations and their designers/SQL were not edited; the current model snapshot was advanced.

Five new tables, taking the model from 34 to **39 domain tables**:

| Table | Purpose |
|---|---|
| Customers | Company-owned identity/classification/tax data, business contact endpoint, credit/preferences, notes and creator/updater audit fields |
| CustomerAddresses | Multiple billing/shipping/both addresses, recipient details, domestic/international location, contact details and active/default flags |
| CustomerContacts | Independent contact persons, role/department, phones/email/WhatsApp, primary/active flags and notes |
| CustomerBankDetails | Optional single customer payment/refund-reference record; inactive content is retained |
| CustomerOpeningBalanceDrafts | Exactly one application-created unposted opening draft per customer; stable draft ID and concurrency version |

All records have stable Guid IDs and optimistic Version tokens. Customer code has a unique (CompanyId, Code) index and canonical uppercase ASCII checks. Normalized names, trade names, GSTIN, PAN, mobile and status/registration have appropriate search/duplicate indexes; GSTIN/PAN/name/mobile are warnings, not unique merge keys.

There are 15 explicit indexes in the new SQL plus primary/alternate-key indexes. Composite company/customer foreign keys use RESTRICT. Address/contact alternate keys also expose (CompanyId, CustomerId, Id) for future owned-document references.

Partial unique indexes enforce at most one billing default, shipping default and primary contact per customer. Checks require active, compatible default addresses and active primary contacts. Boolean flags, statuses, classifications, monetary ranges and opening nature are constrained. Transactions clear displaced unique flags before assigning replacements.

Ten new triggers prevent physical deletion and identity/owner reparenting for the five customer tables. Omitting an existing address/contact in an authorized aggregate update retires it; it does not delete it. Inactive customers/entries remain referencable.

The migration creates only new tables, indexes and triggers. It contains no existing-table DROP/ALTER or business-data migration. Its Down operation refuses destructive downgrade.

**No installed database was opened/migrated/reset. No marker was changed.** This migration was deliberately not added to the startup maintenance allowlist, so it cannot auto-apply to an existing installation. Applying it and validating runtime behavior require a separate authorized phase. Existing installed binaries are left unchanged by isolated build output.

## D. Business rules

### Identity, classification and duplicate warnings

Customer ID/owner are service/database controlled. Code is trimmed uppercase, 1–40 ASCII letters/digits/slash/underscore/hyphen, beginning with a letter/digit. Legal name is required, trimmed and bounded; normalized lookup keys use Unicode Form KC and invariant uppercase. Code can be edited under version/uniqueness checks while the stable CustomerId remains unchanged.

Individual, Business, Government and Other are supported. Status is Active, Inactive or Blocked. Trade/display names, classification labels, since date, general mobile/email, notes and Super Admin internal remarks are optional.

Potential duplicate identity changes warn on normalized legal/trade name, GSTIN, PAN or normalized mobile (including contact-person mobile). Confirmation saves a separate customer; there is no merge operation. Code conflicts remain hard errors. Ordinary non-identity edits do not repeatedly reconfirm already accepted similarities.

### Customer GST/PAN

Customer-specific rules intentionally validate GSTIN even though the earlier Company Master revision removed those checks for the seller. Company validation behavior is unchanged.

Registered categories Regular, Composition, SEZ Unit and SEZ Developer require GSTIN. Unregistered/Consumer allow no GSTIN. Government/Special Entity applicability is not inferred automatically: GSTIN is optional for that category; supplied identifiers are checked.

Validation includes uppercase normalization, structure, base-36 checksum, registered-state prefix, PAN syntax and embedded-PAN consistency where the embedded identifier is PAN. Government/Special Entity supports the PAN-or-TAN identifier portion and two alphanumeric entity positions; UIN/other special schemes are not represented as government-verified.

[CBIC registration rules](https://cbic-gst.gov.in/gst-registration-rules.html) describe the state/PAN-or-TAN/entity/checksum structure. [CBIC's deductor/collector registration guidance](https://cbic-gst.gov.in/pdf/faq-manual/faqs-registration-tax-deductor-tax-collector.pdf) distinguishes PAN/TAN registrations. Format checks do not verify government registration, legal name or current status.

Rates, HSN/SAC, reverse charge, export/SEZ consequences and final place of supply are not inferred. Supply/exemption labels and notes are stored inputs only. Government/special-entity applicability and transaction tax policy require business review before production.

### Addresses and contacts

Multiple addresses/contacts are supported, with a technical bound of 50 each in an aggregate. Both-type addresses appear in both UI tabs but are persisted once. Billing/shipping/registered GST states are independent.

Indian addresses require a supported state code and six-digit PIN syntax. State names derive from the existing domain catalog. Country codes use installed .NET region data. International state/province is free text; postal codes permit letters, digits, spaces/hyphens and may be absent where applicable. PIN syntax is not geographic verification.

Telephone fields accept 7–15 digits and common display separators. Mobile matching normalizes common Indian +91/0091 display variants. Email checks are syntactic. Contact records are independent of addresses, with one active primary at most.

Existing entries are deactivated; unpersisted entries may be discarded locally. Referenced future snapshots are never edited by Customer Master changes.

### Credit, preferences and money

Stored policy includes payment terms, days, limit, hold, payment/delivery preference, currency, PO-required flag and billing instructions. Days are nonnegative with a defensive maximum of 36,500. Currencies are recognized by installed .NET region data, default INR.

Money is C# decimal, converted exactly to SQLite INTEGER units at scale 10,000. Values are nonnegative, at most 99,999,999,999,999.9999 and at most four decimal places. The converter rejects unsupported precision instead of rounding/truncating. SQL checks reject REAL storage. Currency-specific transaction rounding remains future work.

Inactive/Blocked customers are excluded from new invoice selection. Credit hold is conservatively fail-closed for new selection; a transaction-specific cash/credit override policy is not implemented. No credit-limit enforcement against live receivables is claimed. The meaning of a zero limit for later credit enforcement must be approved in that future workflow.

### Opening drafts and bank details

Opening amount is absolute and nonnegative. Debit means customer owes the business; Credit means business owes the customer. Zero amount requires Zero nature; nonzero Debit/Credit requires an as-of date.

This is one unposted draft record, not a second balance or journal. No outstanding is calculated. OpeningBalanceImportCandidate exposes the stable source draft ID/version. Future approved posting must atomically create the ledger entry and enforce a unique (CompanyId, SourceDraftId) import key. Version is for stale-approval detection, not a new posting identity. After posting, draft changes must be governed by that future lifecycle; no posting/approval mechanism exists here.

Bank metadata is optional and sensitive: bank/holder/account/IFSC/branch/UPI/reference notes only. No password/OTP/PIN/CVV/card field exists. Bank data/internal remarks require Super Admin and are omitted from lists/snapshots/diagnostics. There is no field-level encryption; use synthetic data only.

## E. Invoice readiness and integration

GetInvoiceEligibleCustomersAsync returns concise active, non-held candidates with default bill-to/ship-to addresses, registration and credit policy. Eligibility explains missing/blocking master details. An authorized workflow can resolve a snapshot using explicit eligible bill/ship IDs instead of defaults.

No nondefault address/contact is silently guessed. Snapshot resolution checks customer status/hold and address/contact ownership/activity. It returns an immutable CustomerInvoiceSnapshot with version/capture time, customer identity, tax identifiers/status, selected copied addresses, payment/delivery/PO metadata and optional selected contact.

PAN is included only when requested. Contact phone/email inclusion is opt-in. Customer bank details, internal remarks, opening draft and any fabricated financial totals are excluded. Snapshot data is returned, never stored back into Customer Master.

Future invoices must persist their own immutable copy at the approved lifecycle point and reconcile CustomerVersion inside that finalization transaction. Historical invoices must render their saved copy, including for inactive customers, rather than resolving current master data again.

ICustomerFinancialSource explicitly returns unavailable with a null outstanding balance until real invoicing/accounting exists. No hardcoded zero balance, invoice, receipt, ageing or ledger entry is produced.

## F. WPF workflow

The 19-entry shell now offers Company and Customer Masters; Product Category and the other 17 destinations remain Planned.

Select an active company in Company Master, then open Customer Master. List actions support search by code/name/trade name/GSTIN/mobile, status and registration filters, pagination, Add/Edit/View, Activate/Deactivate/Block and refresh.

Nine tabs cover General, GST/Tax, Billing, Shipping, Contacts, Terms/Credit, Opening Draft, Bank, and Notes/Audit. Required fields/inline errors and a summary are displayed. Multiple addresses/contacts have add/edit/default/primary/deactivate controls. View mode is read-only with scrolling. Credit hold and unposted-draft semantics are visible.

Save/duplicate confirmation uses tracked async operations, cancellation, version checks and DTO reload. Duplicate submissions are disabled. Cancel, navigation, regeneration, logout and close protect unsaved changes. No invoice/receipt/ledger screen was activated.

## G. Build and permitted verification

Existing approved SDK/packages only; no application dependency or package version was added/upgraded. Existing EF Core.Design 10.0.15 was reused in an ignored design-time source generator. It builds EF metadata/DDL without opening a database; no fixture or test database is involved.

Release outputs are isolated under .local/stage4c-artifacts, preserving currently installed application binaries. Commands used:

~~~powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build GTS.Billing.slnx -c Release --artifacts-path .local/stage4c-artifacts --source .packages -p:NuGetAudit=false
dotnet build GTS.Billing.slnx -c Release --artifacts-path .local/stage4c-artifacts --no-restore
~~~

The initial restore used only cached approved packages. NuGetAudit=false applied to that offline invocation; no persistent NuGet/security configuration was changed.

Final Release build succeeded with **0 warnings and 0 errors**. All five application projects and the existing test project compiled; no test assembly was executed. Source hash comparison found all 16 existing test source/project files unchanged. Exact file inventory follows below.

## H. Deferred verification

**No automated tests were written, modified, generated or run. No dotnet test, test fixture, test database, UI launch, smoke run or migration execution was performed.** The solution build compiles existing test projects but does not execute them.

Successful compilation is not runtime acceptance. SQLite migration execution/constraints, EF query translation, save/reload, cross-company/session behavior, WPF bindings/keyboard/layout, cancellation/shutdown and snapshot correctness remain unverified. Existing test expectations and the pre-existing automated smoke hook's navigation/schema assumptions are intentionally untouched and need review during the separately authorized testing phase.

## I. Security, compatibility and scope

The original Program/native mutex acquisition before DI/database access is unchanged. Customer database operations use ApplicationLifetime admission/cancellation and dispose contexts/transactions before accepted work completes. Logout still drains and exits. Authentication hashing/recovery/stale-session logic is reused without replacement.

Audit events commit with mutations and use authenticated actor IDs and actual CompanyId. Notes, contacts, identifiers, bank data, password/recovery inputs and full exception payloads are not logged. EF sensitive-data logging remains disabled.

No real database/data/identity marker, Windows setting/account/ACL, AI-Harness, root configuration, Git configuration/history or unrelated business module was modified. Only Billing working files and ignored build/design artifacts changed; no commit/push was made. No financial-year, invoice/posting, stock, product, reporting framework, backup/restore or encryption workflow was added.

Stop after this stage. Installed migration, runtime acceptance, automated testing and later modules require separate authorization.

## Exact file inventory

Application/document sources: **14 created, 9 modified, 0 deleted**. Paths below are relative to D:\Projects\ai-gtx-inventorysystem\Billing-System.

Created:

- docs/stage4c-customer-master.md
- src/GTS.Billing.Application/Customers.cs
- src/GTS.Billing.Application/IApplicationOperationCoordinator.cs
- src/GTS.Billing.Domain/CustomerRules.cs
- src/GTS.Billing.Domain/Customers.cs
- src/GTS.Billing.Infrastructure/CustomerStore.cs
- src/GTS.Billing.Infrastructure/SchemaV4.cs
- src/GTS.Billing.Infrastructure/Migrations/20261119000100_CustomerMaster.cs
- src/GTS.Billing.Infrastructure/Migrations/20261119000100_CustomerMaster.Designer.cs
- src/GTS.Billing.Infrastructure/Migrations/CustomerMaster.sql
- src/GTS.Billing.Presentation.Wpf/CustomerEditorModels.cs
- src/GTS.Billing.Presentation.Wpf/CustomerView.xaml
- src/GTS.Billing.Presentation.Wpf/CustomerView.xaml.cs
- src/GTS.Billing.Presentation.Wpf/CustomerViewModel.cs

Modified:

- README.md
- src/GTS.Billing.Infrastructure/BillingDbContext.cs
- src/GTS.Billing.Infrastructure/Migrations/BillingDbContextModelSnapshot.cs
- src/GTS.Billing.Presentation.Wpf/App.xaml.cs
- src/GTS.Billing.Presentation.Wpf/ApplicationLifetime.cs
- src/GTS.Billing.Presentation.Wpf/CompanyViewModel.cs
- src/GTS.Billing.Presentation.Wpf/MainWindow.xaml
- src/GTS.Billing.Presentation.Wpf/MainWindow.xaml.cs
- src/GTS.Billing.Presentation.Wpf/Navigation.cs

No files under tests/ were added or changed. All existing migration files/resources remain unchanged except the current model snapshot, which is expected to advance with a new migration.

Ignored design-only authored utility files: .local/stage4c-migration-design/MigrationDesign.csproj and Program.cs. They use the already approved EF design library to emit source; they are not application/test projects in the solution and contain no data setup, fixtures, assertions or database-open calls. Generated schema/metadata review artifacts are in .local/stage4c-migration-source; build outputs are in .local/stage4c-artifacts and .local/stage4c-design-artifacts. Automatic bin/obj/package/CLI artifacts are not included in the source inventory.

## J. Scope confirmation

No real database, installation marker, Windows configuration, AI-Harness, root repository configuration or Git configuration/history was changed. Only the authorized Billing working files and ignored build/design artifacts changed. No test source was touched and no tests, UI, migration or financial workflow were executed. Stage 4C implementation and Release compilation are complete; runtime acceptance and all later work remain deferred.



## Customer input update

GSTIN, PAN and PIN/postal code are optional on Customer Master. GSTIN and PAN are still trimmed and uppercased, without number format, checksum or cross-field matching rules. Indian billing and shipping addresses use a State / UT dropdown populated from the existing state catalog. The GST registered state uses the same dropdown.

Migration 20261119000500_CustomerInputFlexibility removes the SQLite PIN format constraint. It copies all address columns and recreates the ownership and default-address protections.
