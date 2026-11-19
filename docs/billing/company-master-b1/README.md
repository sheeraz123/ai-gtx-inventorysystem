# B1 — Company Master and multi-company design

Status: **draft for business/technical approval; documentation only**. Nothing in this pack approves implementation, installs a database, selects a production tax policy, enables the harness, or authorizes changes to application data.

## Specification pack

| Document | Purpose |
| --- | --- |
| [Domain and validation](domain-validation.md) | Company fields, lifecycle, tax registrations, financial years and business rules |
| [Schema and relationships](schema.md) | Logical relational schema, keys, isolation constraints and future extension points |
| [API and UI](api-ui.md) | Endpoint contracts, concurrency/errors, Company screens and switching behavior |
| [Security model](security.md) | Authentication, membership, authorization, company context, RLS, auditing and operations |
| [Verification and roadmap](verification-roadmap.md) | Test/acceptance matrix, dependencies, risks and implementation approval gates |

## Repository assessment

Inspected the repository root, source/project manifests, existing Billing requirements and harness security documentation. No `AGENTS.md` was found in the repository or checked `D:/`, `D:/Projects/` ancestor locations. Recheck instructions before future implementation.

| Area | Observed, not assumed |
| --- | --- |
| Root | Existing repository; root README describes multi-industry ERP Billing |
| Solution | Only `AI-Harness/AI.GTX.Harness.slnx` found in source; six module projects, xUnit project and trusted process-test helper |
| Framework | Harness targets `net11.0`, SDK pin 11.0.100, stable `latestMajor` C#, nullable enabled, warnings as errors |
| Architecture conventions | Core contracts, orchestration, role adapters, controlled tools, infrastructure/DI and console Host; Microsoft hosting/options/JSON logging; xUnit |
| Billing source | No `Billing-System` directory, Billing solution, domain implementation or migrations found |
| Database | No Billing provider/EF DbContext/schema configured; Access is mentioned in requirements, but no Access database/schema was provided or inspected |
| UI | Harness is a console app. No existing Billing Blazor, MVC, WPF or WinForms framework found |
| Security/execution | Harness still registers unavailable isolation/execution backends; documentation records plan-only sample approval and an unverified live backend |
| Repository status | Source/docs are currently untracked alongside the existing tracked README. This plan does not stage, commit, push or merge them |

No Billing application source, harness source/configuration or system settings were modified. No application solution was created. No builds, tests, database commands, generated code or live AI agents were run for B1; existing historical test results are not B1 implementation evidence.

## Proposed architecture, subject to D01–D04 approval

Use a **modular monolith** in a separate future `Billing-System` solution. Begin with Company Administration and Identity/Access modules, then add invoicing and other business modules behind explicit company-scoped application services. Avoid microservices/distributed transactions for the first CRUD module.

Proposed stack: approved stable .NET/C# target, ASP.NET Core application/API host, a server-rendered Blazor Web App, and EF Core with one approved relational database provider. Matching the harness's .NET 11 baseline is the candidate, but production support horizon, deployment OS and package/provider compatibility must be approved separately; the harness SDK pin does not choose Billing's lifecycle policy. Microsoft DI, options, validation, authorization, Problem Details and xUnit should supply established infrastructure. Do not reference or embed AI-Harness in the Billing runtime.

The browser-based UI is an assumption, **not a discovered convention**. Confirm LAN/cloud access, concurrent users, offline needs, desktop/printer integration and Access coexistence first. An offline desktop requirement may change the UI/deployment choice before code is written.

Proposed future layout (not created by B1):

```text
Billing-System/
  AI.GTX.Billing.slnx
  src/AI.GTX.Billing.Domain/          company aggregates and invariants
  src/AI.GTX.Billing.Application/     use cases, authorization and scoped transactions
  src/AI.GTX.Billing.Infrastructure/  approved database, identity adapters, audit/storage
  src/AI.GTX.Billing.Host/            API, Blazor UI, composition and configuration
  tests/AI.GTX.Billing.UnitTests/
  tests/AI.GTX.Billing.IntegrationTests/
  tests/AI.GTX.Billing.AcceptanceTests/
AI-Harness/                           unchanged separate development tool
```

