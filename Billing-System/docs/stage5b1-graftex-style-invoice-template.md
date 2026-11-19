# Stage 5B.1 — GRAFTEX-style A4 invoice

## Reference and layout

The original GTS preview placed a compact table and stacked totals at the top of an A4 page. Its vertical stack measured only the content it contained, leaving the lower page unused. The supplied photograph shows a continuous ruled form with a large merchandise section and a fixed visual relationship between the goods grid and footer. The supplied mockup informed the preview controls. The photograph governs the printed order, including the transport panel above the amount in words.

`GRAFTEX_STYLE_A4` is the **Classic Full-Page GST Invoice — Footwear & Sports**. It uses a 210 × 297 mm portrait page at 96 WPF device-independent units per inch, captured print margins when present, and a 7 mm fallback on each edge. The outer frame fills the resulting content area. The header uses 19/62/19 proportions for the primary logo, company identity and secondary logo. The party panels split evenly. The goods table uses relative widths of 4/29/8/7/8/8/5/7/10/7/10, with a two-level discount heading and right-aligned amounts. An empty ruled strip extends the grid on short invoices. Mixed units are printed per line and the quantity cell in the total row is left blank when adding those quantities would be misleading.

The footer uses adjacent 43/30/27 panels for effective tax slabs, captured bank details and invoice totals. Tax columns reflect the components present in the finalized invoice, including UTGST or cess where captured. Totals remain on the final page. Page sizing measures wrapped content and repeats the company and goods headers on continuation pages. Content that cannot fit even on an empty printable page produces a layout error. The amount in words uses the persisted total and Indian group names, including paise. Transport data and order details use captured fields. Terms and signature share the bottom row. An authorized configured signature image can be captured for new invoices. A blank signing area is available when an image is absent.

## Company setup and historical output

- Select the template in the invoice preview or set `GRAFTEX_STYLE_A4` in the company invoice-template setting for future invoices. The three earlier templates remain available.
- Configure primary and secondary logos, their visibility, and the default bank account through Company Master and existing settings. Captured logos retain their aspect ratios. Each position uses its own asset. An empty slot stays empty.
- The preview can change the template and copy designation for the current output. The default copy designation uses the saved setting, or appears as Unspecified.
- New drafts can capture optional transport fields and Prepared By using the existing JSON payload. New finalizations can capture IEC, MRP and even an already-configured signature. Historical records retain their captured values. Missing optional fields on older documents appear blank. Placeholder or reference-image business details are never inserted.
- The WPF preview, printer and Windows PDF printer use the same document paginator. PDF export requires a suitable Windows PDF queue and the existing print and PDF-export grants. Choosing an alternate layout also revalidates template permission at print time. No PDF package was added.

## Implementation inventory

- Application — `InvoicePrinting.cs`, `SettingsRegistry.cs`, `SalesInvoices.cs`, `ProductCatalog.cs`, `CompanyPresentation.cs`.
- Infrastructure — `SalesInvoiceStore.cs`, `ProductCatalogStore.cs`, `CompanyPresentationStore.cs`.
- Printing — `InvoiceDocumentRenderer.cs`, `ClassicInvoiceLayout.cs`, `IndianAmountWords.cs`.
- Presentation.Wpf — `InvoicePreviewWindow.xaml`, `InvoicePreviewWindow.xaml.cs`, `SalesInvoiceView.xaml.cs`, `SalesInvoiceEditor.cs`.

## Verification

`dotnet restore GTS.Billing.slnx` succeeded against the existing local package cache. `dotnet build GTS.Billing.slnx -c Debug --no-restore` and the corresponding Release build both succeeded with zero warnings and zero errors. Build artifacts were written under the ignored `.local/stage5b1-artifacts` directory so the running desktop application could keep its own files.

The Debug WPF app was launched using `dotnet run --no-build --configuration Debug --project src/GTS.Billing.Presentation.Wpf/GTS.Billing.Presentation.Wpf.csproj`. It reached development sign-in and the user opened the classic template for finalized demo invoice `FWDEMO-0001`. The supplied screenshot confirms one A4 portrait page with a continuous outer frame, centered heading, bordered invoice number/date row, split receiver/consignee panels, a tall vertically ruled goods grid, and the tax, bank and totals panels side by side. Transport/order details, amount in words, and the split terms/signature row appear in the same order as the photograph. The screenshot shows no visible clipping or overlap on this invoice.

The printed values shown in preview are ₹2,580.00 taxable, ₹64.50 CGST plus ₹64.50 SGST, and ₹2,709.00 grand total. The sample shows blank logo and signature areas and an unspecified copy designation. The photograph has both logos and a populated designation, and uses an IGST column for its own transaction. The preview preserves the demo invoice's values. Dual-logo display with populated assets, multi-page behavior, physical printing and PDF output remain unverified.

No automated tests were created or run. The implementation requires no schema migration.
