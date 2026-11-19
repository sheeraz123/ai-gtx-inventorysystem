# Stage 5A Sales Invoice Entry

## Workflow

Open **Transactions → Sales Invoice** after choosing an active company. Filter the latest 500 matching invoices by year, customer, date range, status or series. Search starts after three effective characters with the existing approximately 300 ms debounce, and clearing the input restores the filtered list. Double-click opens read-only View. Use **Edit draft** for a separate edit action.

Choose the invoice date, an open financial year covering the date, an active supplier GST registration and an active Tax Invoice series scoped to that year and registration. Select a customer and eligible billing and shipping addresses. A blank address selection uses its matching configured default. Add product lines, select the default selling unit or an active configured conversion, and enter price and discount. A configured alternate unit requires an explicit unit price. Products expose code, name, SKU/barcode, category and brand in the selector. PAIR, PCS, TUBE and PACK are product-specific units and conversion factors. No stock movements, variant engine or manufacturing transactions are created.

Use **Save Draft** for a persistent ID and optimistic version. A saved draft can have incomplete setup or unresolved tax. Preview recalculates after changes and displays totals or the reason calculation could not complete. Save, Preview and View allocate no number. Select a saved draft and choose **Finalize draft**. Review the company, date, customer, series, financial year, number of lines and totals, then confirm explicitly.

## Calculation policy

Prices and totals use decimal arithmetic and INR. The approved policy applies either a percentage or fixed amount discount to the line quantity times unit price before adding exclusive tax or extracting included tax. A discount must stay within the line amount. Post-supply discounts and invoice-wide discounts remain deferred. [CBIC valuation reference](https://cbic-gst.gov.in/hindi/sectoral-faq.html)

The operator confirms a supported standard domestic sale, place of supply and reviewed GST jurisdiction. The app checks the selected date, supplier GST registration, customer eligibility and domestic addresses, product HSN/SAC, effective tax assignment and selected conversion. The Stage 4H determination determines the applicable CGST and SGST, CGST and UTGST, or IGST components plus applicable percentage cess. It does not derive place of supply from an address. Unsupported or unresolved determination blocks issuance. Nil-rated, exempt and non-GST retain their distinct treatments in preview. This Tax Invoice workflow only issues standard taxable invoices. Zero-rated, special schemes, other document types and statutory reporting remain deferred. [CBIC tax invoice rules](https://cbic-gst.gov.in/gst-invoice-rules.html)

The application rounds each extended line price, percentage discount, taxable value and tax component to two decimal places, using midpoint rounding away from zero. For inclusive prices, the discounted gross divided by one plus the applied percentage ratio gives the taxable base. The difference between the rounded base plus rounded components and the approved inclusive net appears as an explicit round-off. Invoice totals sum the stored line components. No statutory rounding or compliance guarantee is implied.

## Persistence and finalization

The additive migration `20261119001100_SalesInvoiceEntry` adds `SalesInvoices`, associated indexes, triggers and the required permission module catalog entry if missing. The row stores company-scoped references, search/header fields, exact decimal totals, audit metadata and the editable draft and line inputs as JSON. The existing `InvoiceSnapshots` table stores immutable finalized header and line snapshots, alongside the existing permanent number ledger.

Within the existing authenticated, company-scoped and admission-controlled application operation, the store revalidates the saved version and live authorization. It recalculates the snapshots and compares a confirmation fingerprint covering the actual calculated inputs. The existing Stage 4I finalization contract is implemented through the invoice service. The store uses the same series formatter, counter and ledger. It adds the counter change, allocated ledger record, frozen snapshot and finalized invoice association within the same SQLite transaction. Allocation failures roll back the transaction. Retrying a finalized draft returns its existing number through the persisted association. Database triggers require a matching issued ledger and snapshot and protect finalized rows from updates and deletion.

Finalized details preserve pricing, discount, tax base, percentages and amounts, product and unit identity, factor and base quantity, and date-resolved classification and rate. Seller registration, address, contacts, logos, applicable bank details, terms and display options are frozen along with buyer identity, billing/shipping details and contact information. View mode reads the stored snapshot. Invoice audit history shows recent persisted events. Rolled-back operational failures produce sanitized diagnostics.

Existing permissions cover View, ViewFinancials, Create, EditDraft and Finalize. View and ViewFinancials are required for this financial screen. Record and reference queries use the selected company revalidated inside the transaction. Auth, mutex, database identity and shutdown behavior continue to use existing infrastructure. Stage 5A does not add payment, inventory, general ledger, invoice cancellation, amendment, credit notes, returns, printing or PDF output.

## Files

Paths are relative to Billing-System.

| Folder | Created |
| --- | --- |
| src/GTS.Billing.Domain | `SalesInvoice.cs` |
| src/GTS.Billing.Application | `SalesInvoices.cs` |
| src/GTS.Billing.Infrastructure | `SalesInvoiceSchema.cs`, `SalesInvoiceStore.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `20261119001100_SalesInvoiceEntry.cs`, `20261119001100_SalesInvoiceEntry.Designer.cs` |
| src/GTS.Billing.Presentation.Wpf | `SalesInvoiceView.xaml`, `SalesInvoiceView.xaml.cs`, `SalesInvoiceEditor.cs` |
| docs | `stage5a-sales-invoice.md` |

| Folder | Modified |
| --- | --- |
| src/GTS.Billing.Application | `DocumentNumbering.cs`, `PermissionCatalog.cs`, `ProductCatalog.cs`, `SettingsLabels.cs` |
| src/GTS.Billing.Infrastructure | `BillingDbContext.cs`, `CompanyPresentationStore.cs`, `CustomerStore.cs`, `DevelopmentDatabaseMaintenance.cs`, `ProductCatalogStore.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `BillingDbContextModelSnapshot.cs` |
| src/GTS.Billing.Presentation.Wpf | `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MeasureEditorDialog.cs`, `Navigation.cs` |

The new migration remains outside the installed database's reviewed upgrade list. No historical migrations were changed. No automated tests were created, modified, deleted or run.

## Verification

Commands ran from Billing-System using existing cached packages and isolated build outputs.

```powershell
dotnet restore GTS.Billing.slnx
dotnet build GTS.Billing.slnx -c Debug
dotnet build GTS.Billing.slnx -c Release
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
```

Restore succeeded. Both builds passed with zero warnings and errors. The Windows process displayed the reviewed database update message and stopped before login. No replacement database was created. The invoice navigation and dependency registration were reviewed in source. WPF invoice entry, migration execution, SQLite finalization, and visual verification remain pending. AI-Harness, installed databases, Windows provisioning and Git history were left untouched.
