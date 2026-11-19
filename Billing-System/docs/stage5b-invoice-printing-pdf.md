# Stage 5B Sales Invoice Printing

## Preview and print

Open **Transactions → Sales Invoice**, select a finalized row, and choose **Print preview**. Double-click still opens read-only invoice View. The existing invoice list search and debounce are unchanged. Drafts are excluded from preview and printing. Preview does not issue a number or write the invoice.

The application authorizes and loads the company's immutable Stage 5A snapshot through the invoice service. It converts that snapshot to an invoice print model and then lays it out as a WPF FixedDocument. The same paginator drives the screen and Windows printing. Preview supports previous and next page, page count, zoom, fit width, and fit page. **Select printer and print** opens the Windows dialog. The selected queue must accept the document's physical paper, orientation, imageable margins and copy count. The app checks access again after printer selection and records an authorization audit event. Windows submission is distinct from printer completion.

## Template catalog and history

Choose the company scope under **Administration → Settings → Printing** and edit **Default sales invoice template** with template-view and template-management permissions. Super Admin retains access. The company has one effective Sales Invoice template. The built-in default is Standard A4 Portrait.

| Stable template code | Layout |
| --- | --- |
| A4-PORTRAIT-v1 | Standard A4 portrait, 210 × 297 mm |
| A4-LANDSCAPE-v1 | Detailed A4 landscape, 297 × 210 mm |
| A5-COMPACT-v1 | Compact A5 portrait, 148 × 210 mm |

Templates are versioned in source and use captured margins, optional sections, captions, and display settings. New invoices store the selected template version, financial-year code and series code in the existing snapshot JSON at finalization. Earlier invoices without a version use the captured landscape preference or A4 portrait. Missing historical year/series labels are omitted. Existing finalized snapshots and ledger values are unchanged. Updating the company default affects future snapshots. New layout versions should receive new IDs.

## Content and pagination

The document includes captured seller identity and GST registration, both buyer addresses, applicable contact details, invoice identity, place of supply, customer PO, notes and payment terms when present. Optional captured primary and secondary images are decoded independently, scaled uniformly, and kept within header space. Missing or unreadable logo data is omitted. The renderer uses captured account details, UPI ID and payment instructions. Payment QR codes and uncaptured data such as transport and variants are outside this release.

Product rows show the finalized quantity in its issued unit, such as PAIR, PCS or BOX. No stock-unit conversion is applied to display. The tax section groups by captured HSN/SAC, treatment and component rates and sums stored taxable values and taxes. The final totals and round-off come directly from the snapshot. Optional column settings cannot hide a recorded discount. No current GST rates, master records or tax calculations participate in printing.

Layout measures content in device-independent physical units. Continuation pages repeat the invoice identity and active table headings. The totals block stays together. Long narratives can continue on later pages. Page numbering uses the final paginated count. Missing historical fields are omitted. Printer-specific output and physical fidelity still need manual verification.

## PDF and permissions

There is no approved direct PDF writer. No package was added. When the installed Windows driver offers **Microsoft Print to PDF**, select it from the same print dialog. The Windows driver controls destination selection, filename and overwrite prompts. The application has no direct PDF export, filename management or printer installation workflow. This driver route has not been verified with a generated PDF.

The invoice service requires current company scope, a current authenticated session, and invoice View and financial access for preview. Printing adds Print permission. A queue or driver identified as PDF also requires ExportPdf. Windows printer permissions still govern OS-level routing. The only print audit evidence is authorization, including separate authorization for identified PDF queues. Preview has no user-facing mechanism for sending a print job without that permission check.

No database migration was needed. Defaults use the existing settings override store. The revision adds optional fields only to newly finalized snapshot JSON. It makes no statutory-compliance or printer certification claim.

## Files

Paths are relative to Billing-System.

| Folder | Created |
| --- | --- |
| src/GTS.Billing.Application | `InvoicePrinting.cs` |
| src/GTS.Billing.Printing | `InvoiceDocumentRenderer.cs` |
| src/GTS.Billing.Presentation.Wpf | `InvoicePreviewWindow.xaml`, `InvoicePreviewWindow.xaml.cs` |
| docs | `stage5b-invoice-printing-pdf.md` |

| Folder | Modified |
| --- | --- |
| src/GTS.Billing.Application | `ApplicationSettings.cs`, `PermissionCatalog.cs`, `SalesInvoices.cs`, `SettingsLabels.cs`, `SettingsRegistry.cs` |
| src/GTS.Billing.Infrastructure | `SalesInvoiceStore.cs`, `SettingsStore.cs` |
| src/GTS.Billing.Printing | `InvoicePrintPreferences.cs` |
| src/GTS.Billing.Presentation.Wpf | `SalesInvoiceView.xaml`, `SalesInvoiceView.xaml.cs` |

## Verification

From Billing-System, using the installed SDK, existing packages and isolated build artifacts:

```powershell
dotnet restore GTS.Billing.slnx
dotnet build GTS.Billing.slnx -c Debug
dotnet build GTS.Billing.slnx -c Release
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
```

Restore succeeded. Final Debug and Release builds each passed with zero warnings and errors. The actual WPF window reached **Sign in** and was left open. Navigation and dependency integration were verified in source. The signed-in preview, physical printer output and PDF output remain unverified. No automated tests were created, modified, deleted or run. No installed database migration, AI-Harness change, Windows provisioning change or Git history change was performed.
