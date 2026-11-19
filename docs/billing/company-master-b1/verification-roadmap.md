# B1 testing, acceptance and implementation roadmap

Status: **planned tests and milestones, none executed for Billing**. This documentation does not borrow AI-Harness test counts as proof of Company Master functionality. No Billing source, database or test runner was created or run.

## Test strategy

Use unit tests for domain normalization, lifecycle and invariant behavior; real approved-provider integration tests for transactions, RLS, composite constraints, concurrency and migrations; API authorization/contract tests with at least two companies; and browser acceptance tests for the proposed UI. EF InMemory or a different SQLite provider is not evidence that SQL Server RLS/constraints work. Use synthetic identity/tax data with approved fixtures, never customer credentials or production taxpayer records.

| ID | Scenario | Required outcome |
| --- | --- | --- |
| CM-01 | Create minimal Draft, then replay idempotency key | One Company/initial access assignment; stable creation receipt; no duplicate rows |
| CM-02 | Whitespace/Unicode names; normalized code collisions and boundary lengths | Preserve legitimate legal names; reject invalid/bounded fields and duplicate normalized code atomically |
| CM-03 | Activation with missing currency/country/address/contact/year/administrator or unsupported tax profile | 409/readiness errors; Company remains Draft, no partial activation |
| CM-04 | Active → Inactive → Reactivate, with permissions/reasons | Audited transitions; new transaction authorization blocked when inactive; archival read only for authorized users |
| CM-05 | Two clients edit with the same ETag | One success, one 412; no lost updates or partial child/audit writes |
| TAX-01 | Approved PAN/GSTIN profiles, malformed/checksum/state/PAN mismatch and special kinds | Approved fixtures determine results; format checks do not claim issuance/portal verification |
| TAX-02 | Multiple registrations, including different states, owned by one Company | Preserve valid approved cases without a one-GSTIN or one-state assumption |
| TAX-03 | Used registration/address/legal identity changed | Prevent ordinary destructive edits; controlled history/supersession and issued-document snapshots preserved |
| FY-01 | Concurrent financial-year ranges touching/overlapping at boundaries | Enforce approved inclusive no-overlap rule per Company under concurrent transactions |
| FY-02 | Cross-company default year or attempted historical boundary/currency changes | Reject invalid ownership and protected historical changes |
| AUTH-01 | User of Company A requests Company B or an unknown Company | Same safe 404 contract; zero B data, counts, contacts, tax IDs or storage metadata |
| AUTH-02 | A route with B child ID, forged body CompanyId, foreign role/year/address ID | Reject; same-company composite FKs also prevent direct invalid writes |
| AUTH-03 | Company Administrator attempts platform role escalation or grant beyond delegation ceiling | Deny and record a safe security event; no access changes |
| AUTH-04 | Two administrators concurrently remove/inactivate the last owners; global user disabled | Preserve one operational platform Super Admin and at least one active Company Administrator per active Company |
| AUTH-05 | Membership revoked while a form/export/job is waiting | Reauthorize at documented boundary; stale client capability list does not grant access |
| AUTH-06 | UI code calls application service directly, bypassing API routing | The same authorization/ownership rules apply |
| DB-01 | Native reads/writes as application principal with no/wrong company context | RLS denies reads or returns no tenant rows, and blocks invalid writes; no default-company fallback |
| DB-02 | Raw SQL/unfiltered ORM query, mixed-company joins and bulk paths | Database policies still constrain all relevant paths; any unsupported path is forbidden |
| DB-03 | Reused pooled connections, parallel A/B requests, rollback/retry and session reset | No context leaks; context verification precedes every tenant operation |
| DB-04 | Audit append or initial membership creation fails during mutation | Entire business transaction rolls back; no unaudited successful change or unusable Active company |
| DB-05 | Migration/provisioning principal versus normal application credentials | Least privilege enforced; normal principal cannot disable row policies or alter schema |
| UI-01 | Switch companies with a dirty form; use two tabs and delayed responses | Draft remains bound to original Company; explicit save/discard/cancel; stale responses cannot replace another company's UI |
| UI-02 | Read-only/masked-tax views, keyboard navigation, validation and 412 recovery | Accessible controls; no hidden sensitive payload merely concealed by CSS |
| ASSET-01 | Optional third logo, invalid slot, oversize/polyglot/SVG/path attack | Reject before persistence; only two validated private company-owned assets |
| OPS-01 | Backup/restore/import into quarantine and role reconciliation | Restore only intended Company; verify row ownership, referential integrity, identities and audit continuity before promotion |
| OPS-02 | Access import duplicates/missing company ownership/invalid masters | Quarantine with deterministic source-ID mapping and reconciliation; no silent company assignment or dual write |

## Release acceptance criteria

The first usable Company Master release is accepted only when:

