# Stage 3.5 machine-wide ownership

Stage 3.5 adds global Windows mutex ownership and the approved logout-and-exit behavior. The existing five application projects remain. Stage 3 authentication and the stale-session fix remain active. SQLite is plaintext and restricted to synthetic development data.

## Provisioning prerequisite

The shipped executable reads exactly one pre-provisioned file at
`%ProgramData%\GTS Billing\Installation\identity.json`.

An administrator must approve and provision the following before normal use.

1. One nonempty, nonsecret deployment GUID that stays stable across Windows accounts, launches, database moves and upgrades.
2. An explicit participant SID list for approved Windows accounts or a restricted local group. Resolve SIDs on the target host. The application rejects Everyone, Authenticated Users, Interactive, Builtin Users and Builtin Guests as participant grants.
3. A machine-wide configuration directory and identity file writable only by trusted administrators and readable by the participating accounts. Redirected identity files/directories are rejected. Filesystem ACL enforcement must be verified separately.
4. Shared development data on an internal drive with suitable SQLite database-directory access, including journal/WAL/SHM files. Keep all accounts configured for that same database.
5. Approved cross-account checks of actual mutex rights, ownership and handoff.

The identity document contains `DeploymentId` as a GUID and `AuthorizedSids` as an array of approved SID strings. It contains no database credentials. Its identifier is independent of the existing SQLite installation ID. Unknown JSON members, empty GUIDs, invalid SIDs, missing files and inaccessible files stop startup. Ordinary startup never generates deployment identity or provisions directories.

No provisioning was performed during implementation. This host has no deployment identity at the required path. The actual executable was verified to exit with code 3 on `--smoke-test`, before opening SQLite. Authorized provisioned startup remains a separate acceptance gate.

## Bootstrap and Windows permissions

`Program.Main` is the explicit STA entry point. It validates deployment identity, constructs `Global\GTS.Billing.<deployment-guid>`, and attempts a zero-wait native mutex acquisition before creating the WPF App or DI host. The CLI offers no identity, namespace or configuration-path override. Synthetic identity injection is internal and used by the test assembly only.

`GlobalInstanceLock` uses CreateMutexExW with a protected security descriptor. SYSTEM and Builtin Administrators receive administration rights. Approved participant SIDs receive SYNCHRONIZE. Each runtime handle requests only SYNCHRONIZE. Same-account automated tests demonstrated wait and release with these rights. The Windows creator remains an object owner with associated security implications. The application does not grant participants explicit DACL/owner modification rights.

Creation alone does not establish ownership. Wait results distinguish acquired, busy and abandoned. Native errors distinguish access denial, invalid configuration/name collision and other failures. A failed launch exits without trying another name or namespace. Existing object security comes from the existing object. A hostile pre-created object can cause denial of service.

Busy interactive launches display “GTS Billing is already running on this computer. Close the other instance and try again.”
Other ownership failures display “GTS Billing could not verify exclusive access. The database was not opened.”
Automation modes use exit codes and sanitized diagnostics instead of a blocking message box.

Exit codes are 0 for completed operation, 1 for startup/database verification failure, 2 for contention and 3 for unverifiable ownership/configuration. Fatal process failures terminate the whole process.

## Operational lifetime

All shipped modes pass through the bootstrap, including normal UI, explicit development initialization, schema export and startup smoke. The STA entry thread owns and releases the mutex. Thread-mismatched disposal is rejected. Duplicate disposal does nothing after successful release.

`ApplicationLifetime` admits and tracks work under a monitor, creates linked cancellation tokens and keeps a cached shutdown task. It tracks host startup, startup database work, authentication operations and recovery-document actions. Shutdown closes admission, clears sensitive presentation state, requests cancellation and awaits every accepted operation. It then invalidates the session, stops/disposes the host and requests WPF shutdown. The entry point releases the mutex only after WPF has exited with the coordinator marked stopped.

Closing the main window is initially cancelled while draining. Repeated close/logout actions cannot start another drain. A timeout never releases ownership. Cleanup failure leaves the process in its stopping state and reports that ownership is retained. Unrecoverable dispatcher/runtime failure uses process-wide termination so the owner thread cannot unwind while surviving workers continue to access SQLite.

Logout now closes GTS after this drain. The next employee launches GTS again. Password-recovery completion and stale-session rejection still return to login in the same instance and keep ownership. Authentication records, password hashing, recovery validation and transactions retain their existing responsibilities.

If a logout audit write fails, the service has already cleared its in-memory identity and disposed its store operation. The coordinator records a sanitized diagnostic and continues resource cleanup. Other shutdown cleanup failures keep ownership.

## Crash recovery

An abandoned mutex is logged as an unclean-exit signal. A crash can also destroy the object when the last handle closes. Therefore both ordinary and abandoned starts verify the database before setup or login.

