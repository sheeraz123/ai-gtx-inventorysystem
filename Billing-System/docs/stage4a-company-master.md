# Stage 4A — Company Master

## Result and boundaries

Implemented and verified on 18 November 2026 using the existing .NET SDK 10.0.403. Release build: **0 warnings, 0 errors**. Complete Billing test suite: **127 passed, 0 failed, 0 skipped**. This includes the two disposable child-process crash cases that were excluded during Stage 3.5.1.

Company Master is functional through WPF bindings, application authorization/validation, EF Core, SQLite and reload. The other 18 navigation destinations remain Planned. Exactly five application projects remain.

**SQLite remains unencrypted and suitable only for synthetic development/testing.** No installed database was opened for application use, migrated, recreated or reset during this work. No installed marker, configuration, machine identity, Windows permissions, AI-Harness files, root configuration, Git settings, branch or history were changed. Billing source and local ignored build/test artifacts changed; no commit or push was made.

The configured development database is an established installation and must not be treated as a disposable fixture. The new build deliberately rejects an older migration history. Upgrading that database requires a separate, explicitly approved operation with exclusive ownership, a verified safety copy and preservation of its marker/identity. This task does not supply an automatic installed-database migration or a bypass flag.

## Architecture and authorization

- Domain: CompanyDraft and CompanyRules centralize normalization and business validation.
- Application: CompanyService accepts typed CompanyRequest operations and returns CompanyResult outcomes/field errors. Operations are Create, Update, Get, List/Search, SetActive, Select and Current.
- Infrastructure: CompanyStore opens a fresh checked context per operation. It uses the existing immediate SQLite writer transaction, verifies installation identity/state, user activation/version and authoritative singleton Super Admin assignment, and commits company/children/audit together.
- WPF: CompanyViewModel owns commands and editor state; CompanyView contains list and six editor tabs. No SQL or EF persistence is placed in the view model. MainWindow provides navigation, discard prompts and the persisted current-company header.
- Printing: unchanged. Invoice/letterhead printing is not implemented.

There is no implemented permissions evaluator to extend. All Company Master reads and writes require a valid Super Admin session; ordinary accounts are denied. Existing roles/permission schema is retained without activating administration.

AuthenticationService.RunAuthenticatedAsync holds the existing authentication gate across a protected operation. This prevents local login/logout/recovery from racing it. CompanyStore independently checks the identity version and administrator assignment inside its writer transaction, covering recovery through another service instance between initial validation and mutation. A rejected stale identity is cleared under the authentication gate. Password hashing, recovery-code validation, throttling and existing logout semantics are unchanged.

WPF operations run through ApplicationLifetime with cancellation and background execution for SQLite work. Clearing an authentication/editor context cancels its pending work; cancelled results are not rebound to a logged-out view. Shutdown closes admission, cancels/drains accepted operations, disposes their transactions/contexts and retains the original owning-thread mutex release. A cancellation near commit can mean a save completed: the UI tells the user to refresh rather than promising rollback or retrying automatically.

## Data model and migration

New forward-only migration: **20261118000100_CompanyMaster**.

The original **20261116000100_InitialDevelopmentSchema**, its designer, SQL and invariant triggers were not edited. The current EF model snapshot was advanced normally. There are now **34 domain tables**, plus EF migration bookkeeping.

| Existing table | Added columns |
|---|---|
| Companies | BusinessType, EstablishedOn, RegistrationType (default Unregistered), Tan, Cin, Udyam, UpdatedUtc, UpdatedByUserId |
| CompanyContacts | Purpose |
| CompanyAddresses | Line2, Locality, District |
| CompanyBankAccounts | AccountType, UpiId |
| BrandingAssets | Content (bounded BLOB), ContentType |
| CompanySettings | InvoiceHeader, InvoiceFooter, SignatoryName |

Existing Company fields supply ID/code/legal name, trade name through DisplayName, tagline, PAN, activation and creator metadata. Existing child tables supply addresses, GST registrations/mappings, contacts, accounts, terms and branding revisions.

New CurrentCompany table has Slot as a checked primary key fixed to 1 and a required CompanyId foreign key. Zero rows means no company selected; one row means exactly one current context. It does not restrict the count of company records or active companies.

