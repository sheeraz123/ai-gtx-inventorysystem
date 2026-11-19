# Stages 1–2 implementation results

## Executed verification

Environment: Windows, existing .NET SDK 10.0.403; target .NET 10, WPF net10.0-windows. Branch main. No SDK/runtime/system software installed or upgraded.

- `verify-stage1-2.ps1 -SkipRestore`: PASS.
- Release solution build: five application projects plus one test project; **0 warnings, 0 errors**.
- xUnit: **31 passed, 0 failed, 0 skipped**. Evidence: `TestResults/stage1-2.trx` (ignored local artifact).
- WPF auto-closing smoke: loaded/visible window, all **19** planned navigation targets exercised, exit 0. Evidence: `src/GTS.Billing.Presentation.Wpf/bin/Release/net10.0-windows/logs/smoke.json` (ignored local artifact). No manual visual/accessibility acceptance claimed.
- Explicit CLI development initialization into a new ignored .local/cli-smoke database: exit 0. Subsequent configured WPF smoke/restart: exit 0, database SHA256 unchanged. Only installation and plaintext security metadata were seeded.
- Migration: `20261116000100_InitialDevelopmentSchema`. All 33 expected domain tables created; EF snapshot matches the model with no pending changes.
- Foreign keys, unknown-parent rejection, duplicate company/user/default bank rejection, singleton admin assignment maximum, same-company GST mapping, cross-company GST/branding rejection, state mismatch, financial-year boundaries, rollback, optimistic concurrency, permanent numbering, immutable terms/audit, restart persistence and missing/corrupt established state are tested.
- Git database inclusion check: PASS; no tracked or unignored database files under Billing-System. No commit or push performed.

Initial attempts exposed and resolved a vulnerable old native SQLite dependency, an incomplete migration snapshot and test assertion/compiler issues. No warning suppression was used to bypass dependency vulnerability detection or EF model drift. Final versions restored successfully.

## Direct NuGet dependencies

| Package | Version | Usage |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | WPF view-model state/commands |
| Microsoft.Extensions.Hosting | 10.0.15 | WPF DI/configuration/logging/lifetime |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.15 | Plain SQLite persistence |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | Patched native SQLite selection |
| Microsoft.EntityFrameworkCore.Design | 10.0.15 | Test-only migration snapshot generation/check |
| Microsoft.NET.Test.Sdk | 17.14.1 | Test host |
| xunit | 2.9.3 | Tests |
| xunit.runner.visualstudio | 3.1.4 | Test adapter |

Resolved SQLite native package: SQLite 3.53.4; managed Microsoft.Data.Sqlite.Core 10.0.15 and SQLitePCLRaw core/provider/config 3.0.5. No SQLCipher or cryptographic integration package was downloaded or installed.

## Acceptance status and limits

Completed: five compiling application projects, dependency-direction test, DI startup, planned navigation, configuration/event-only logging, lifecycle foundation, persistence contracts, EF schema/configurations, initial migration/snapshot, database constraints, transaction abstraction, explicit development initialization and fail-closed existing opening.

Schema maximum-one Super Admin does NOT imply an operational exactly-one setup workflow. Zero admins is intentional before setup. At-most-one active default bank is enforced; requiring exactly one at company activation is future business validation. GST checks are structural, not full registration verification. Backup defaults and security/envelope tables are inert metadata only. Immutable JSON snapshot records are a foundation, not completed invoice issuance/printing.

Database is **UNENCRYPTED, DEVELOPMENT/TESTING ONLY**. All integration fixtures are synthetic; automated tests use disposable databases. The CLI smoke database remains as ignored synthetic initialization evidence. No authentication, permissions evaluator, password generation/recovery, Company Master CRUD, numbering allocation, backups/restore, USB handling, encryption or Windows provisioning is activated. Global multi-user session coordination is not implemented; this shell is not an approved multi-account operational deployment.

SQLCipher migration, Access compatibility, original invoice-layout matching, manual UI verification and production readiness are NOT verified.

## Preserved boundaries

All source/configuration/migration/test/documentation changes are inside Billing-System. Existing root files, AI-Harness and earlier docs were not edited. AI-Harness was not executed. No Git configuration/branch/remote changes, commits, pushes, merges or deployments occurred. No Windows accounts/groups/ACLs/services/tasks/settings were provisioned or changed.

The SDK printed a first-use development-certificate message. A read-only check of current-user certificate metadata showed the sole development certificate predates this session; no trust command was executed. Subsequent SDK commands explicitly disable certificate generation and use project-local CLI/package state.

## Before Stage 3

Separate approval is required. Decide the next authentication scope, reviewed password-hashing dependency, production SQLCipher package/licensing and key-management direction, machine-wide ownership, shared-data ACL/provisioning procedure, backup defaults/recovery procedure and logout policy before activating those capabilities. No later stage will run automatically.
