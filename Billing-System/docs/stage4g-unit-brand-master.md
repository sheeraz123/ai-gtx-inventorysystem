# Stage 4G Unit Master, Brand Master and Product Unit Integration

## Using the masters

Select an active company, then open **Master → Unit Master** or **Master → Brand Master**. Use **Add** to create a record. A code is fixed after creation. Double-click a populated row or choose **View** to see persisted details without editing. Use **Edit** separately and **Activate / Deactivate** to change availability. The list searches by normalized code or name after three effective characters, with Stage 4F's search delay (300 ms by default) and cancellation. It also filters by active status. Stage 4F supplies the master titles and shared action labels.

Unit Master stores the company code, name, symbol, type, decimal precision from zero to six, optional UQC, description, sort order and status. A UQC is a separately entered mapping. The application does not determine eligibility for a GST filing or verify codes against an external service. Brand Master supports an optional manufacturer description and PNG/JPEG logo. Manufacturer information is independent of the primary brand link. Master changes retain UTC timestamps and the acting account. Deactivation and edits that would alter the semantics of a referenced unit are blocked. Brand deactivation removes it from new selections while existing links remain readable.

## Product mapping and conversions

In **Product / Item Master**, select a saved product and choose **Units and brand** to view its configuration or **Edit units and brand** to change it. The brand filter matches persisted brand links. Create a product using the existing Product editor before adding these links.

The detail panel shows original brand and unit text, stored sales and purchase factors, and whether each unresolved value has zero, one or multiple normalized matches among active records in that company. Exact matches are suggestions. Even a unique match requires explicit selection. Blank and ambiguous fields are preserved. The migration does not populate master records or change existing product data.

An optional ProductMeasureProfile stores the stable brand and base-unit references and material classification. ProductUnitConversion stores one decimal factor per product and unit. The two default flags identify the sales and purchase units independently and can both refer to the same conversion. The base factor is one. A product needs an active base unit before saving conversions. Changes are transactional. Replacing a base with active non-base factors requires deactivating those factors first. Draft and persisted conversion rows are visibly distinct through the discard and deactivate actions.

A linked master value takes precedence in the supported grid and snapshot. Original text and factors remain in the existing Product row so the transition preserves data and supports fallback. The original editor prevents changes to linked legacy fields. If an unresolved commercial unit would mix an old factor with a different new base, snapshot resolution asks for an explicit default. A rate attached to a different unit or factor requires explicit pricing by the future document workflow.

## Footwear, sports and manufacturing compatibility

| Product or material | Example configuration |
| --- | --- |
| Finished shoes | PAIR as base and sales unit. BOX as purchase unit, with 12 PAIR per BOX for this example product. |
| Badminton racket | PCS as base and sales unit. CARTON with a product-specific factor such as 20 PCS. |
| Shuttlecock | PCS as base, with DOZEN or TUBE as an optional conversion. A tube may contain six pieces for one product. |
| Soles and laces | SET or PAIR as defined by the product. A PACK of laces can map to PAIR per product. |
| Leather, fabric and adhesive | SQFT for leather, MTR for fabric and KG or LTR for adhesive. A ROLL-to-MTR or DRUM-to-KG factor is product-specific. |
| Apparel, packaging and services | Use configured units such as PCS, BOX, BAG, BUNDLE, CM, HOUR, JOB or SERVICE. |

PCS, PAIR and SET are distinct. A pair can mean matched left and right shoes. The application ships no example records or conversion factors. Industry-independent classification includes Raw Material, Component, Work in Progress, Finished Goods, Packaging Material, Consumable, Trading Goods and Service. Brands can be in-house or third-party. Products may remain unbranded.

## Invoice and variant contracts

Product snapshot resolution captures the product ID, version, code, name, primary brand, selected unit and factor, quantity precision, optional UQC, HSN/SAC reference and pricing/tax defaults. A future document supplies the actual quantity, unit price, tax classification reference and optional immutable variant description. The variant contract can carry size, size system, color, style, model, material and segment. A UK, US, EU or custom size is interpreted only in its own system. The future variant model may add variant-specific SKU and stock while the brand and unit profile stays attached to the product.