Use business-specific persistence/application interfaces such as `ICompanyRepository`, `ICompanyUnitOfWork`, `ICompanyAccessEvaluator` and later `IInvoiceNumberAllocator`; do not build a generic repository/provider framework. Keep transaction/constraint behavior explicit rather than claiming every database is interchangeable.

## Shared database versus separate databases

| Consideration | Shared database, common schema + CompanyId | Separate database per company |
| --- | --- | --- |
| Boundary | Application authorization + composite constraints + tested DB row policies | Same application authorization plus separate database credentials/routing; stronger accidental-access boundary |
| Compromised application/admin | Broad credentials or a compromised server can still expose multiple companies | Compromise is more contained only if credentials/access are genuinely separated; a broker holding all credentials remains sensitive |
| Maintenance | One controlled schema migration and reference-data pipeline | Fleet migrations, provisioning, schema-version tracking and partial-failure handling |
| Backup/restore | Simple full backup; company-only recovery needs tested extraction/reconciliation | Company-level backup/restore simpler; catalogue/identity consistency still required |
| Availability/performance | Shared failure/noisy-neighbor risk; quotas and monitoring needed | Per-company tuning/failures can be separated, depending on hosting topology |
| Reporting | Easier authorized aggregate reporting, still requires explicit cross-company capability | Explicit aggregation service/ETL; never arbitrary connection switching |
| Residency/retention | Harder independent placement/retention | More flexible placement and retention; operational cost higher |
| Cost/operations | Lower initial operational burden | More databases, secrets, connection pools and recovery procedures |

**Proposed initial choice:** shared SQL Server database with mandatory CompanyId, composite foreign keys, resource authorization and row-level security, provided the real-provider isolation tests pass. SQL Server is a candidate because the environment is Windows/Visual Studio and it has native RLS, not because a server is already installed. PostgreSQL is a viable alternative if deployment/licensing/operations favor it; choose one provider and test its policies. No edition, paid license, hosting service or installation is approved here.

Select separate databases instead when contractual isolation, recovery, residency or workload requirements demand it. Preserve CompanyId even in separate databases. A trusted server-owned catalogue must resolve authorized CompanyId to a database; never accept a connection string/database name from the client. Provisioning becomes a state machine with Pending/Failed/Active states and explicit reconciliation, not an assumed cross-database transaction. Provider-specific migrations and RLS must be redesigned/tested when changing engines. Schema-per-company is not recommended as the initial compromise because of operational complexity and provider differences. Microsoft describes the major EF Core tenancy strategies: [multi-tenancy guidance](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy).

## Decisions requiring approval

| ID | Decision / proposed baseline | Why it blocks or changes implementation |
| --- | --- | --- |
| D01 | Company is the initial accounting/security boundary; can have multiple GST registrations | Confirm whether users instead mean one company per GST registration, and whether distinct SaaS customer organizations exist |
| D02 | Shared DB + composite keys + RLS; SQL Server candidate | Confirm isolation obligations, hosting, licensing, backup/restore needs and provider |
| D03 | Browser/LAN/cloud web UI; Blazor candidate | Confirm offline/desktop/printing needs and deployment topology |
| D04 | Identity provider and one platform Super Admin; no implicit company-data bypass | Approve authentication/MFA, bootstrap/recovery, invitations and membership responsibilities |
| D05 | Draft/Active/Inactive lifecycle and activation requirements | Confirm required fields, editing rules, code uniqueness and historical access |
| D06 | Jurisdiction/tax profile, GSTIN/PAN/IEC validation and duplicate policies | Business/tax owner must approve authoritative rules; ordinary Indian GST rules are not universal |
| D07 | Company financial-year calendars and currency policy | Confirm defaults, opening/closing, overlapping periods and changes after documents exist |
| D08 | Invoice series scope/reset/concurrency/cancellation policy | Needed before numbering implementation; no legal numbering rules invented here |
| D09 | PII/audit retention, exports, encryption and recovery objectives | Required before production data or backups |
| D10 | Access migration/coexistence and authoritative source | Need sanitized schema/workflow/examples and reconciliation plan before migration |
| D11 | Industry taxonomy versus business constitution | Avoid conflating industry classification with legal business type |
| D12 | Delivery scope and optional IEC/two-logo features | Earlier requirements retain these; decide whether first CRUD milestone includes them |

Default positions above are **proposals**, not approvals. Stop at the implementation gate in the roadmap after these decisions and the acceptance scope are reviewed.
