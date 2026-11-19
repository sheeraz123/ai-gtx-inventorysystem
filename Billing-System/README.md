# GTS Billing System — Stage 4C implementation

Customer Master source is implemented across the five layers, with a new additive migration. See [Stage 4C scope, rules, inventory and build report](docs/stage4c-customer-master.md). **No automated tests were written/modified/run, no app was launched, and no installed database was migrated for Stage 4C.** Release outputs are isolated under .local/stage4c-artifacts; currently installed binaries remain unchanged. Customer runtime acceptance and applying its schema require a separately authorized phase. The new migration is not in the automatic startup-upgrade allowlist.

The [recurring startup repair](docs/startup-repair.md) synchronizes Debug/Release and the approved development schema, adds a stable Start-GTSBilling.ps1 launcher, and replaces the generic startup error with specific explanations. Reviewed development updates require confirmation and a verified backup; newer/unknown versions are never downgraded. Final regression: 149 passed. Both build configurations and a repeated launch reached Login.

The [Company input revision](docs/stage4a-company-input-revision.md) adds an Established on date picker and state-dropdown search, and removes GSTIN/IFSC format validation. Its migration has now been applied to the approved development installation with verified backup and preservation checks. Earlier reports retain their historical execution state.

Company Master supports authenticated Super Admin maintenance, validation, persisted current-company selection, versioned terms and database-managed PNG logos. See [Stage 4A implementation, exact file inventory and verification](docs/stage4a-company-master.md). Other business modules remain Planned. **Schema changes never silently reset or replace the database.**

Stage 3.5.1 adds explicit closing feedback, observable machine-wide diagnostics and a default-deny database override policy. See [Stage 3.5.1 corrections and verification](docs/stage3-5-1-targeted-corrections.md).

Stage 3.5 requires a pre-provisioned machine deployment identity before any executable mode runs. The test suite uses isolated synthetic identities. Logout drains work and exits. Historical reports describe provisioning state at their execution date; Stage 4A does not provision or change the installed identity. See [Stage 3.5 ownership and provisioning prerequisites](docs/stage3-5-instance-locking.md).

**UNENCRYPTED DEVELOPMENT DATABASE. Synthetic data only. Not suitable for production.**

This independent .NET 10 WPF solution contains exactly five application projects and one test project. Stage 3 adds Super Admin setup, sign-in, sign-out and recovery codes. See [Stage 3 authentication](docs/stage3-authentication.md) for the user workflow, security model and interruption behavior.

## Requirements

Existing .NET SDK 10.0.403 (pinned in global.json), Windows Desktop runtime and access to nuget.org for the initial project-local package restore. No SDK/runtime installation is performed. Dependencies are pinned in Directory.Packages.props; packages and CLI state are local ignored folders.

## Build, test and start

From `D:\Projects\ai-gtx-inventorysystem\Billing-System` in PowerShell:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build GTS.Billing.slnx -c Release --no-restore
# Full synthetic suite (including disposable child-process crash recovery):
dotnet test GTS.Billing.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=stage4a-company-master.trx' --results-directory TestResults
```

The test suite includes synthetic WPF startup, logout and navigation verification through internal test composition. Manual layout/accessibility acceptance remains separate. The historical verify-stage1-2.ps1 script runs the crash cases and requires separate approval plus provisioning for its direct executable smoke. Its smoke evidence location is now the machine Logs directory and a timeout reports failure without terminating the process.

Launch the shell normally:

```powershell
& ./src/GTS.Billing.Presentation.Wpf/bin/Release/net10.0-windows/GTS.Billing.Presentation.Wpf.exe
```

The configured development path remains explicit; there is no per-user database fallback. The application opens setup or sign-in only after ownership, installation, integrity and exact migration-history checks succeed. There are 19 navigation destinations: Company Master is Available to an authenticated Super Admin, and the other 18 remain Planned. An existing database with only the original migration must not be opened with the expectation of an automatic upgrade.

## Deliberate development initialization

Only use a new, disposable absolute path on an approved internal fixed drive, with synthetic data. These commands require separately approved deployment identity provisioning and an explicit development configuration with DevelopmentDatabase:AllowExplicitOverrides=true. The shipped default is false. Installed/default mode rejects --database and explicit initialization, even if the override equals the configured path. No environment variable or additional CLI flag enables this opt-in. Example after those prerequisites:

```powershell
$exe = Join-Path $PWD 'src/GTS.Billing.Presentation.Wpf/bin/Release/net10.0-windows/GTS.Billing.Presentation.Wpf.exe'
$devDatabase = Join-Path $PWD '.local/synthetic.db'
$p = Start-Process -FilePath $exe -ArgumentList @('--initialize-development-database','--database',('"'+$devDatabase+'"')) -Wait -PassThru
$p.ExitCode # 0 = migration and verification succeeded, 1 = stopped
# Later: verify and open that SAME existing development database:
Start-Process -FilePath $exe -ArgumentList @('--database',('"'+$devDatabase+'"'))
```

Alternatively set DevelopmentDatabase:Path to the same explicit absolute path in application configuration. There is no per-Windows-user database fallback. Stage 3.5 global ownership requires the pre-provisioned identity described above. Shared ProgramData folders, groups and filesystem ACLs require separate approval, and real cross-account operation remains unverified.

Initialization reserves a sidecar marker with CreateNew, creates a new database exclusively, applies migrations, inserts installation/security metadata, and marks initialization ready. Existing databases or markers are never overwritten by initialization. Interrupted initialization remains blocked for review. Normal opening uses SQLite ReadWrite and verifies identity, foreign keys, integrity and exact migration history. Reviewed development updates require explicit confirmation, a verified safety backup and preservation checks.

Tests create disposable databases under .local/tests and remove their own fixtures. No real passwords, employee, customer, bank or signature data should be entered. Users, Super Admin and business records are not seeded by application startup.

## Architecture

- Domain: schema records and centralized Company Master validation.
- Application: authentication/session services, Company Master use cases and persistence contracts.
- Infrastructure: EF Core SQLite, additive Company Master migration, transactionally authorized persistence, explicit development initialization and verification.
- Printing: recovery-code printing; invoice layouts remain planned.
- Presentation.Wpf: authenticated MVVM shell, Company Master list/editor, navigation, DI/configuration, safe logging and controlled lifetime.

See docs/schema.md, docs/architecture.md and docs/stage1-2-results.md. Exact authored/generated-source file inventory: docs/files-created.md.

## Diagnostics and limitations

Operational diagnostics use the fixed, administrator-provisioned %ProgramData%\GTS Billing\Logs directory before and after DI. The application never creates that directory or falls back to another location. Writes return Written, Unavailable, AccessDenied or IoFailure and retain only timestamps, allowlisted event codes and levels. The UI warns when diagnostics are unavailable. Smoke evidence is required and fails verification if it cannot be written. Test composition uses private fixture directories. The current logout audit failure policy is documented in the Stage 3.5.1 report.

Stage 3 uses application-level authentication and validates Super Admin status for code regeneration. Stage 3.5 adds machine-wide process ownership and logout-and-exit. Stage 4A adds Super Admin Company Master workflows. The database remains plaintext. Full permissions administration, billing, backups and encryption require later approval. Shared-machine deployment still requires appropriate provisioning and real cross-account verification.

No commits, pushes, SDK upgrades or deployment actions are performed by verification. The initial SDK invocation emitted a first-use HTTPS development-certificate message. A read-only metadata check found one existing certificate dated before this implementation session; no certificate trust command was run. Subsequent commands disable certificate generation. This desktop application does not use HTTPS.
