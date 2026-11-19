# B1 API and UI specifications

Status: proposed contracts, not implemented endpoints or screens. The identity/deployment/UI decisions remain pending. All `/api/v1/companies/{companyId}` operations require explicit Company access; selecting a company in a browser is not authorization.

## API conventions

- Authenticated server session with approved identity provider; for the proposed same-origin web host, use secure HttpOnly cookies, TLS and anti-forgery protection on state-changing requests. Do not put bearer tokens in local storage as an unreviewed default.
- CompanyId is an immutable route value. Reject body/header/query tenant overrides, not silently prefer one. All child IDs are resolved by `(CompanyId, Id)`. No unscoped GetById endpoint.
- Responses are explicit DTOs. Never serialize EF navigation graphs, memberships of other companies, private storage keys, raw audit payloads or full tax IDs by default.
- List endpoints use stable sorting and cursor/page limits (proposed default 25, maximum 100). Search is constrained to the authorized company list/data set.
- GET entity responses include opaque ETag from Version; mutations require If-Match. Missing precondition → 428; stale version → 412, no partial changes. Child edits through the Company screen use the Company's aggregate ETag.
- Create operations use Idempotency-Key, bound to authenticated actor/company/operation and canonical request hash. Replay returns the original resource; different payload/key reuse → 409. Do not trust a client-provided CompanyId or actor.
- Validation → 422 with field/rule codes; malformed JSON/type errors → 400; unauthenticated → 401; visible resource but insufficient action permission → 403; nonexistent or inaccessible Company/child → the same 404 response. Conflict/invariant failure → 409. Do not disclose cross-company tax/code ownership in errors.
- Successful mutations and business audit append commit together. HTTP success is not returned before durable commit. Unexpected failures use sanitized Problem Details with correlation ID, never SQL text, stack traces or connection strings.

## Endpoints

| Method and route | Purpose / permission | Contract and notable behavior |
| --- | --- | --- |
| GET `/api/v1/me/companies` | Current user's authorized company summaries | Membership-filtered CompanyId/code/display/status, no PAN/contact list; paging and archival access rules |
| POST `/api/v1/me/company-context` | Select an accessible Company and optional financial year | Body `{companyId, financialYearId?}`; verifies membership, year ownership and allowed status; selection is a UX preference only |
| POST `/api/v1/companies` | Create Draft; `Platform.Company.Create` | Code/legal name, proposed country/currency, explicit initial administrator and permitted draft fields; 201 Location + ETag; idempotent provisioning transaction |
| GET `/api/v1/companies/{companyId}` | Company.Read | Profile DTO, masked tax summary, activation readiness and Version/ETag |
| PUT `/api/v1/companies/{companyId}` | Company.EditGeneral; stronger policy for legal identity | Explicit general-profile fields only, no memberships/status/counters. If-Match; reject unsupported/cross-scope fields |
| POST `/api/v1/companies/{companyId}/activation` | Company.ChangeStatus | Reason + expected ETag; activation checklist evaluated server-side; 409 with safe missing-requirement codes |
| POST `/api/v1/companies/{companyId}/inactivation` | Company.ChangeStatus | Required reason; version check and atomic status/audit transition |
| GET/POST `/api/v1/companies/{companyId}/addresses` | Company.Read / Company.EditGeneral | Company-owned addresses; POST returns AddressId; no supplied CompanyId in body |
| PUT `/api/v1/companies/{companyId}/addresses/{addressId}` | Company.EditGeneral | Aggregate If-Match; owned address only; referenced historical address supersession rules |
| GET/POST `/api/v1/companies/{companyId}/contacts` | Company.Contacts.Read / Company.Contacts.Manage | Minimal fields appropriate to the permission; many contacts allowed |
| PUT `/api/v1/companies/{companyId}/contacts/{contactId}` | Company.Contacts.Manage | Version check, field whitelist, primary-contact transaction |
| GET/PUT `/api/v1/companies/{companyId}/tax-profile` | Company.Tax.Read / Company.Tax.Manage | Full PAN/IEC only for authorized detail access; general company response remains masked |
| GET/POST `/api/v1/companies/{companyId}/tax-registrations` | Company.Tax.Read / Company.Tax.Manage | Explicit kind, jurisdiction, principal address, identifier and dates; no automatic portal verification |
| PUT `/api/v1/companies/{companyId}/tax-registrations/{registrationId}` | Company.Tax.Manage | Version/effective-date/history checks; identifier mutation after use is rejected |
| GET/POST `/api/v1/companies/{companyId}/financial-years` | FinancialYear.Read / FinancialYear.Manage | Explicit date-only range, code and intended state; concurrency-safe overlap validation |
| POST `/api/v1/companies/{companyId}/financial-years/{yearId}/closure` | FinancialYear.Close | Future transactional preconditions require D07; do not expose an unrestricted Boolean state setter |
| GET `/api/v1/companies/{companyId}/memberships` | Company.Users.Manage | Scoped access list; limited identity projection, no credentials or global role assignments |
| POST `/api/v1/companies/{companyId}/invitations` | Company.Users.Manage | Selected company roles, verified invitation lifecycle/expiry; never grant access just because an email matches |
| PUT `/api/v1/companies/{companyId}/memberships/{membershipId}/roles` | Company.Users.Manage | Company role IDs only; cannot grant permissions outside caller's delegated ceiling or remove the last active Company Administrator |
| POST `/api/v1/companies/{companyId}/memberships/{membershipId}/revocation` | Company.Users.Manage | Immediate membership-version invalidation at the next authorization boundary; no promise of canceling every already-authorized transaction |
| GET `/api/v1/companies/{companyId}/audit-events` | Company.Audit.Read | Paged/redacted event projection; full sensitive history requires a separately approved capability |
| PUT/GET `/api/v1/companies/{companyId}/logos/{slot}` | Company.Branding.Manage / Company.Read | Optional later increment; slot 1 or 2, content validation and private retrieval; no source filesystem path accepted |

