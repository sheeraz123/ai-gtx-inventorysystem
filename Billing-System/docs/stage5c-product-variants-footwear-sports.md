# Stage 5C — Product variants for footwear and sports goods

The baseline contained a variant-description contract and existing Product, Brand, Unit, GST and Sales Invoice services. It had no persisted variants, size or color master, or invoice variant selection.

## Product Master workflow

Select a parent in Product Master and open **Variants**. The dialog contains **Variants**, **Size Master**, **Color Master** and **Other Attributes**. These masters are company-wide and use a single option catalog. Size codes carry a controlled size system (UK, US, EU, India/custom, apparel, numeric or other). Color identity uses its code. A color can also have a display hex value. Additional axes, such as width or grip size, use an immutable normalized attribute code and controlled option records. Each variant can use one option per axis and one size system. The system never seeds sample options or infers cross-system size conversions.

The variant list supports size, color, status and debounced text filters within the selected parent. Variant code and SKU searches support partial text. Barcode search uses the whole stored value and preserves leading zeros. Double-click opens a read-only record. Edit and status changes are separate actions. The footwear matrix shows active sizes and colors, including occupied or inactive cells. Choose a cell to add or edit that combination explicitly. Variants with additional attributes appear in the variant list, since multiple detailed combinations can share a size and color. Suggestions use the parent code and selected option codes in deterministic attribute order. Use **Suggest SKU** to populate an empty SKU field. The suggestion requires saving through the usual uniqueness validation.

## Persistence and security

`VariantOptions`, `ProductVariants` and `ProductVariantOptions` support a parent product with multiple stable variant IDs. Composite foreign keys enforce company ownership. Unique indexes protect variant code and attribute combinations per parent and SKU/barcode per company when supplied. The Product and Variant services check parent/variant SKU and barcode collisions in both directions. Option and variant changes retain timestamps and user attribution and write to the existing audit log. Existing company and product visibility checks govern entry, and separate role grants control variant create, edit, status and price updates and size/color/other option management. Existing roles receive no automatic grants.

Variant MRP, selling rate and purchase rate are optional. Blank overrides inherit the corresponding parent value. Invoice entry suggests the variant or parent sales rate for the default commercial unit. Alternate units keep their existing explicit price-entry behavior. Units and conversions belong to the parent and are reused.

## Sales Invoice

After choosing a product with active variants, select one for the draft line. Older drafts retain their stored selections when opened and can be reviewed if the product now requires a variant. The nullable variant ID is serialized in the existing draft document format. The invoice service revalidates the company, parent product and active variant. Finalized line snapshots capture variant ID, code, SKU, barcode and controlled attribute labels alongside frozen units, rates, tax and totals. Existing finalized invoices and the permanent document ledger need no backfill. All four renderer layouts and company template settings retain their existing implementations. The captured effective SKU can appear in an existing SKU text position. Dedicated printed size/color lines are deferred to preserve the approved page geometry.

## Migration and verification

Migration `20261120000100_ProductVariants` adds three tables, composite protection for variant parents, and four permission modules. The existing backup-and-update screen requires separate approval before applying it. Debug and Release built with zero warnings and errors. The Debug app launched and displayed the database update prompt listing this migration. No migration was applied. Runtime validation of the variant masters, matrix, invoice selection and visual layout remains pending database update approval. Automated tests were neither created nor run. Stage 5D was not started.

## Source inventory

- Domain — `ProductVariants.cs`.
- Application — `ProductVariants.cs`, `ProductCatalog.cs`, `SalesInvoices.cs`, `PermissionCatalog.cs`.
- Infrastructure — `ProductVariantSchema.cs`, `ProductVariantStore.cs`, `BillingDbContext.cs`, `ProductCatalogStore.cs`, `SalesInvoiceStore.cs`, `DevelopmentDatabaseMaintenance.cs`, `Migrations/20261120000100_ProductVariants.cs`, `Migrations/BillingDbContextModelSnapshot.cs`.
- Presentation.Wpf — `ProductVariantsDialog.cs`, `CatalogView.xaml`, `CatalogView.xaml.cs`, `SalesInvoiceEditor.cs`, `SalesInvoiceReadView.cs`, `MainWindow.xaml.cs`, `App.xaml.cs`.
