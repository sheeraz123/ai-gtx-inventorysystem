# Company branding, bank accounts and master grids

## Company Master workflow

Select an active company and choose **Select as Current**. Open that company in View or Edit. Logos & Branding, Bank Accounts, and Invoice Defaults are tabs in the company detail screen.

- **Logos & Branding** manages named logos with usage, display order and active status. The grid shows thumbnails and invoice slots. Add a PNG or JPEG to create a separate logo. Edit a logo to rename it, change its usage or order, change its status, or replace its image. Removal archives an unused logo. Historical references and original image bytes are retained.
- **Bank Accounts** manages bank accounts with masked numbers in the list. Open an account to see its full details. Set or clear the invoice default in this tab or Invoice Defaults. Deactivating the default leaves the company without a default until one is explicitly chosen.
- **Invoice Defaults** selects an optional primary logo, secondary logo and bank. The display controls govern primary logo, secondary logo and UPI. A selected active default bank account is included in the invoice snapshot. Header, footer, signatory name and versioned terms are also editable here. **Preview saved defaults** uses the persisted snapshot resolver and shows logo images and enabled output. It does not render a full invoice.

Company profile saves from the standard editor leave these presentation records alone.

## Storage and historical records

- The existing company-owned `BrandingAssets` table stores the logo catalog. The legacy `Kind` column continues to identify asset families and signatures. Separate name and usage metadata describe logos. Existing `Logo1AssetId` and `Logo2AssetId` in `CompanySettings` are the only authoritative primary and secondary selections. Existing composite foreign keys prohibit cross-company references. The new triggers require distinct, active logo selections on writes.
- Uploads are bounded to 1 MiB and 2048 × 2048 pixels. The WPF image processor checks PNG structure or the JPEG start-of-frame dimensions, decodes with the Windows imaging stack, and stores re-encoded pixels and a small PNG thumbnail. No source path is stored. Legacy assets without thumbnails can still be previewed in details if they have usable image bytes.
- Logo replacement inserts a new asset ID and updates any enabled selection to that new ID when the replacement stays active. The old asset retains its original bytes and becomes inactive and retired. Deactivation clears invoice references. Removal retains the record and is unavailable for selected or historically referenced assets.
- The existing company-owned `CompanyBankAccounts` table adds display order, address, instructions and audit metadata. An existing filtered unique index and active/default check enforce at most one active default. The service clears the previous default before assigning the new one in the same transaction. The existing `IsDefault` flag is the sole source of the invoice bank ID. The settings form carries that ID for selection and stores display preferences separately.
- The new bank editor validates new or changed account digits, IFSC and UPI. It ignores spaces and hyphens in account numbers for duplicate comparison and normalizes IFSC case. The duplicate check uses company, normalized account number and IFSC. Unchanged legacy IDs remain usable. Existing bank records keep their values and IDs.
- `CompanyInvoicePresentation` copies the selected image bytes into immutable arrays, copies versioned terms and captures details from the active default bank account and active default seller GST registration. It excludes disabled outputs and provides no-logo and no-bank states. A future finalization flow should persist the snapshot with its document and reuse it for reprints. No invoice transaction or printing layout is created here.

## Grid interaction

Double-click a data row to load current persisted details in View mode. A header or empty region does not open a record. View mode offers Close and an explicit Edit transition. Save is visible only in Edit mode. Company, Customer, Logos and Bank Accounts use the same row interaction.

Company, Customer, Logos and Bank Accounts use parameterized database queries after 300 ms of typing with at least three trimmed characters. Short or cleared input uses the default list. A new query cancels prior work or prevents older results from being shown. Customer GST and status filters combine with text search and company scope. Company search is installation-wide for Super Admin because it is the company selector. SQLite's `upper()` case handling is limited for non-ASCII company, city, logo and bank text. Customer name search also uses the existing normalized name columns.

Product Category Master is a navigation placeholder in this checkout. It has no grid or persistence implementation.

## Authorization and lifecycle

All company presentation operations require a valid Super Admin session. The store rechecks the installation identity, user version, Super Admin assignment and active current company. Child lookups use the authorized company and record ID. Updates check versions, record the actor and commit the audit event with the mutation. The operation coordinator tracks database work through the existing shutdown admission and drain mechanism. Full bank numbers are shown only in an authorized detail view or included in an enabled invoice snapshot. Bank details and image content are excluded from diagnostics and audit payloads.