Additional indexes cover the updater foreign key, unique company/contact-purpose pairs (for non-null purposes), and the selection foreign key. New triggers reject inactive selections, deactivation of a currently selected company without first clearing selection, mutation of an existing code, and noncanonical ASCII codes on insertion. Existing uppercase-code validation plus uniqueness prevents case-variant duplicates. Older noncanonical codes are retained by migration and require deliberate review, not silent renaming.

The branding BLOB has a database size constraint of 45 bytes through 1 MiB, or null for legacy metadata-only assets. Existing immutable branding/terms/snapshot/audit constraints and company-scoped composite foreign keys remain in force. No business-history cascade delete was added.

Migration SQL uses additive columns, indexes, a new table and triggers; it does not rebuild existing tables or remove their triggers. Downgrade remains intentionally unavailable. Only explicit fresh synthetic initialization applies migrations automatically; normal startup still requires exact migration-history equality.

### Current-company policy

Multiple company records are preserved. Selection atomically replaces the singleton reference. Inactive companies cannot be selected. Deactivating the current company clears its selection and deactivates the company within one transaction. Reactivation does not automatically select it.

Future billing modules must begin their authoritative writer transaction, read the current CompanyId inside that transaction and retain it through commit. They must not resolve a mutable UI label and then start an unrelated transaction. Selection uses the same SQLite writer exclusion and cannot overlap that database write transaction. Transaction-scoped company access and company-owned foreign keys remain mandatory integration requirements; no future billing workflow is claimed operational.

### Existing multi-record data

The editor maintains the primary/default billing address, its selected active GST registration, six purpose-labelled contact entries and the default bank account. It preserves additional addresses, GST registrations, unlabelled legacy contacts, bank accounts, Logo2 and signature references.

Changing GST/state creates a replacement primary address and mapping rather than violating or deleting old mappings. The displaced managed registration is deactivated when its GSTIN changes. Default dispatch status is retained when replacing the primary address. Changing bank identity retains the old account as inactive and establishes the new eligible default. Other active accounts prevent removing the only default. Other active GST registrations prevent falsely marking the company Unregistered or saving a conflicting PAN.

Complete administration of multiple addresses, registrations, extra accounts, Logo2 and scanned signatures is not part of this single-primary editor. Those records remain stored; there is no destructive consolidation.

## Fields and validation

| Section | Fields and behavior |
|---|---|
| General | Stable generated ID; required immutable code; required legal name; optional trade name, multiline tagline, business type and establishment date; list activation/deactivation |
| Address | Required line 1/city/state/PIN; optional line 2/locality/district; country IN; state name derived from controlled code list |
| Contacts | Primary/alternate mobile, landline, email, http/https website and contact person |
| Tax | Regular, Composition or Unregistered; optional/required GSTIN according to type; PAN; optional TAN, CIN/LLPIN and Udyam |
| Banking | Optional account group: bank, holder, account number, IFSC, branch, account type and UPI ID |
| Branding | Primary PNG logo, invoice header/footer, authorized signatory name and versioned terms |
| Audit | UTC creation/update timestamps and authenticated creator/updater IDs, not user-editable fields |

Code is trimmed, uppercase ASCII, 1–30 characters, beginning with a letter/digit and then letters/digits/underscore/hyphen. Legal name is trimmed and 1–200 characters. Ordinary optional fields are bounded to 200 characters; tagline/header/footer to 1,000 and terms to 8,000. Establishment date uses YYYY-MM-DD and cannot be in the future.

Stage 4A's address editor supports Indian domestic states/UTs and six-digit PIN syntax. Historical state codes 25/28 and nongeographic/special codes are not offered for new entry. This is a supported-input policy, not a claim that retained historical registrations are invalid. State-code source: [official e-way bill API state list](https://docs.ewaybillgst.gov.in/apidocs/state-code.html). PIN syntax does not establish the postal area's geographic state.

