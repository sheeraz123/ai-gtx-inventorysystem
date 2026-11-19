# Stage 3.5.1 targeted corrections

The approved scope covers closing presentation, safe operational diagnostics and Option A database path validation. SQLite remains plaintext and is restricted to synthetic development/testing data.

## Closing and operation lifetime

AuthenticationPage.Closing now has an explicit heading and instructions. Normal closing shows “Closing GTS Billing, please wait…”. Accepted work still running shows “Waiting for an active operation to finish. GTS Billing will close automatically.”

ApplicationLifetime.IsWaitingForOperations reads the existing stopping flag and active count under the same monitor that governs admission. DrainStateChanged is published outside that monitor. The view model marshals property notifications through its captured synchronization context. The views also dispatch updates when necessary. The WPF regression checks the actual heading, instructions, hidden credential form and denied submission during an accepted operation.

Admission, cancellation, transaction disposal, host cleanup and owning-thread mutex release retain the existing order. Logout exits. Stale-session rejection and successful recovery still permit reauthentication without lifetime shutdown. Existing cleanup failures continue to retain ownership.

## Diagnostics

The fixed operational path is %ProgramData%\GTS Billing\Logs. The application expects administrator provisioning and never creates that directory or uses executable/profile/temp fallback destinations. This directory and its parent ACLs were not provisioned in this task.

SafeDiagnostics uses the same backend before and after DI. ILogger instances capture that backend. Internal test composition supplies fixture-local sinks and a process-local fallback for test dispatcher contexts. The shipped CLI/configuration cannot select these destinations.

Each attempt returns Written, Unavailable, AccessDenied or IoFailure. Missing or redirected directories are unavailable. Permission and I/O errors produce bounded outcomes. A single outstanding write is permitted per backend. The calling thread waits at most 500 ms for that write and makes no retries. A timed-out write may finish later, so IoFailure means persistence was unconfirmed. The worker can remain blocked by an operating-system storage failure, but subsequent diagnostics do not create additional writers or wait for it. It accesses logs only and never accesses SQLite.

Written means append and OS flush completed. It does not promise recovery from every hardware failure. Data consists only of timestamp, approved event identifier and approved severity. Unknown identifiers become diagnostic-event. ILogger state, formatter, category, exception text and sensitive input are excluded. Failure notifications occur outside the write lock and never recursively log a logging failure.

The persistent application footer warns when diagnostics are unavailable, including while the shell is authenticated. Startup/ownership failure messages retain their primary explanation and add a diagnostics-unavailable indication when applicable. Logging failure alone does not grant access, release ownership or block ordinary application use.

Operational diagnostics remain best-effort. Authentication audit transactions remain separate. Logout clears its cached identity before the audit write. If that write fails, the existing sanitized logout-audit-failed diagnostic is attempted and cleanup continues. Neither the warning nor a diagnostic is represented as durable audit success.

Smoke reports are required verification artifacts. Production-mode smoke writes under the pre-provisioned machine Logs directory. Synthetic tests inject separate existing evidence directories. A missing/unwritable evidence destination causes exit 1. No evidence directory is silently created and no alternative is chosen.

## Option A database policy

DevelopmentDatabase:Path retains its existing role. DevelopmentDatabase:AllowExplicitOverrides defaults to false. Installed/default startup uses only Path and rejects --database plus explicit development initialization. The protected appsettings.json is read from the installed application directory. Default external environment/CLI configuration providers remain disabled. Invalid opt-in values fail startup.

A deliberately configured development opt-in permits synthetic overrides and explicit initialization. Tests inject that policy internally. The shipped CLI has no bypass flag. Code does not establish effective Windows ACL protection for appsettings.json. Protecting the binary/configuration directory and its parents remains a deployment prerequisite. A modified client or writable installed configuration is outside this boundary.

DatabasePathPolicy accepts conventional drive-letter paths and normalizes them lexically. It rejects relative/drive-relative paths, UNC and device namespaces, alternate data streams, ambiguous trailing dots/spaces, reserved names, known non-fixed roots and unavailable drives. It inspects existing database, marker, pending marker, SQLite journal/WAL/SHM and ancestor components for reparse-point redirection.

For explicit initialization, absent components are permitted only after validating the existing ancestor chain. The original exclusive file/marker reservation remains. Existing opening still requires the expected ready marker, database installation ID, migration history, full integrity_check and foreign-key checks. No relocation, marker rewrite or repair is performed by the path policy.

Fixed is a Windows classification and does not establish physical internal placement. SUBST mappings, short names, hard links, device topology, effective ACLs and check/use races remain provisioning/acceptance limitations. Option B native storage inspection was not introduced. The mutex name, rights and owning-thread model are unchanged and independent of every database path.

## Verification and scope

The historical Stage 3.5 report contains 66 passing cases. Two deliberate crash cases are part of that suite. They were excluded from Stage 3.5.1 execution because forced termination is outside the current authorization. No real Windows-account switching, ACL provisioning, Windows shutdown or installed-app startup against real data was performed.

Focused tests cover closing status, diagnostic outcomes/redaction and path policies. Synthetic WPF/multiprocess tests cover the real controls, startup override rejection, ignored environment opt-in, required smoke-evidence failure, normal ownership handoff and logout. Physical drives and reparse/ACL failure scenarios are simulated in unit tests where real system changes would otherwise be needed.

Project-local build/test outputs and synthetic fixtures remain ignored. No packages, schema, migration, authentication algorithms, deployment identity or mutex implementation were changed.


## Executed verification