1. D01–D07 and D09–D12 have signed-off answers appropriate to its exact scope; D08 remains a blocking prerequisite for invoicing, not a reason to ship numbering assumptions now.
2. Authorized users can create Draft, configure approved identity/contact/address/tax/year fields, review readiness, activate/select, edit with concurrency, and inactivate a Company.
3. Company discovery exposes only permitted summaries; direct and indirect access tests show no cross-company reads, writes, roles, years, assets or audit data under the normal DB principal.
4. Real-provider isolation, transaction, duplicate, last-administrator and pool-reuse tests pass; query-filter tests alone are insufficient.
5. Every sensitive state/access change has a durable appropriately redacted audit event; actor/scope fields cannot be supplied by clients.
6. There are no embedded credentials, unrestricted uploads/exports, general database-switch endpoints or implicit Super Admin data bypasses.
7. Deployment, backup/restore, support/patching, privacy retention and rollback procedures have owners and verified evidence.
8. Branches, warehouses, invoice issue/number allocation, tax calculations, inventory, manufacturing and full accounting are not represented as implemented by Company CRUD.

## Phased roadmap

| Milestone | Small deliverable | Dependencies / exit evidence | Main risks and approval |
| --- | --- | --- | --- |
| B1-D0 — decisions/discovery | Resolve company meaning, hosting/UI/identity/DB; inventory sanitized Access schema/workflow | This pack reviewed; decisions D01–D04/D10/D11 recorded | Wrong tenant boundary or offline assumptions; business and architecture approval required |
| B1.1 — isolated Billing skeleton | Separate solution, domain/application/infra/host boundaries, configuration and synthetic test fixtures | D0; approved SDK/provider packages and development environment | No automatic software/DB install; approve the source scope before creating projects |
| B1.2 — access/isolation proof | Identity mapping, membership/permissions, immutable company context, same-company FK pattern and DB policy prototype | B1.1 + approved real DB/test environment; AUTH/DB negative tests | Prove isolation before broad CRUD; separate installation/provisioning approvals if needed |
| B1.3 — Company profile | Draft create/read/edit, normalized codes, optimistic concurrency and transactional audit | B1.2 + D05; CM-01/02/05/DB-04 | Mass assignment, code collisions and lost updates; review API DTOs and invariants |
| B1.4 — addresses/contacts/tax | Scoped collections and approved jurisdiction validation/history | B1.3 + D06; TAX/ownership tests | Registration/legal-identity ambiguity; tax/data owner approval of validation fixtures |
| B1.5 — years/lifecycle/selection | Financial-year management, activation checklist, company selection and inactivation | B1.4 + D07; FY/CM-03/04/UI tests | Overlaps, stale contexts, irreversible currency/calendar edits |
| B1.6 — membership UI and operational hardening | Invitations/delegation/revocation, last-owner guards, audit UI, optional two logos/IEC completion | B1.2–5 + D04/D09/D12; AUTH/ASSET tests | Access escalation, leaked PII/files; identity/storage and privacy review |
| B1.7 — Company Master acceptance | Real-provider regression, UI acceptance, backup/restore rehearsal and reviewed deployment plan | All scoped acceptance criteria; product/security sign-off | No production cutover/deploy automatically; separate approval for rollout and any real-data import |
| B2 — invoicing foundation (future) | Company/year/registration selection, series policy and issued-document identity snapshots | B1 accepted + D08 and complete invoice/tax requirements | Do not implement invoice numbering/GST calculations before rule approval |

Each implementation increment requires a concrete file/behavior/test scope and reviewable changes. Approval of B1-D0 planning is not approval to install software, change Windows settings, create a database, migrate live Access data or deploy. The harness's sample-plan approval does not authorize Billing code generation, autonomous execution or source application.

## Prerequisites and unanswered questions

- No Billing solution, DB engine/provider, hosting/UI choice or identity provider exists in source. Select/authorize them before implementation; do not infer they are installed from the harness framework reference.
- Confirm browser versus desktop/offline operation, multi-user scale, deployment network and operational ownership. If offline writes are mandatory, revise this centralized authorization model before coding synchronization.
- Supply sanitized Access schema/relationship/report examples and define source of truth, coexistence and reconciliation. No Access migration can be accurately estimated yet.
- Confirm whether Company is a legal entity, GST registration, accounting book or SaaS customer; whether PAN/code duplicates across companies are legitimate; and whether any cross-company reports/sharing are required.
- Approve tax registration kinds/verification process, mandatory business fields, fiscal/currency rules, language/format defaults and document-history policy with responsible business owners.
- Confirm one Super Admin's bootstrap, MFA/recovery/transfer and company-access rules, company invitation ownership and revocation guarantees.
- Define recovery objectives, retention, expected data volume, logo handling and sensitive-field export/reveal policy. No invented SLA/RPO/RTO is binding in this pack.
- Current harness live generated-code execution remains unavailable. B1 documentation does not attempt to resolve/enable that backend. Human-reviewed implementation and any execution authorization must be separately scoped; no generated code was run here.

## Approval requested before implementation

Review this specification pack and decide D01–D04 first, including **shared SQL Server + RLS versus separate databases**, **web versus desktop/offline UI**, company/registration meaning and identity model. Then approve the exact B1.1 source scope and test/dependency plan. Until that approval, stop at documentation; do not create Billing projects or database migrations.
