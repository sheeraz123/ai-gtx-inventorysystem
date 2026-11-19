# Stage 4H HSN/SAC and GST Rate Masters

## Catalog and desktop workflows

Select an active company and use **Master → HSN/SAC Master**, **GST Rate Master**, or **Classification rate assignments**. Search starts at three trimmed characters, uses the existing Stage 4F debounce (300 ms by default), and cancels stale work. Company and status filters apply to the same query. Double-click a populated row to open its persisted values in a read-only dialog. Use the separate edit action to make authorized changes. The settings text catalog supplies the module titles, treatment labels, effective-date labels and tax component names. These display labels have no effect on stored treatment codes or rate determination.

Classification records hold type, code, description, short description, notes, status and UTC actor audit metadata. A classification's type and code are immutable. The application accepts the supported numeric HSN lengths and six-digit SAC starting with 99, with leading zeroes preserved. Format validation does not establish that a code exists or applies to a product. The current-rate summary reports one applicable context/version or flags multiple contexts. Classification detail lists up to ten associated products for users with product viewing permission.

## Rates and assignments

A company can have multiple immutable rate versions under the same rate code. Each has an independently stored treatment, total, CGST, SGST, IGST, optional UTGST, and percentage cess. Nil-rated, exempt, zero-rated and non-GST stay separate from taxable and from one another. Rates use exact decimal arithmetic and four fractional percentage places. The input bound of 0–100 percent per component is a technical limit. A standard taxable version validates the CGST/SGST and IGST totals, and the optional CGST/UTGST total. Nonstandard cess bases and specialized tax arrangements require future rules.

Rate and assignment intervals are **start-inclusive and end-exclusive**. A blank end is open-ended. Assignment dates must sit inside the referenced rate version's interval. Rate versions cannot overlap for the same company and code. Assignments cannot overlap for the same company, classification and explicit normalized context code. Context codes identify locally reviewed profile scenarios. The application never derives a context from a product name or address.

When rates change, first close the old assignments, then shorten the old rate to the same boundary if needed, create the next rate version, and add the next assignment. Existing end dates can only be shortened prospectively. Existing descriptions and rate names can be edited. Deactivating a classification or rate prevents new selections while retaining its historical links and date-based resolution. Use effective end dates to stop applicability. No tax records, rates or classifications are seeded.

## Product integration and future invoices

In **Product / Item Master**, open a product for editing and choose the **GST & Tax** tab. Choose a company HSN for goods or SAC for services, then select a GST rate context from assignments for that classification. Both dropdowns support search after three trimmed characters. Enter the invoice date and select **Preview applicable rate**. A missing or ambiguous assignment returns an explicit message. The product and tax link are saved together. Legacy HSN, GST percentage and exemption values are read-only in the editor. Price inclusivity, unit and brand data retain their existing behavior. A linked product's underlying goods/service type is fixed. No product-specific rate override is introduced.

The same flow accommodates footwear sold by PAIR, rackets and cricket bats by PCS, sportswear, raw materials, soles, adhesives, packaging and service products without assigning rates based on those names or units.

The dated product snapshot copies classification identity and text, the applied assignment and rate version, treatment, percentages, context and date. The determination contract accepts supplier registration, recipient, explicit place of supply, jurisdiction and taxable value. Missing facts and unsupported cases return distinct outcomes. The service represents CGST/SGST, CGST/UTGST and IGST on a caller-confirmed standard domestic supply. It does not infer jurisdiction from address equality. A future finalizer must calculate and round amounts under approved rules and persist the immutable snapshot. Invoice posting, invoice amounts, export, SEZ, reverse charge, composition, statutory filing and inventory/accounting posting are outside Stage 4H.

References for reviewing actual catalog entries include the [GST portal HSN/SAC search](https://services.gst.gov.in/services/searchhsnsac) and the [CBIC goods and services rate reference](https://cbic-gst.gov.in/hindi/gst-goods-services-rates.html).

## Authorization and migration

Every tax operation uses the existing operation coordinator and authenticated session. The store validates the selected active company and resolves current RBAC grants. HSN/SAC and rate masters have separate view, create, edit and status permissions. Assignment writes require GST Rate assignment rights. Product tax writes require Product Edit and GST Rate assignment rights. Audit records identify the actor, company, record and action. The existing installation identity, startup mutex, authentication, logout and shutdown gates remain in place.

Migration `20261119000900_TaxCatalog` adds four tables with restrictive same-company foreign keys, normalized unique keys, exact integer-scaled percentage storage and interval overlap triggers. It ensures the two module catalog rows exist. No business tax rows are seeded, existing product fields remain intact, and no existing table is rebuilt. The preservation verifier recognizes only the exact expected new permission catalog rows should this migration be explicitly approved later. The migration remains outside the reviewed startup list. It was not applied to any installed database.

## File inventory

Paths are relative to Billing-System.

| Folder | Created |
| --- | --- |
| src/GTS.Billing.Domain | `TaxCatalog.cs` |
| src/GTS.Billing.Application | `TaxCatalog.cs` |
| src/GTS.Billing.Infrastructure | `TaxSchema.cs`, `TaxCatalogStore.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `20261119000900_TaxCatalog.cs`, `20261119000900_TaxCatalog.Designer.cs` |
| src/GTS.Billing.Presentation.Wpf | `TaxEditor.cs`, `TaxView.xaml`, `TaxView.xaml.cs` |
| docs | `stage4h-hsn-sac-gst-master.md` |

| Folder | Modified |
| --- | --- |
| src/GTS.Billing.Application | `PermissionCatalog.cs`, `ProductCatalog.cs`, `SettingsLabels.cs` |
| src/GTS.Billing.Infrastructure | `BillingDbContext.cs`, `DevelopmentDatabaseMaintenance.cs`, `ProductCatalogStore.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `BillingDbContextModelSnapshot.cs` |
| src/GTS.Billing.Presentation.Wpf | `App.xaml.cs`, `CatalogView.xaml`, `CatalogViewModel.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MeasureEditorDialog.cs`, `Navigation.cs` |

EF metadata was generated in an ignored local directory without opening a database connection.

## Verification

From Billing-System, using the existing local package cache and isolated build artifacts:

```powershell
dotnet restore GTS.Billing.slnx
dotnet build GTS.Billing.slnx -c Debug
dotnet build GTS.Billing.slnx -c Release
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
```

Restore succeeded. Final Debug and Release builds each passed with zero warnings and zero errors. The Debug WPF process opened the **GTS Billing — startup stopped** dialog. It reported that the database needs a reviewed update and that no replacement database was created. I dismissed the dialog and observed exit code 1. The signed-in shell and its HSN/SAC and GST Rate menu entries were reviewed in source and could not be reached on the desktop. Migration execution, persisted changes and effective-rate previews remain unverified at runtime.

No automated tests were created, modified, deleted or run. AI-Harness, installed databases, production data, Windows provisioning and Git history were left untouched.
