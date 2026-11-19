# Stage 3 implementation record

Stage 3 adds transactional first-run Super Admin setup, Argon2id password verification, application sessions, login and logout, audited password recovery, and recovery code regeneration with immediate password reauthentication. WPF includes one-time code display and actions for printing and saving. See [authentication, recovery and configuration](stage3-authentication.md).

The single new dependency is `BouncyCastle.Cryptography` 2.7.0. It implements Argon2id and is confined to Infrastructure. The five application projects and the original migration `20261116000100_InitialDevelopmentSchema` remain in use. Stage 3 adds zero migrations. Authentication uses the existing installation, users, singleton assignment, recovery codes and audit tables. Original migration files and model snapshots were preserved.

## Verification

- Release build passed with zero warnings and zero errors.
- Final suite passed with 47 tests, zero failures and zero skips. See `TestResults/stage3-final.trx`.
- The focused Argon2id test measured 1,996 ms for two hash operations including cold start on this machine. Hashing runs off the WPF dispatcher. The benchmark is a local observation and varies with CPU and contention.
- The Stage 1–2 baseline was run before resuming the Stage 3 changes. All 31 baseline tests and the desktop startup smoke passed.
- The executable smoke test opened WPF both without a configured database and with a freshly initialized disposable database. It reported the expected unavailable and setup screens, locked shell, and 19 planned navigation targets.
- The WPF integration test verified the setup, recovery-code actions, login gate and logout against the actual controls. It did not open native dialogs, submit a print job or save actual codes.
- Database tests use disposable fixtures. The compatibility fixture uses the original migration, preserves a synthetic Company row across setup and verifies the plaintext SQLite file header.

Manual acceptance remains for keyboard and screen-reader behavior, visual layout, the native Save dialog and text output, and printer selection and output. Test those operations only with disposable development codes.

The database remains plaintext and local file access can bypass application authorization. Stage 4 employee and permissions administration and later billing, encryption, backup and machine-wide coordination remain outside this stage. AI-Harness, Windows settings and Git state were not intentionally changed.

## File inventory

All paths are relative to `D:\Projects\ai-gtx-inventorysystem\Billing-System`.

Created:

```text
docs/stage3-authentication.md
docs/stage3-results.md
src/GTS.Billing.Application/Authentication.cs
src/GTS.Billing.Application/RecoverySecrets.cs
src/GTS.Billing.Infrastructure/Argon2PasswordHasher.cs
src/GTS.Billing.Infrastructure/AuthenticationStore.cs
src/GTS.Billing.Presentation.Wpf/AuthenticationView.xaml
src/GTS.Billing.Presentation.Wpf/AuthenticationView.xaml.cs
src/GTS.Billing.Presentation.Wpf/AuthenticationViewModel.cs
src/GTS.Billing.Printing/RecoveryCodeDocument.cs
tests/GTS.Billing.Tests/AuthenticationTests.cs
```

Modified:

```text
Directory.Packages.props
README.md
src/GTS.Billing.Infrastructure/DevelopmentDatabase.cs
src/GTS.Billing.Infrastructure/GTS.Billing.Infrastructure.csproj
src/GTS.Billing.Presentation.Wpf/App.xaml.cs
src/GTS.Billing.Presentation.Wpf/MainWindow.xaml
src/GTS.Billing.Presentation.Wpf/MainWindow.xaml.cs
src/GTS.Billing.Presentation.Wpf/Navigation.cs
src/GTS.Billing.Printing/GTS.Billing.Printing.csproj
```

Build, test, logs and synthetic database outputs remain ignored development artifacts.
