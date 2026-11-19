# B1 security and authorization model

Status: proposed controls and threat model; **not implemented or security-certified**. No runtime gate, identity provider, database or harness permission was changed for this design.

## Threat model and boundaries

Protect against authenticated users forging CompanyId/child IDs, cross-company role assignments, stale browser contexts, cache/session/pool leakage, mass assignment, unsafe exports/uploads, background-job scope mistakes, concurrent access changes and direct database operations under the application principal. Treat request bodies, route IDs, client permission lists, imported Access data and uploaded assets as untrusted.

The proposed shared database is not an absolute boundary against a compromised application server, database owner or infrastructure administrator. A service credential able to change session context is part of the trusted middle tier. Parameterization, least privilege and operational controls remain mandatory; RLS cannot authenticate a browser user or cure arbitrary SQL injection/server compromise. Select genuinely separated database credentials/infrastructure when the required blast-radius boundary demands it.

Company is the initial tenant boundary, subject to D01. Future SaaS customer organizations, company groups and branches are distinct concepts. Do not grant group-wide data access by matching email domains, PANs, industry labels or parent-company names.

## Identity, creation and selection

Use an approved authentication system with secure server-managed sessions, MFA for privileged actions and reviewed recovery. Identity is `(trusted issuer, subject)` mapped to ApplicationUser, not a self-asserted email. Do not implement custom password/token infrastructure or store credentials in Company records. Do not auto-enroll external users based on unverified email-domain matching.

The one platform Super Admin has platform setup/provisioning/recovery permissions. **It does not automatically receive unrestricted Company business-data access.** To administer a company's data it must use an explicitly authorized, time-bounded/audited grant or an ordinary company membership under D04. No general IgnoreCompanyScope switch is proposed. Company Administrators may be multiple per company, but at least one active owner must remain. Transfer/removal/inactivation checks serialize to prevent concurrent removal of the last administrator. Global user inactivation must respect both the platform slot and all last-company-owner constraints.

Company creation is a privileged platform operation with explicit initial Company Administrator, unique code reservation, Draft state and atomic access/audit setup. Company selection is available only through active membership (or a separately authorized archival read grant). Store last selection as a preference only; the route/application command always defines actual scope. A selection cookie, header, client claim or hidden UI field cannot create membership.

## Proposed permission catalogue

| Capability | Intended use / default candidates |
| --- | --- |
| Platform.Company.Create / Platform.Admin.Transfer | Super Admin workflow only; not company role permissions |
| Company.Read | Company profile/general read; explicitly assigned members |
| Company.EditGeneral | Company Administrator or delegated master-data editor |
| Company.ManageLegalIdentity | Stronger permission for legal name/identifier-sensitive workflows; not ordinary contact editing |
| Company.ChangeStatus | Company Administrator with reason and activation checks |
| Company.Contacts.Read / Manage | Explicit contact-data access and editing |
| Company.Tax.Read / Manage | Sensitive tax detail reveal/edit; accountant/admin candidates |
| FinancialYear.Read / Manage / Close | Separate configuration/closure powers; final role mapping requires D07 |
| Company.Users.Manage | Invitations, delegated role assignment and revocation, within approved ceilings |
| Company.Audit.Read | Redacted audit feed; fuller exports require separately approved access |
| Company.Branding.Manage | Optional validated logo replacement |

These are permission definitions, not preapproved role grants. Proposed Company Administrator, Master Data Editor, Accountant and Read-only roles are templates cloned/configured within each Company. All unspecified actions deny by default. Company role assignment cannot grant platform permissions or exceed delegated ceilings. Read-only does not automatically include full PAN/contact exports. UI controls reflect server capabilities but never replace server authorization.

## Request execution pipeline

1. Validate the authenticated identity/session; reject expired/invalid/untrusted issuer or disabled user.
2. Parse route CompanyId and reject contradictory scope fields. Resolve membership through a narrowly scoped server-owned access-directory operation, not an unfiltered company-table query exposed to clients. Unknown and inaccessible Company return the same 404 shape.
3. Evaluate membership validity, permission, Company state and any financial-year ownership/state requirements. Inactive-company historical read is distinct from permission to create new transactions.
4. Construct an immutable, request/command-scoped CompanyContext: CompanyId, actor, authorization version and explicit optional financial year. Do not put mutable tenant state in a singleton, static variable, pooled service or browser-wide global context.
5. Resolve a connection using a trusted Company-to-store mapping. Open a short-lived context/transaction with the authorized scope; initialize and verify DB session context before any tenant query. Missing context must deny, never select a default company.
6. Load/update resources by both CompanyId and resource ID. Reject CompanyId changes and cross-company attached entity graphs. Assign CompanyId/actor on the server for creates; composite FKs backstop child ownership.
7. Check optimistic concurrency and business invariants inside the transaction; persist approved field changes and the redacted audit event atomically. Revalidate critical status/access at the transaction's authorization boundary.
8. Return only the scoped DTO; never expose database routing details, credentials, unrelated identity graphs or raw audit objects.