Verification uses full SQLite integrity_check, foreign_key_check, foreign-key enforcement, exact expected migration history, the ready startup marker, matching SQLite installation ID and plaintext development mode. SQLite performs its normal journal recovery. Pending/unknown migrations, damaged state, missing established files and mismatched identity stop startup without creating a replacement. Explicit initialization remains a separate deliberate command.

The existing initial migration and schema are unchanged. Session-history rows are never ownership authorities. No timestamps, heartbeat ages or lock-file deletion authorize takeover.

## Verification evidence

The pre-change baseline passed all 48 existing tests. The final Release build completed with zero warnings and zero errors. The complete Stage 3.5 suite passed **66 tests, zero failures and zero skips**, including 18 Stage 3.5 test cases. Evidence is in `TestResults/stage3-5-final.trx`. Thirteen test cases launch test-owned cooperating processes. The remaining new cases cover local validation, thread affinity and lifetime coordination. The crash pair also has a focused report in `TestResults/stage3-5-crash.trx`.

Automated tests use the existing test project as an executable fixture, with isolated synthetic deployment identities. The test executable is not installed or used by application runtime. All child processes belong to those tests. No real user process is terminated.

Covered cases include competing and simultaneous processes, normal release/successor acquisition, name collisions, denied SYNCHRONIZE access, every CLI mode rejecting before output/database creation, thread-affinity enforcement, repeated disposal, shutdown cancellation and admission rejection, real transaction drain, cleanup failure retention, and interrupted uncommitted SQLite transactions.

Crash tests cover both a retained observer handle that produces abandonment and destruction of the mutex with no surviving handle. The next WPF startup verifies the same disposable database and finds no uncommitted Company record. A WPF authentication/logout smoke initializes synthetic credentials, enters the shell, blocks a competitor, exits through Logout, and allows the next owner. Recovery codes and credentials are never printed or captured in screenshots.

Real two-Windows-account use, Fast User Switching, Windows lock/disconnect, filesystem provisioning, accessibility/native-dialog checks, forced Windows shutdown and hostile-user resistance remain pending separate approval. Same-account native permission tests do not establish cross-account security.

## Reproduction

From Billing-System, using the existing SDK and project-local packages:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.cli-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build GTS.Billing.slnx -c Release --no-restore
dotnet test GTS.Billing.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=stage3-5-final.trx' --results-directory TestResults
```

The old Stage 1–2 verification script's direct executable smoke requires the pre-provisioned identity now. Its historical shell-only launch behavior is superseded by the ownership gate. The test suite performs provision-free synthetic composition without changing the shipped CLI or ProgramData.

## Security limits

The mutex coordinates cooperating application instances. Direct SQLite access, manipulated clients, sufficiently privileged Windows users and administrators remain outside that boundary. The shared plaintext database and audit records are not tamper resistant. A local user can deliberately obstruct mutex acquisition. The first creator's object-owner rights require consideration in the cross-account security review. The machine identity file and participant list rely on separately approved Windows ACL provisioning.

No Windows accounts, groups, filesystem ACLs, services, tasks, registry settings, SDK/runtime installations or shared machine files were changed. AI-Harness and unrelated repository files were untouched. Stage 4, encryption, backups and production provisioning require separate approval.

## Exact file inventory

All paths are relative to `D:\Projects\ai-gtx-inventorysystem\Billing-System`. Build/test output and the ignored synthetic fixtures are local artifacts.

Created files

```text
src/GTS.Billing.Presentation.Wpf/Program.cs
src/GTS.Billing.Presentation.Wpf/GlobalInstanceLock.cs
src/GTS.Billing.Presentation.Wpf/ApplicationLifetime.cs
tests/GTS.Billing.Tests/GlobalInstanceLockTests.cs
tests/GTS.Billing.Tests/ApplicationLifetimeTests.cs
tests/GTS.Billing.Tests/ProcessProbe.cs
docs/stage3-5-instance-locking.md
```

Modified files

```text
src/GTS.Billing.Presentation.Wpf/GTS.Billing.Presentation.Wpf.csproj
src/GTS.Billing.Presentation.Wpf/App.xaml
src/GTS.Billing.Presentation.Wpf/App.xaml.cs
src/GTS.Billing.Presentation.Wpf/MainWindow.xaml.cs
src/GTS.Billing.Presentation.Wpf/AuthenticationViewModel.cs
src/GTS.Billing.Presentation.Wpf/AuthenticationView.xaml.cs
src/GTS.Billing.Infrastructure/DevelopmentDatabase.cs
tests/GTS.Billing.Tests/GTS.Billing.Tests.csproj
tests/GTS.Billing.Tests/AuthenticationTests.cs
README.md
```

Existing authentication tests were adapted only for the approved logout-and-exit state and to assert that stale-session reauthentication keeps the application lifetime active. The initial migration, EF schema, application authentication service and dependency versions were preserved.