A future BOM should reference product/variant and use decimal quantities with an explicit unit conversion. Production orders, material issues, finished-goods receipts, size/color stock, wastage, scrap and costing should keep their own transaction records and historical unit factors. Stage 4G adds no invoice posting, stock movement, variant engine, BOM or manufacturing transaction.

## Database and security

Migration `20261119000800_UnitBrandMaster` adds `Units`, `Brands`, `ProductMeasureProfiles` and `ProductUnitConversions`. It adds catalog entries for Unit and Brand so roles can assign their permissions. It does not assign any permissions to roles. The master codes and normalized names are unique within a company. Composite foreign keys bind product, brand and unit references to the same company. Filtered unique indexes enforce at most one sales and one purchase default per product. The service uses decimal arithmetic; SQLite stores the factor in EF's decimal representation. The migration does not run a backfill, change product columns, or rebuild an existing table.

The application uses the existing authenticated session, selected-company context, company-scoped RBAC and shutdown operation coordinator. The new master permissions cover View, Create, Edit and StatusChange. Product links require Product.View and Product.Edit for writes. Existing mutex, database identity verification and the development migration allowlist remain in place. Installed databases were not updated.

## Files

Paths are relative to Billing-System.

| Project or folder | Created |
| --- | --- |
| Domain | `UnitBrand.cs` |
| Application | `UnitBrand.cs` |
| Infrastructure | `UnitBrandSchema.cs`, `UnitBrandStore.cs` |
| Infrastructure/Migrations | `20261119000800_UnitBrandMaster.cs`, `20261119000800_UnitBrandMaster.Designer.cs` |
| Presentation.Wpf | `UnitBrandView.xaml`, `UnitBrandView.xaml.cs`, `MeasureEditorDialog.cs` |
| docs | `stage4g-unit-brand-master.md` |

| Project or folder | Modified |
| --- | --- |
| Application | `PermissionCatalog.cs`, `ProductCatalog.cs`, `SettingsLabels.cs` |
| Infrastructure | `BillingDbContext.cs`, `ProductCatalogStore.cs` |
| Infrastructure/Migrations | `BillingDbContextModelSnapshot.cs` |
| Presentation.Wpf | `App.xaml.cs`, `CatalogView.xaml`, `CatalogViewModel.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Navigation.cs` |

EF metadata generation used an ignored local design directory and opened no database connection.

## Build and launch evidence

From Billing-System, using the existing local package cache and ignored Stage 4G build artifacts:

```powershell
dotnet restore GTS.Billing.slnx
dotnet build GTS.Billing.slnx -c Debug
dotnet build GTS.Billing.slnx -c Release
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
```

Restore succeeded. Debug and Release each passed with zero warnings and zero errors. The Windows Debug process displayed **GTS Billing — startup stopped** and exited with code 1. The dialog reported that this database needs a reviewed update and that no replacement database was created. Startup stopped before login. The master screens, saving and reopening records, role enforcement in the running application, snapshot resolution and the migration itself remain unverified at runtime. The Stage 4G migration awaits separate approval before use with the installed synthetic development database.

No automated tests were created, modified, deleted or run. AI-Harness, Windows provisioning, installed and real business databases, and Git history were left untouched.

## Approved development database update and recovery

The subsequent approved update created a verified safety backup and applied the Stage 4G migration. Its original preservation check stopped startup because it counted the two new permission catalog rows as changes to existing data. The targeted recovery compared all prior rows and values against the hashed backup and allowed only the exact approved additions. It verified migration history, integrity and foreign keys under normal exclusive application ownership before restoring the original Ready marker. The app then reached Sign in. Debug and Release were rebuilt with zero warnings and errors. No automated tests were run.

The recovery and prevention changes are in `DatabasePreservation.cs`, `DevelopmentDatabaseMaintenance.cs`, and `App.xaml.cs`. The receipt is `completed-recovery.json` in the original safety backup directory.
