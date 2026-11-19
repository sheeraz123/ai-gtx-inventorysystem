# B1 Company domain and validation specification

Status: proposed product behavior, with Dxx decisions defined in [the overview](README.md). Field sizes are proposed storage/API bounds, not statutory requirements. No tax calculations or regulatory verification service is implemented or authorized.

## Boundary and relationships

A Company represents the initial accounting and data-security boundary. Company ID is not a tax ID, branch ID, user ID or invoice number. A Company owns its addresses, contacts, tax registrations, financial years, access roles and future documents. A user can belong to several companies with different permissions. A branch/warehouse will remain a child of exactly one Company; it never becomes a shortcut around Company authorization.

Separate **legal identity**, **GST registration** and **industry**. The GST portal describes registrations by state and GSTINs associated with a PAN; the model therefore allows multiple registrations instead of embedding one permanent GSTIN into the Company row. This modeling decision is not a claim about which registrations a particular business must obtain. [GST registration manual](https://tutorial.gst.gov.in/userguide/registration/Apply_for_Registration_Normal_Taxpayer.htm).

If the business requires one accounting company per GST registration, record that as D01 and adjust uniqueness/ownership before implementation. Do not silently split a customer's company or combine companies because their PANs match.

## Company fields

| Field | Type / bound | Required and behavior |
| --- | --- | --- |
| CompanyId | UUID, server generated | Always; immutable. Random/opaque IDs do not confer authorization |
| Code / NormalizedCode | string 20 | Required; proposed ASCII `[A-Z0-9][A-Z0-9_-]{1,19}` after trim/uppercase. Unique deployment-wide while only one organization namespace exists; never reuse inactive codes. Draft edit only; later rename is a controlled operation |
| LegalName | Unicode string 200 | Required in draft and active; trim outer whitespace, preserve meaningful Unicode/case/punctuation |
| DisplayName | Unicode string 200 | Optional stored value; display fallback to LegalName without overwriting it |
| BusinessTypeCode | reference code 40 | Controlled proposed constitution/business-type master; definition and mandatory status require D11 |
| Industry classifications | collection of reference codes | Optional multi-industry tags, separate from legal business constitution; no industry-specific rules assumed |
| RegisteredCountryCode | approved country reference | Required to activate; consistent with primary registered address and selected supported jurisdiction profile |
| BaseCurrencyCode | approved currency reference | Required to activate; proposed uppercase three-letter reference code, not arbitrary free text |
| Status | Draft / Active / Inactive | Server-controlled state transition, not arbitrary body assignment |
| Version | positive integer concurrency token | Server-maintained; changes on successful aggregate mutations |
| CreatedAtUtc / CreatedBy | UTC instant / actor reference | Server-set; immutable |
| UpdatedAtUtc / UpdatedBy | UTC instant / actor reference | Server-set on change; distinct from effective business dates |

Never globally title-case legal names or strip punctuation to create identity matches. Proposed code uniqueness is a product decision (D05), not a legal uniqueness rule. Introduce OrganizationId first if separate unrelated customer organizations need independent code namespaces; CompanyId alone is not a complete SaaS organization model.

## Tax profile and registrations

| Entity / field | Proposal |
| --- | --- |
| CompanyTaxProfile | One per company/jurisdiction profile; declared Registered, Unregistered or Unknown state; Unknown allowed in draft only when registration behavior depends on it |
| PAN | Optional in draft; ordinary Indian GST registration profile requires it on activation. Uppercase ASCII shape `[A-Z]{5}[0-9]{4}[A-Z]`, length 10; format is not proof of issuance, ownership or status |
| IEC | Earlier requirement retained; optional value up to 40 characters pending approved validation/migration policy. Do not invent a current IEC format or make it activation-mandatory |
| CompanyTaxRegistration | Company-scoped ID, kind/jurisdiction, identifier, registration state, principal address, effective dates and verification metadata |
| Ordinary Indian GSTIN | Proposed 15-character uppercase ASCII identifier profile. Exact checksum, PAN/state consistency, supported registration kinds and authoritative fixtures must be approved under D06 before activation validation is released |
| Registration status | Draft / Active / Inactive, with effective start/end dates; portal verification state separate: NotChecked / Verified / CheckFailed / NeedsReview |
| Verification metadata | Method/source, checked date and reviewer; never imply a local regex is portal verification |

The Income Tax Department documents the PAN letter/digit format; local validation must not infer entity ownership or verify a taxpayer solely from a matching pattern. [PAN format reference](https://www.incometaxindia.gov.in/documents/20117/43154/18__PAN__English.pdf/0f590273-a856-dee2-9053-76f8c0aa4127?t=1762672726879&version=1.0).

Validation rules TAX-01…06:

1. Use explicit jurisdiction/registration-kind validators. Ordinary PAN-based GSTIN checks must not reject or misclassify unsupported UIN, non-resident or other special registration cases as ordinary registrations; unsupported types require review.
2. If an approved ordinary-GSTIN profile provides embedded PAN/state rules, verify them against the declared PAN and that registration's state. A company's legal or mailing address can be in a different state from another registration; never compare every registration to one universal address.
3. Within one company, registration identifiers are unique by kind/jurisdiction/normalized value. Cross-company matching is a privileged conflict-review policy under D06, not a public lookup that reveals other companies. No automatic global unique PAN constraint.
4. Once a registration is referenced by issued documents, changing its identifier or effective period requires controlled supersession/new registration history, not silent overwrite. Old documents retain immutable identity/address/tax snapshots.
5. No automated portal calls, taxpayer-data scraping, API credentials or paid verification subscription is assumed. Business review/authorized verification must be distinguishable from unverified data.
6. Scope B1 to approved jurisdiction profiles. Do not impose Indian PAN/GST rules on all foreign entities or claim complete tax compliance from the data model.

## Addresses and contacts

CompanyAddress: CompanyId, AddressId, type (Registered/Billing/Correspondence), lines 1–3 (200 each), locality/city (100), district (100 optional), state/region reference or approved free text, country reference, postal code (20), IsPrimaryForType, effective dates and audit/concurrency fields.

Require line 1, city/locality and country for a usable address. State/postal rules are country-specific, using approved reference data; no global six-digit postal assumption. Exactly one current primary address per required type at activation; a tax registration's principal address must be owned by the same Company and satisfy its jurisdiction profile. Multiple addresses may share content. Do not delete an address referenced by historical documents; supersede it. Registered/billing addresses can differ.

CompanyContact: CompanyId, ContactId, person/display name (120), designation (80 optional), email (254 optional), phone in normalized international form where supported (32) plus separate extension (10), preferred channel and IsPrimary. Require at least one email or phone for a usable contact. Proposed activation policy requires one primary company contact, subject to D05. Multiple contacts can use shared inboxes/numbers; do not make them login identities or globally unique. Trim input, apply approved country-aware phone parsing, validate reasonable email syntax without demanding live delivery, and encode all displayed values as text.

## Financial years, currency and status

CompanyFinancialYear: CompanyId, FinancialYearId, label/code, StartDate and EndDate as date-only, state Draft/Open/Closed, and concurrency/audit fields. Require StartDate ≤ EndDate and no overlapping inclusive date ranges within the same company, enforced transactionally. Separate companies may have different calendars. Do not hard-code April–March or assume only one year may remain Open; D07 decides defaults and closing rules. Dates on financial documents must belong to the selected year and permitted posting state when invoicing is implemented.

The company may nominate a default year, but each operation carries an explicit FinancialYearId and validates ownership/date/state. Changing a user selection does not change company defaults. Prevent boundary changes after dependent issued transactions; corrections need a reviewed migration/reconciliation policy. Base currency changes are draft/pre-transaction operations; after issued documents, reject ordinary edits and require an approved conversion/migration procedure. Do not add exchange-rate or multi-currency accounting behavior to B1.

Lifecycle CM-01…05:

- Create as Draft; it cannot transact. Validate mandatory identity fields immediately.
- Activate only after required country/currency/address/contact fields, supported tax profile, at least one configured/default permitted financial year and an active Company Administrator are valid. The exact activation checklist requires D05/D07 approval.
- Inactivate with explicit permission and reason. Block new transactional work while preserving authorized archival read access. Revalidate status in the server transaction; UI disabling is insufficient.
- Reactivate only through the same authorization/checklist checks. No physical company deletion or code reuse is proposed for B1.
- Concurrent edits use expected Version/ETag. Unauthorized or invalid updates cannot partially update child data or omit the corresponding business audit event.

## Branding and future structure

Earlier Phase 1 requirements for up to two optional logos remain in scope for planning (D12 decides delivery order). Model two explicit asset slots, not unbounded uploads. Proposed initial policy: PNG/JPEG only, 2 MiB per file, bounded decoded dimensions/pixels, decode/re-encode and metadata stripping; no SVG/HTML or executable content. These limits are configurable product proposals. Assets use company-scoped IDs, private storage and authorized retrieval, never caller-supplied paths or permanent public URLs.

Reserve a future Company → Branch → Warehouse relationship with composite company foreign keys. GST registration is not automatically identical to a branch or warehouse; future branch-to-registration assignment needs explicit ownership/effective-date rules. Branch/warehouse CRUD, stock movements and accounting remain outside B1 implementation.
