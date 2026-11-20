# Sales Invoice Configuration

Select an active company and open **Settings → Sales Invoice Configuration → Configure invoice fields**. A user with invoice-settings edit permission can edit a selected row, drag it within its section to change its order, preview the working layout, and save. Double-click opens a read-only field definition. Search and section filters help locate fields.

**Reset to defaults**, **Import JSON**, and **Restore version** prepare changes for review. **Save configuration** applies them to the selected company. To duplicate a layout across companies, export its JSON, switch to the destination company, import it, and save there. Review layout files before sharing because company-specific labels, defaults, and help text are included.

The existing Settings service stores rules under stable keys at company scope. It checks the selected company, edit permission, expected version, and field constraints. A save records the actor and previous and new values and captures a restorable layout version. Restoring a version creates a new audit entry. The version picker displays the latest 50 saved layouts.

Create and Edit share the same field configuration. Defaults apply when creating an invoice. Existing draft values remain intact when visibility changes. Required optional text fields are checked by the invoice service on preview, save, and finalization. Core invoice data and calculated totals remain protected. An authorized user can still enter a missing MRP inline. Customer PO remains visible because Customer Master can require it.

Entry, preview, and print/PDF visibility can be set separately where supported. The output controls apply to product code, SKU, brand, MRP, invoice reference, payment terms, and notes where the selected template already renders those values. Printed columns can retain their width with empty cells so the template geometry is preserved. Finalized invoices use the rules recorded in the seller presentation snapshot. Older finalized invoices use the built-in layout behavior.

The payment, transport, and notes section can be hidden as a group. Required fields in that group prevent hiding it. Hidden GST controls are available in a separate **Review GST details** dialog, while their entry section stays hidden. Unit, line total and grand total can be hidden in entry. The selected unit or product sales unit and calculated amounts are still used by the service.

GST calculations use reviewed transaction data. For a company with a consistent, reviewed domestic supply policy, **Settings → Sales Defaults** supports a pre-reviewed GST place and jurisdiction. Set both only when the policy applies to invoices that omit all GST review inputs. The normal registration and tax-profile checks still apply. With no complete reviewed inputs, Preview shows gross and line discounts and marks GST and the invoice total pending. Draft saving remains available. Finalization requires a complete calculation.

Invoice-wide discounts, additional charges, and arbitrary new fields have no configuration support in this version.

## Draft finalization

Draft Preview permits provisional totals. Finalization review reads the persisted draft and applies the same required fields, current company settings, reference checks and tax rules as finalization. A draft with incomplete GST review receives a field-level explanation. The editor layout does not alter the required business facts.

Previously the finalization button reused Draft Preview. A provisional response had no finalization confirmation, so the UI returned early. The following list refresh cleared the message. Finalization now uses its own review operation and keeps failure details in a dismissible error banner. Success shows the issued invoice number. The final write validates the reviewed snapshot again and commits the document number, audit event and invoice together.

The implementation uses the existing SQLite schema and the existing local Settings interface.
