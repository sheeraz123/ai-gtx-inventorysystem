# Stage 5B.3 — Sales Invoice layouts and desktop UI

## Invoice formats

| Code | Name | Paper |
| --- | --- | --- |
| `GRAFTEX_CLASSIC_A4` | Classic Full Page — Dual Logo | A4 portrait |
| `MODERN_GST_A4` | Modern Professional GST | A4 portrait |
| `WHOLESALE_DETAILED_A4_LANDSCAPE` | Wholesale Detailed | A4 landscape |
| `COMPACT_RETAIL_A5` | Compact Retail | A5 portrait |

The GRAFTEX layout retains the ruled form, fixed page geometry, and two independent logo slots from Stage 5B.1. It uses the primary and secondary images captured for the company, in that order, with uniform scaling. Explicitly configured visibility choices can hide optional content. The renderer keeps the supplier GSTIN on a tax invoice. The modern layout uses a contrasted header treatment and side-by-side address panels. The wholesale format uses the larger landscape width for product code, SKU, brand, MRP and monetary columns. Compact Retail uses a condensed A5 grid and includes the captured GST breakdown. Uncaptured product variant data and logos are left blank.

Settings uses the existing company scope. Choose **Settings → Printing → Current company → Default sales invoice template**, open the setting, choose **Edit**, then select a template and **Save as company default**. The setting shows the company, paper size, orientation and short description. Preview uses an existing finalized invoice in the selected company and requires template and sales-invoice read access. It does not persist the selection or allow printing from Settings. Logo and bank configuration reuse Company Master. The registry controls optional IEC, address, phone, email, transport, terms, signatures and footer notes. Copy designation and business description also use existing settings.

For new finalizations, a supported company preference is captured in the invoice snapshot. Missing or invalid settings fall back to Modern Professional GST. Existing company settings with earlier standard, landscape and A5 codes map to their corresponding current choice for new invoices. Finalized documents with the earlier codes retain the historical renderers. Earlier GRAFTEX identifiers resolve to the same GRAFTEX renderer. Legacy snapshots that lack a code retain the existing orientation-based fallback. A one-off preview selection does not change the company preference or the saved invoice. Preview, print and the Windows PDF printer use the same paginator and frozen invoice data.

## Invoice entry and View

- The wide entry dialog has a horizontal company, registration, date, year and series header, side-by-side billing and shipping panels, and horizontally scrollable product rows with product-specific unit selectors. Product lookup filters the current company's loaded catalog by its searchable labels, including code, name, SKU, barcode, and brand. It begins after three non-whitespace characters and debounces through the existing settings value. No cross-company product search or mutation was added.
- The visible summary comes from the existing invoice calculation service. The collapsed details section holds payment, order, transport and notes. The entry action bar supports previewing totals, saving the draft, and saving then reviewing finalization. Finalization reloads the persisted draft, calculates it server-side, asks for confirmation, and uses the existing version, permission, numbering and snapshot workflow. Closing at the confirmation leaves the saved draft.
- View opens through a separate read-only invoice document dialog and presents status, date, supplier and customer address panels, a read-only product grid, totals, payment/transport details and audit history. Draft editing is an explicit action. Finalized print preview provides print/PDF actions under the existing permissions. Double-click on a populated row opens View through the existing grid behavior.

## Keyboard and authentication

Open dropdowns consume Escape before it reaches the surrounding window. Owned WPF windows use their close lifecycle, so invoice editors retain discard confirmation and saving/preview guards. In the shell Escape routes through existing navigation to the dashboard and its page validation. On Login, Escape asks before using normal application shutdown. Eligible authentication subflows can return through their existing cancel action.

Login uses a centered themed card, labeled keyboard-accessible inputs, visible busy state, and an opt-in password reveal. The masked field is the default and the temporary reveal input is cleared on submission or page changes. Authentication, Argon2id, throttling, recovery codes, password change and session security still use the existing services. No schema or migration was needed.

## Files changed in this implementation pass

- Application — `InvoicePrinting.cs`, `SalesInvoices.cs`, `SettingsRegistry.cs`.
- Infrastructure — `SalesInvoiceStore.cs`.
- Printing — `ClassicInvoiceLayout.cs`, `InvoiceDocumentRenderer.cs`.
- Presentation.Wpf — `App.xaml.cs`, `AuthenticationView.xaml`, `AuthenticationView.xaml.cs`, `MainWindow.xaml.cs`, `MasterGridInteraction.cs`, `MeasureEditorDialog.cs`, `SalesInvoiceEditor.cs`, `SalesInvoiceReadView.cs` (new), `SalesInvoiceView.xaml.cs`, `SearchableComboBox.cs`, `SettingsView.xaml`, `SettingsViewModel.cs`.
- Documentation — this file.

## Verification

`dotnet restore GTS.Billing.slnx` succeeded using the existing package cache. `dotnet build GTS.Billing.slnx -c Debug --no-restore` and the Release equivalent both succeeded with zero warnings and zero errors.

Launched `dotnet run --no-build --configuration Debug --project src/GTS.Billing.Presentation.Wpf/GTS.Billing.Presentation.Wpf.csproj` with the isolated Stage 5B.3 build. The actual WPF window and sign-in accessibility controls were observed, including username, password visibility and sign-in. Desktop screenshot capture timed out. Stage 5B.1 verified the one-page GRAFTEX structure using the user's actual demo invoice screenshot. A fresh Stage 5B.3 comparison, populated logos, all four rendered layouts, cross-company default switching, signed-in entry/View flows, Escape interactions, physical printing and PDF remain pending manual verification.

No automated tests were created, modified, deleted or run. No database migration was applied.