The final focused run passed 22 cases with zero failures and zero skips. The permitted complete regression run passed 88 cases with zero failures and zero skips. The two pre-existing deliberate crash cases were excluded by filter and are **Not Executed — Separate Approval Required**. They are not counted as runner-skipped tests. The current source contains 90 total cases. The historical 66-case result was not reused as evidence for these corrections.

Commands executed from Billing-System with the existing SDK and project-local CLI home:

```powershell
dotnet test GTS.Billing.slnx -c Release --no-restore --filter 'FullyQualifiedName~SafeDiagnosticsTests|FullyQualifiedName~DatabasePathPolicyTests|FullyQualifiedName~ClosingMessagesFollowAuthoritativeDrainState' --logger 'trx;LogFileName=stage3-5-1-focused-final.trx' --results-directory TestResults
dotnet test GTS.Billing.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName!~CrashRecoveryChecksDatabaseWithOrWithoutAbandonedSignal' --logger 'trx;LogFileName=stage3-5-1-final.trx' --results-directory TestResults
dotnet build GTS.Billing.slnx -c Release --no-restore
```

Release build passed with zero warnings and zero errors. All five application projects and the test project compile. The permitted suite includes actual WPF control assertions, synthetic multiprocess startup/override rejection, normal ownership handoff, logout drain, setup/login/recovery and the stale-session regression. It also verifies explicit smoke failure when its evidence directory is absent.

Diagnostics tests cover successful and missing-directory writes, injected permission/full-disk failures, a stalled writer, bounded outstanding work, nonrecursive outcomes and credential/exception redaction. Storage tests cover actual disposable paths plus simulated drive classifications and reparse attributes. They do not establish actual machine filesystem ACLs, physical internal storage or cross-account safety.

Real logging-directory/ACL provisioning, deployment identity/shared configuration provisioning, two-account/Fast User Switching checks, forced termination, Windows shutdown tests and Stage 4 were not authorized or performed. No installed application was started against real data. Only disposable synthetic databases and project-local build/test artifacts were created or modified. Existing databases, sidecars, migrations, Windows settings, Git settings and unrelated projects were preserved.

## Exact changed-file inventory

All paths below are relative to Billing-System. Four files were created and seventeen modified. Hash comparison against the initial source inventory identified no removed files. MainWindow owns the persistent diagnostics warning so it remains visible after authentication. The Infrastructure project adds only an InternalsVisibleTo declaration for injected tests, with no dependency change.

| File | Action | Reason |
|---|---|---|
| `README.md` | Modified | Current launch prerequisites, development opt-in and diagnostics instructions. |
| `docs/stage3-5-1-targeted-corrections.md` | Created | Behavior, results, scope and deferred acceptance record. |
| `src/GTS.Billing.Infrastructure/DatabasePathPolicy.cs` | Created | Central Option A validation and installed/development selection. |
| `src/GTS.Billing.Infrastructure/DevelopmentDatabase.cs` | Modified | Apply validation while preserving existing initialization/open safeguards. |
| `src/GTS.Billing.Infrastructure/GTS.Billing.Infrastructure.csproj` | Modified | Expose internal storage probes to the existing test assembly only. |
| `src/GTS.Billing.Presentation.Wpf/App.xaml.cs` | Modified | Protected override policy, shared diagnostics and mandatory smoke evidence. |
| `src/GTS.Billing.Presentation.Wpf/ApplicationLifetime.cs` | Modified | Read-only drain state and notifications outside the monitor. |
| `src/GTS.Billing.Presentation.Wpf/AuthenticationView.xaml.cs` | Modified | Explicit Closing display and dispatcher-safe refresh. |
| `src/GTS.Billing.Presentation.Wpf/AuthenticationViewModel.cs` | Modified | Closing and diagnostics properties with UI-context notifications. |
| `src/GTS.Billing.Presentation.Wpf/MainWindow.xaml` | Modified | Persistent diagnostics warning in the application footer. |
| `src/GTS.Billing.Presentation.Wpf/MainWindow.xaml.cs` | Modified | Bind warning and marshal shell refresh onto the dispatcher. |
| `src/GTS.Billing.Presentation.Wpf/Program.cs` | Modified | Keep safe ownership errors visible when diagnostics fail; internal fixture composition. |
| `src/GTS.Billing.Presentation.Wpf/SafeDiagnostics.cs` | Modified | Fixed backend, observable outcomes, redaction and bounded writes. |
| `src/GTS.Billing.Presentation.Wpf/appsettings.json` | Modified | Default AllowExplicitOverrides to false. |
| `tests/GTS.Billing.Tests/ApplicationLifetimeTests.cs` | Modified | Verify authoritative waiting/closing messages and admission. |
| `tests/GTS.Billing.Tests/AuthenticationTests.cs` | Modified | Fixture-local logging and actual WPF closing controls regression. |
| `tests/GTS.Billing.Tests/DatabasePathPolicyTests.cs` | Created | Invalid roots/forms, redirection, missing files and override-policy tests. |
| `tests/GTS.Billing.Tests/GlobalInstanceLockTests.cs` | Modified | Installed override/environment rejection and required evidence failure. |
| `tests/GTS.Billing.Tests/ProcessProbe.cs` | Modified | Internal synthetic development, logging and evidence composition. |
| `tests/GTS.Billing.Tests/SafeDiagnosticsTests.cs` | Created | Outcomes, stalled writes, redaction and fixture support. |
| `verify-stage1-2.ps1` | Modified | Update smoke report path and avoid process termination on timeout. |