## Migration and verification

`20261119000200_CompanyPresentation` follows `20261119000100_CustomerMaster`. It extends the three existing tables, keeps bank and logo records and their references, and initializes new visibility flags to reflect prior configured choices. It replaces the branding update trigger with one that still protects original identity and payload while permitting catalog metadata updates. Old migrations and the reviewed database-upgrade allowlist are unchanged.

The Release solution build was run with `dotnet build GTS.Billing.slnx -c Release` using existing cached packages and isolated build output. Build succeeded with **0 warnings and 0 errors**. EF metadata was generated from the model without opening a database. Installed databases were not migrated. WPF interaction, image decoding, SQLite query execution, migration execution and save/reload behavior remain unverified at runtime. Automated tests were not created, modified, deleted or run.

The current SQLite database is plaintext and for synthetic development data only. Existing installations must complete a separately approved schema upgrade before using this build. No application launch or deployment was performed.

## File inventory

All paths are relative to `D:/Projects/ai-gtx-inventorysystem/Billing-System`.

Created:

- `docs/company-branding-bank-grid-enhancements.md`
- `src/GTS.Billing.Application/CompanyPresentation.cs`
- `src/GTS.Billing.Infrastructure/CompanyPresentationStore.cs`
- `src/GTS.Billing.Infrastructure/SchemaV5.cs`
- `src/GTS.Billing.Infrastructure/Migrations/20261119000200_CompanyPresentation.cs`
- `src/GTS.Billing.Infrastructure/Migrations/20261119000200_CompanyPresentation.Designer.cs`
- `src/GTS.Billing.Infrastructure/Migrations/CompanyPresentation.sql`
- `src/GTS.Billing.Presentation.Wpf/CompanyImageProcessor.cs`
- `src/GTS.Billing.Presentation.Wpf/CompanyPresentationViewModel.cs`
- `src/GTS.Billing.Presentation.Wpf/CompanyPresentationWindow.xaml`
- `src/GTS.Billing.Presentation.Wpf/CompanyPresentationWindow.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/MasterGridInteraction.cs`

Modified:

- `src/GTS.Billing.Domain/Entities.cs`
- `src/GTS.Billing.Application/Companies.cs`
- `src/GTS.Billing.Infrastructure/BillingDbContext.cs`
- `src/GTS.Billing.Infrastructure/CompanyStore.cs`
- `src/GTS.Billing.Infrastructure/CustomerStore.cs`
- `src/GTS.Billing.Infrastructure/Migrations/BillingDbContextModelSnapshot.cs`
- `src/GTS.Billing.Presentation.Wpf/App.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/CompanyView.xaml`
- `src/GTS.Billing.Presentation.Wpf/CompanyView.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/CompanyViewModel.cs`
- `src/GTS.Billing.Presentation.Wpf/CustomerView.xaml`
- `src/GTS.Billing.Presentation.Wpf/CustomerViewModel.cs`

Local design and build files are under `.local/presentation-migration-design` (the EF design project's `MigrationDesign.csproj` and `Program.cs`), `.local/presentation-migration-source` (generated designer, snapshot and SQL), `.local/presentation-design-artifacts`, and `.local/presentation-artifacts`. These directories do not contain test scripts or databases.


## Invoice bank and GST registration defaults

In Company Master, open the active current company in Edit mode. Use **Bank Accounts** to mark an active account as the invoice default. The invoice snapshot includes that account without a separate bank visibility checkbox. UPI remains optional.

Use **GST Registrations** to add GSTIN and state records. Mark one active registration **Default for invoices** and save it. Choosing another default clears the previous selection in the same transaction. Existing GSTIN and state are retained when a registration is deactivated. The snapshot captures the chosen seller GSTIN and state and does not calculate tax.

Migration 20261119000400_InvoiceTaxDefault adds the default flag, an active-default check and a filtered company-unique index. A company with one existing active registration keeps it as the default. Companies with multiple active registrations must choose one. The migration is not applied to the installed development database by this change. Full invoice rendering is still deferred.