Resource authorization must apply inside application services as well as HTTP handlers, including direct Blazor component calls. ASP.NET Core describes why authorization attributes alone cannot decide resource ownership: [resource-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased).

Revocation takes effect on the next authorization boundary, including form submit, export/job start and sensitive download. Do not promise instantaneous cancellation of every transaction already authorized in flight. Define and test the exact revocation consistency guarantee under D04. Identity-token expiry alone is not sufficient membership revocation; use current membership/authorization-version checks and safe cache invalidation.

## Database isolation design

In the proposed shared SQL Server mapping, combine:

- CompanyId in all tenant rows and same-company composite keys/FKs.
- Tenant-aware EF queries/global filters as a convenient default, not the security boundary. Prohibit general application use of unfiltered/raw query paths; review any exception explicitly. EF filters can be disabled, so provider-level tests must deliberately bypass them to verify DB protections. [EF query-filter documentation](https://learn.microsoft.com/en-us/ef/core/querying/filters).
- Native RLS filter predicates for reads/updates/deletes and block predicates for wrong-company writes. Missing/invalid context denies. Use trusted server-derived CompanyId/session data, parameterized commands and least-privilege application credentials. SQL Server documents the middle-tier/session-context pattern: [RLS reference](https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security?view=sql-server-ver17).
- Separate narrowly scoped database principals/views/procedures for membership-based company discovery and platform provisioning, since those operations occur before a company context exists. Do not expose a universal bypass connection to ordinary application services. Keep predicate logic nonrecursive; do not create circular membership policies by casually joining RLS-protected membership tables inside their own predicates.
- Different migration/backup/maintenance principals. The application principal must not own tables, disable policies, alter schemas, change roles or perform unrestricted backups.

Initially disable EF DbContext pooling until context-lifetime/isolation tests establish safe behavior. Database connection pooling can remain only with a reviewed session initialization/reset strategy. Test open/close/reuse, failed transactions, retries, parallel requests and missing context. Validate the chosen SQL Server build/driver and relevant SESSION_CONTEXT/MARS/parallel-plan behavior; setting one context value once at process startup is not sufficient. Never carry a context from Company A into Company B. For a separate-database design, resolve database/credentials only after authorization and recheck the selected store's CompanyId; a wrong routing catalogue must fail closed.

Shared read-only reference catalogues are explicitly global; company business data is not. Cross-company reports, support access and administration require a separate capability, audited reason and constrained output, not reuse of a company endpoint with filters removed.

## Non-database boundaries

- Cache keys contain CompanyId and relevant authorization version/year/resource identity. Invalidate membership and sensitive-data caches on change. Do not reuse a private HTTP response across companies/users.
- Jobs/outbox messages carry CompanyId, operation identity and authorized actor/service scope. Validate access/status again before execution; never infer scope from a worker's last job or ambient thread state. No jobs are implemented by B1.
- Asset metadata and object keys are company-scoped; authorize every access through the app/private storage policy. Path prefixes alone are not permissions. Reject upload traversal, unsupported formats, decompression/image bombs and public credential-bearing URLs.
- Logs use correlation IDs, scoped metadata and error codes; never authentication tokens, connection strings, full PAN/GSTIN, unrestricted request bodies or arbitrary imported records. Sensitive detail screens/exports need explicit permissions and access auditing.
- Notifications later use company-owned recipient/template configuration; no global fallback sends another company's data. Email/SMS delivery is outside B1.

## Audit, backup and operations

Audit company creation/activation/inactivation, legal/tax/profile changes, financial-year changes, membership/role changes and sensitive exports. Record server UTC time, stable actor, company, entity/version, action, correlation and reason; use an approved redacted field-level change summary. Maintain a separate security-event stream for denied access without leaking whether another company's private resource exists.

Define access, retention, archival, key management and recovery objectives before production. Shared-database company-only restoration must occur in quarantine, with ownership/reconciliation checks and explicit access reconciliation; do not restore obsolete role grants into production automatically. Separate databases still need catalogue/identity/audit consistency. Encryption in transit/at rest and protected backups do not prevent an authorized service credential from reading data. Audit immutability has an operational trust boundary; DB administrators require privileged-access controls and review.

Future branch/warehouse permissions must always include CompanyId and same-company parent FKs. No branch permission system is claimed by this B1 proposal. No production secrets, real taxpayer records or Access files were collected for this documentation.
