# Recurring startup failure — repaired

The reviewed repair found **20261118000200_CompanyInputFlexibility** pending in the configured database. Application and schema versions were out of sync. A broad exception handler previously hid that reason behind a generic path/installation message.

The pending migration was applied under the original global mutex, after a verified SQLite backup. Every prior non-EF table's rows and values were compared cryptographically before/after migration. User/password hashes, recovery-code records, company data and the installation identity were preserved without logging their values. The original marker bytes were restored exactly.

## Prevention

- Current databases open normally without another upgrade prompt.
- Recognized older development databases can offer a Yes/No confirmation for the explicitly reviewed CompanyMaster and CompanyInputFlexibility migrations. Default is No.
- Confirmation creates and verifies a consistent SQLite backup first. The approved pending list is checked again before mutation.
- A newer database explains that the executable is too old; no downgrade is attempted.
- Unrecognized/unreviewed history, missing data, invalid markers, permission/storage errors and integrity failures receive sanitized explanations.
- Smoke/automation mode never approves or applies a migration. A pending schema exits without a prompt or database changes.
- Debug and Release were rebuilt from the same source. Start-GTSBilling.ps1 provides a stable Release entry point without changing configuration or Windows execution policy.

The startup confirmation is development schema maintenance for the existing authorized Windows operator, before GTS login. It does not authenticate a GTS user or grant business permissions. Production maintenance authorization remains deferred.

## Interrupted upgrades

SQLite table rebuilds have multiple transaction phases, not one entirely atomic DDL transaction. Before mutation the updater atomically marks the installation Migrating, retaining its path and ID. Both old and new GTS builds reject this marker state.

Ready is restored only after history, integrity, foreign keys, prior data and backup checks pass. Failure leaves maintenance blocked and the verified backup available. No automatic database replacement, deletion, rollback or initialization is used to conceal failure. Recovery requires review.

The mutex coordinates cooperating processes only. Direct file writers/administrators remain outside this plaintext development boundary. Storage faults, power loss and unreviewed future migrations can still require intervention; no guarantee of infallible startup is claimed.

## Evidence

- Focused repair suite: **16 passed / 0 failed / 0 skipped**.
- Final complete regression: **149 passed / 0 failed / 0 skipped**.
- Release build: **0 warnings / 0 errors**.
- Debug build: **0 warnings / 0 errors**.
- Actual Release startup: exit 0, visible Login, operational shell locked.
- Actual Debug startup: exit 0, visible Login, operational shell locked.
- Repeated actual Release startup: exit 0, visible Login, operational shell locked.
- The Release application was then opened normally for the user.

The first complete iteration passed 148 cases and failed one assertion expecting the old generic exception type. That assertion now checks DatabaseStartupException, and the final full run passed.

Safety backup and evidence directory:

.local/database-upgrades/20261118-184721-be9cb32a18c6441992b316bbe906e950/

Files: before-upgrade.db, before-upgrade.installation.json, verified-backup.json, completed-upgrade.json, release-startup.json, debug-startup.json and release-restart.json.

TRX evidence: TestResults/startup-repair-focused.trx and TestResults/startup-repair-final.trx.

The confirmation dialog has not been manually visually signed off. Automated tests cover the maintenance service, backup/approval failures, an injected failure after table rebuild, data/account preservation, specific messages and refusal to upgrade in automation. The actual repair used this conversation's explicit repair instruction through a temporary mutex-owning runner calling the same service.

## Usage

From Billing-System:

~~~powershell
./Start-GTSBilling.ps1
~~~

Alternatively launch src/GTS.Billing.Presentation.Wpf/bin/Release/net10.0-windows/GTS.Billing.Presentation.Wpf.exe. Visual Studio's Debug build is synchronized too. Avoid launching old copied bundles against a newer database.

## Files

Created:

- Start-GTSBilling.ps1
- src/GTS.Billing.Infrastructure/DatabaseStartupException.cs
- src/GTS.Billing.Infrastructure/DatabasePreservation.cs
- src/GTS.Billing.Infrastructure/DevelopmentDatabaseMaintenance.cs
- src/GTS.Billing.Presentation.Wpf/StartupFailurePresentation.cs
- tests/GTS.Billing.Tests/StartupRepairTests.cs
- docs/startup-repair.md

Modified:

- src/GTS.Billing.Infrastructure/DevelopmentDatabase.cs
- src/GTS.Billing.Presentation.Wpf/App.xaml.cs
- src/GTS.Billing.Presentation.Wpf/SafeDiagnostics.cs
- tests/GTS.Billing.Tests/PersistenceTests.cs
- tests/GTS.Billing.Tests/CompanyTests.cs
- tests/GTS.Billing.Tests/DatabasePathPolicyTests.cs
- tests/GTS.Billing.Tests/GlobalInstanceLockTests.cs
- README.md

Ignored runner source/build artifacts and evidence are under .local. No new package, application project or migration was added for this repair; it applies the existing CompanyInputFlexibility migration. No AI-Harness, root configuration, Git settings/history, Windows settings, groups or provisioning ACLs were modified. No account was created/reset. SQLite remains unencrypted, suitable only for synthetic development data.

Existing pinned dependencies were restored from the local cache. The offline restore invocation used -p:NuGetAudit=false because network vulnerability lookup is unavailable; no persistent package/security configuration was changed.

~~~powershell
dotnet test GTS.Billing.slnx -c Release --artifacts-path .local/startup-repair-artifacts --no-restore --logger 'trx;LogFileName=startup-repair-final.trx' --results-directory TestResults
dotnet build GTS.Billing.slnx -c Release --source .packages -p:NuGetAudit=false
dotnet build GTS.Billing.slnx -c Debug --no-restore
~~~