Company data hard-delete, arbitrary database selection, direct number-counter update, unscoped admin search, cross-company exports and impersonation endpoints are absent. Field retirement/end-date and invitation acceptance details need explicit follow-on contracts before their implementation milestone. No invoice, branch or warehouse API is implemented by B1.

## Request/response examples (illustrative, not runnable code)

Draft create request shape:

```json
{
  "code": "ACME_IN",
  "legalName": "Example Trading Entity",
  "displayName": "Example Trading",
  "registeredCountryCode": "IN",
  "baseCurrencyCode": "INR",
  "initialAdministratorUserId": "<existing-authorized-user-id>"
}
```

These are placeholders, not real company/taxpayer data. The server generates CompanyId, Status=Draft, audit fields and ETag. Tax registrations, contacts, addresses and years can then be populated through scoped operations before activation. The create DTO cannot assign global Super Admin status.

Selection returns only `{companyId, financialYearId, displayName, allowedCapabilities, contextVersion}` as an authorized UI projection. Subsequent requests still supply their immutable route company and receive fresh authorization; a selection response/capability list is not a reusable security credential. UI language “current company” does not change ownership of existing drafts or database sessions.

## Company Master screens

| Screen | Proposed behavior |
| --- | --- |
| Company selector | Shows only memberships visible to the signed-in user; clear active/inactive labels, search and explicit selection. A new browser tab can select another company independently |
| My companies | Code/display/legal name/status summary; create control only for platform creation permission. No leaked company counts from inaccessible tenants |
| Create Draft wizard | Identity → jurisdiction/currency → initial administrator → review. Show unresolved activation items; no default real GSTIN/PAN or invented business type |
| Company detail | Persistent company code/name banner; tabs: General, Addresses, Contacts, Tax Registrations, Financial Years, Users/Permissions, Audit and optional Branding |
| General editor | Exact legal name, display name, business-type/industry distinction, country/currency, read-only ID/status/audit metadata. Unsupported changes clearly explain the reviewed procedure needed |
| Address/contact editor | Multiple entries, explicit primary selection, validation summaries and history/supersession for referenced data |
| Tax editor | Registration collection with state/jurisdiction/effective dates, masked identifiers by default, privileged reveal/edit and explicit verification status. Do not call all unverified entries valid |
| Financial-year editor | Date ranges/state/default selection; overlap errors and locked historical boundaries; no hard-coded fiscal calendar |
| Access management | Membership and delegated company roles; revocation confirmation; last-administrator protection; no platform-role editing in this screen |
| Activation/inactivation review | Display checklist, scope/company name, required reason and expected version; confirmation commits one explicit transition |
| Audit viewer | Date/action/actor filters, paging, redacted changes and correlation IDs; no secret/request-body dumps |

Company switching: show an unsaved-changes prompt; save/discard remains in the original CompanyId. Never rewrite an open form's tenant ID to the newly selected company. Cancel or ignore stale in-flight UI responses, clear company-specific caches/forms and reload authorized context. Keep visible company/year banners on all future transactional screens. Browser multi-tab behavior must not rely on one mutable global session “current company.”

Accessibility/usability: keyboard-complete forms, labels and focus/error summaries, text status indicators, localized date/currency display without changing stored values, double-submit prevention, and clear 412 conflict recovery. Two users editing simultaneously must see a conflict, not silent last-write-wins. A stale form must reauthorize on submit after membership revocation or company inactivation.

For the proposed interactive server UI, do not hold a mutable DbContext/company singleton for a circuit. Create short-lived contexts per authorized application operation. Both HTTP endpoints and direct UI-service calls must invoke the same authorization/use-case boundary; API middleware alone does not protect a server component that bypasses it.