GSTIN validation checks uppercase 15-character structure, PAN structure, state prefix and mod-36 checksum. An explicitly supplied PAN must agree with the embedded PAN. Registered categories require GSTIN; Unregistered does not accept a current GSTIN. RegistrationType is stored as text so future reviewed categories do not require a destructive enum/schema conversion. [CBIC's registration guide](https://cbic-gst.gov.in/pdf/e-version-gst-fliers/regn-GST-onlineversion-07june2017.pdf) describes the state/PAN/checksum components. These are offline checks, **not government registration/status verification**.

Mobile inputs support Indian ten-digit numbers or +91/91 forms; spaces, brackets and hyphens are accepted. Landlines allow 6–15 digits with those display separators. Email and website checks are syntactic; no network calls are made. International address/mobile rules are deferred. Optional identifiers without implemented business-specific checks remain bounded text; no government verification is claimed.

Entering any bank field requires bank name, holder, a 6–34-digit account number and valid IFSC syntax; UPI syntax is checked when supplied. No banking passwords, card credentials or PINs are accepted.

## Branding and historical data

Choose Logo reads at most 1 MiB, requires PNG with dimensions no larger than 2048 × 2048, decodes one frame and re-encodes pixels without retaining the source path/metadata. Preview is bounded and tolerates corrupt image data. The application-service boundary independently checks the PNG envelope/limits, and the database constrains stored size. This bounds image processing; it is not an image-decoder sandbox/security certification.

Logo bytes reside in the shared database, with SHA256, MIME type, random storage identifier and immutable revision metadata. There is no per-user external logo path or new asset directory/ACL provisioning. Replacing/removing the current logo changes the settings pointer while preserving older revisions. Terms behave similarly. Existing historical snapshot foundations and other branding pointers are retained. Issuing invoices and rendering historical reprints remain future work.

## UI workflow

After normal ownership/database validation and Super Admin login:

1. Select Company Master. The list shows code, legal/trade name, city/state, GSTIN, status and current indicator.
2. Search by code, legal name or trade name; Refresh reloads persisted data. Results are bounded to 200 matches; refine search for larger datasets.
3. Add, Edit or View Details opens General, Address, Contacts, Tax, Banking and Branding tabs.
4. Required fields are marked; service validation appears inline. Existing code is read-only. Duplicate submissions and browsing actions are disabled while editing/saving as appropriate.
5. Save commits and reloads the list. Cancel/Close discards the editor. Navigation, regeneration, logout and normal close warn about unsaved edits where no save is already in progress.
6. Activate/Deactivate and Select as Current use the selected row's version. The shell header comes from persisted selection; no company is invented.
7. Logout still drains and exits. Stale sessions are returned to login through the existing authentication view model.

The original GRAFTEX invoice reference and Access database remain unavailable; this work does not claim layout matching, migration compatibility or invoice functionality.

## Verification evidence

Commands executed from Billing-System with project-local DOTNET_CLI_HOME and certificate generation disabled:

~~~powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet test GTS.Billing.slnx -c Release --no-restore --logger 'trx;LogFileName=stage4a-company-master-final.trx' --results-directory TestResults
dotnet build GTS.Billing.slnx -c Release --no-restore
~~~

Final full suite: **127 passed / 0 failed / 0 skipped**, including all previous authentication, mutex/crash, diagnostics, path-policy and persistence cases. Release build: **0 warnings / 0 errors**.

Additional executed runs:

- Baseline permitted subset: 87 passed / 1 failed / 0 skipped (88 selected; two historical crash cases excluded).
- Baseline stalled-diagnostics test in isolation: 1 passed / 0 failed / 0 skipped.
- Focused Company/validation/security/artifact checks: 36 passed / 0 failed / 0 skipped at that intermediate revision.
- Final focused WPF and stalled-diagnostics checks: 2 passed / 0 failed / 0 skipped.
- Intermediate complete run: 125 passed / 1 failed / 0 skipped; redaction test assumed a second best-effort write had completed under parallel CPU load.

The timing tests now explicitly wait for writer entry independently of the caller-latency assertion and execute their diagnostic timing collection without parallel CPU-intensive tests. No production diagnostics behavior changed. The final 127-case run includes that correction and the additional multi-record preservation test.

Thirty-seven new cases cover required/optional/normalized fields, GST checksum/PAN/state, PIN/contact/bank validation, CRUD/reopen/search, uniqueness/immutable code/stale versions, current-selection concurrency and database invariants, authorization/stale recovery, atomic rollback on audit failure, logo bounds/retention, preservation of other master records, lifetime admission/drain/cancellation, old-schema upgrade, view models and actual WPF controls.

RealWpfBindingsCreateReloadAndSelectCompanyWhileOtherModulesStayPlanned creates an off-screen WPF window on an STA dispatcher, signs in using synthetic credentials, fills real bound text/combo controls, saves through the service to SQLite, reloads, selects the company, verifies the bound shell indicator and logs out. Existing subprocess tests retain normal startup/mutex verification. These are automated UI checks, not human visual/accessibility acceptance.

The upgrade test deliberately creates only V1 in a disposable fixture, seeds synthetic data and installation identity, verifies that startup refuses it, explicitly applies V2 to that fixture and verifies preserved records, marker bytes, identity, plaintext header, foreign keys and integrity. No configured installed database is used.

TRX evidence is in the ignored TestResults directory. The final report is stage4a-company-master-final.trx.

## Manual checks and limitations

Manual visual layout, keyboard/screen-reader behavior, discard dialogs, file-picker/image errors and normal operator acceptance remain pending. No screenshot, manual visual pass or installed-app startup is claimed. Real multi-Windows-account behavior and effective provisioning ACLs remain separate acceptance work.

The database, bank details, assets and audit rows are plaintext development data. Direct file writers/administrators can bypass application authorization and correctness triggers. The global mutex coordinates cooperating processes only. No production security, tax compliance certification, backup, encryption or raw-file tamper resistance is claimed.

No paid API, AI-Harness execution, new package, software installation, deployment, commit or push was needed.

Before using this build with the established development database, obtain separate approval for an explicit safe migration. Do not enable automatic startup migration or delete the database/marker to clear that blocker. Stage 4B, financial years, numbering, invoices, other masters, full permission administration, backup/restore and encryption remain unimplemented.

## Exact source/document file inventory

All paths below are relative to D:\Projects\ai-gtx-inventorysystem\Billing-System. Ignored binaries, packages, CLI state, generated test artifacts and disposable databases are not source files.

Created (16):

- src/GTS.Billing.Domain/CompanyRules.cs
- src/GTS.Billing.Domain/CompanySelection.cs
- src/GTS.Billing.Application/Companies.cs
- src/GTS.Billing.Infrastructure/CompanyStore.cs
- src/GTS.Billing.Infrastructure/SchemaV2.cs
- src/GTS.Billing.Infrastructure/Migrations/20261118000100_CompanyMaster.cs
- src/GTS.Billing.Infrastructure/Migrations/20261118000100_CompanyMaster.Designer.cs
- src/GTS.Billing.Infrastructure/Migrations/CompanyMaster.sql
- src/GTS.Billing.Presentation.Wpf/CompanyViewModel.cs
- src/GTS.Billing.Presentation.Wpf/CompanyView.xaml
- src/GTS.Billing.Presentation.Wpf/CompanyView.xaml.cs
- tests/GTS.Billing.Tests/CompanyValidationTests.cs
- tests/GTS.Billing.Tests/CompanyTests.cs
- tests/GTS.Billing.Tests/CompanySafetyTests.cs
- tests/GTS.Billing.Tests/CompanyPresentationTests.cs
- docs/stage4a-company-master.md

Modified (14):

- README.md
- src/GTS.Billing.Domain/Entities.cs
- src/GTS.Billing.Application/Authentication.cs
- src/GTS.Billing.Infrastructure/BillingDbContext.cs
- src/GTS.Billing.Infrastructure/Migrations/BillingDbContextModelSnapshot.cs
- src/GTS.Billing.Presentation.Wpf/App.xaml.cs
- src/GTS.Billing.Presentation.Wpf/Navigation.cs
- src/GTS.Billing.Presentation.Wpf/MainWindow.xaml
- src/GTS.Billing.Presentation.Wpf/MainWindow.xaml.cs
- tests/GTS.Billing.Tests/AuthenticationTests.cs
- tests/GTS.Billing.Tests/FoundationTests.cs
- tests/GTS.Billing.Tests/PersistenceTests.cs
- tests/GTS.Billing.Tests/MigrationArtifacts.cs
- tests/GTS.Billing.Tests/SafeDiagnosticsTests.cs

Deleted: none. New NuGet packages: none. Existing central package versions, project references, appsettings, original migrations, mutex implementation and path policy were not changed.

