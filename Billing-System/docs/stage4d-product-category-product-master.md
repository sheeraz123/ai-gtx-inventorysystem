# Stage 4D — Product Category Master and Product Master

In Company Master, select an active company as Current. Sign in as Super Admin and choose Product Category Master or Product / Item Master. Create a category before adding products. Both lists are paged. A row double-click opens persisted details in View mode. Use Edit to make changes. Save is hidden in View mode. Leaving an edited record prompts for confirmation.

- **Category Master** supports create, view, edit, activation, deactivation, parent selection and display order. Codes are immutable and unique within a company. Normalized names are unique within sibling groups, including root categories. An active category needs an active parent. The service and database reject cycles and prevent deactivation while active children remain. Existing products retain their category associations.
- **Product Master** stores goods and services, a required category, optional brand, SKU, barcode, manufacturer part and model numbers, descriptions, specifications, warranty and internal remarks. Name, SKU and barcode matches produce a confirmation before a separate record is saved. Any active category can be chosen, including a non-leaf category. An inactive category can be retained on an existing product but cannot receive a new assignment.
- Unit administration is absent in this checkout. Each product stores a base unit and optional sales and purchase labels. Every conversion points directly to the base. Blank sales or purchase units use the base. The base factor is 1, equal unit names require equal factors, and quantity precision is zero through four decimals.
- GST master data includes an optional HSN or SAC, provisional percentage, exemption flag and notes. The product editor validates format only. Final tax, eligibility and statutory reporting rules depend on the future transaction. [GSTN HSN guidance](https://tutorial.gst.gov.in/downloads/news/advisory_on_revamped_search_hsn_code_functionality_final.pdf)
- Purchase rate, sales rate, optional MRP, tax-inclusive flags, currency and effective date are defaults. Decimal amounts, conversion factors, tax rates and stock thresholds use scaled SQLite integers with four decimal places. Inputs requiring rounding are rejected.
- Goods can opt into stock tracking and record reorder, minimum, maximum and a storage label. Services cannot opt in. The module stores settings only, with no stock availability, stock movement or valuation. Negative-stock configuration is deferred.
- The bounded image processor validates, decodes and re-encodes PNG/JPEG uploads. Optional images are stored in SQLite without a dependency on the original file. Replace and remove changes take effect on Save. List queries do not load product images.
- Product lookup returns active current-company products with selection data. The immutable snapshot captures identity, descriptions, tax hints, quantity precision, selected unit and factor, and the applicable default rate and tax-inclusive flag. The future document workflow must capture actual quantity, price, discounts, tax and totals and persist the snapshot for reprints. There is no live stock eligibility check.

## Search and access

Search starts after 300 ms with at least three trimmed characters. Blank or shorter input restores the default text scope and keeps other filters. The application cancels superseded requests and checks the current filter before displaying results. Code, names and identifiers are queried in SQL. Search on normalized names supports Unicode normalization. SQLite case folding on other text fields is ASCII-based. Category filters use the current company's hierarchy metadata.

The catalog uses the existing authentication service, operation coordinator and installation-checked contexts. A transaction rechecks the user's active identity, version, Super Admin assignment and the current active company. Request company IDs are assertions, not authorization. Changes use optimistic versions and append audit events. Switching company or leaving the shell clears the catalog. Existing global mutex, startup checks, database path policies and shutdown drain are preserved.

## Schema

Migration `20261119000300_ProductCatalog` adds `ProductCategories` and `Products` after Company Presentation. It adds company-scoped unique code and normalized sibling indexes, composite category foreign keys, search indexes, checks and retention/hierarchy triggers. References are restrictive and audit actor IDs reference Users. No catalog data is seeded and no installed database is migrated. The approved migration allowlist is unchanged.

## Source and documentation inventory

Created

- `docs/stage4d-product-category-product-master.md`
- `src/GTS.Billing.Domain/ProductCatalog.cs`
- `src/GTS.Billing.Application/ProductCatalog.cs`
- `src/GTS.Billing.Infrastructure/ProductCatalogStore.cs`
- `src/GTS.Billing.Infrastructure/SchemaV6.cs`
- `src/GTS.Billing.Infrastructure/Migrations/20261119000300_ProductCatalog.cs`
- `src/GTS.Billing.Infrastructure/Migrations/20261119000300_ProductCatalog.Designer.cs`
- `src/GTS.Billing.Infrastructure/Migrations/ProductCatalog.sql`
- `src/GTS.Billing.Presentation.Wpf/CatalogView.xaml`
- `src/GTS.Billing.Presentation.Wpf/CatalogView.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/CatalogViewModel.cs`

Modified

- `src/GTS.Billing.Infrastructure/BillingDbContext.cs`
- `src/GTS.Billing.Infrastructure/Migrations/BillingDbContextModelSnapshot.cs`
- `src/GTS.Billing.Presentation.Wpf/App.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/CustomerEditorModels.cs`
- `src/GTS.Billing.Presentation.Wpf/MainWindow.xaml`
- `src/GTS.Billing.Presentation.Wpf/MainWindow.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/Navigation.cs`

Ignored local files include the copied EF metadata-generation project and program in `.local/catalog-migration-design`, its generated designer, snapshot and schema in `.local/catalog-migration-source`, and build outputs in `.local/catalog-design-artifacts` and `.local/catalog-artifacts`. The design utility generated source without opening a database.

## Verification

Run from Billing-System using the cached dependencies and isolated MSBuild output under .local/catalog-artifacts.

| Command | Result |
|---|---|
| dotnet restore GTS.Billing.slnx | Succeeded |
| dotnet build GTS.Billing.slnx -c Debug | Succeeded, 0 warnings, 0 errors |
| dotnet build GTS.Billing.slnx -c Release | Succeeded, 0 warnings, 0 errors |

Requested interactive launch:

~~~powershell
$env:ArtifactsPath = Join-Path $PWD '.local/catalog-artifacts'
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
~~~

Automatic approval review rejected both launch attempts before creating a process. The retry was rejected because the application may open or alter the configured SQLite database and the reviewer did not accept the available approval for that runtime access. No WPF window appeared. Login, shell and catalog screens were not reached, and there was no process to close. A direct approval naming the existing synthetic GTSBilling.Development.db is needed to retry the launch. The installed database and its identity marker were not changed. If startup reports a schema mismatch, the missing migrations must be handled in a separately approved development upgrade.

Runtime UI behavior, database migration, save/reload and query execution remain unverified. No automated tests were created, modified, deleted or run. AI-Harness, Windows provisioning and Git history were untouched.
