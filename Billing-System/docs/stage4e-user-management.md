# Stage 4E — User Management, Roles and Permissions

In the WPF application, open **Administration → User Management**. The screen contains **Users**, **Roles & Permissions**, and **Security Audit**. The special Super Admin sees all three. Other users see only the granted read-only administration sections for their active current company.

## User identity and passwords

The existing Users table remains the only credential store. Username comparison uses the existing normalized unique index and usernames stay fixed after creation. New ordinary users require a full name. The editor also supports display name, contact fields, employee code, designation and department. Existing accounts without historical profile fields display their usernames or an unrecorded value. The Argon2id hash retains its existing version and unique salt. No new hashing package or algorithm is used.

Create ordinary accounts with a temporary passphrase entered twice in masked fields. The user must replace it before entering the shell. **Change password** requires verification of the current password and a different compliant passphrase. A successful change invalidates the session and returns to sign-in. Administrative reset is available for ordinary users only and always requires a change on next sign-in. Password input is excluded from JSON and diagnostics, and cleared from the input controls after submission.

Administrative lock and inactivity are independent. Unlocking leaves inactivity and any password-change requirement in place. The existing installation-wide login throttle still blocks further attempts after five failures in the preceding 30 seconds. The user details identify this shared throttle separately from the administrative lock. Successful login and password-change times are tracked.

The Super Admin remains a permanent singleton assignment separate from ordinary roles. First-run setup, ten one-time recovery codes, regeneration, transactional recovery and the existing Argon2id verifier are preserved. The user editor cannot modify the Super Admin account or reset its password. The new own-password change requires the current Super Admin password and leaves unused recovery codes intact. Database guards protect the singleton assignment and Super Admin status from ordinary edits.

## Role and company scope

The application reuses Users, Roles, Modules, RolePermissions, UserRoles, and UserPermissionOverrides. New UserCompanyAccess rows explicitly grant a user access to a company. UserRoles already includes CompanyId, so a user can have different roles in each company. A grant requires an active company, active access, an assignment in that company, and an active role. Existing explicit denials take precedence. Existing positive overrides alone confer no permission. Legacy role rows are retained. The upgrade grants no company access, enables no legacy permission on its own, and creates no accounts or roles.

Role templates are installation-wide. Their permissions take effect only in the companies where assigned. Deactivating a role leaves its assignments intact. User creation, editing, locking, status, reset, role mutation and role/company assignment are reserved for the special Super Admin. There is no business role that can grant itself those operations or the singleton identity. User, Role and Security Audit read permissions can be assigned to company-scoped roles. Ordinary user and role lists show only records associated with their active current company. The company chooser shows assigned active companies. Viewing company details requires Company.View.

The permission matrix is drawn from the source catalog and grouped by module. It maps stable permission codes onto the original table's module/action representation. On the first explicit grant for a module, its missing module metadata is inserted in the same transaction. No privileged accounts, roles, or grants are seeded. User assignments are edited from the user record's company and roles section. Role details list members and their assigned companies. Status, temporary-password requirement, company grants, and role membership are saved together in the user editor.

Company, Customer, Category and Product operations query current authorization in their SQLite transaction. Business grant codes protect view and ordinary mutations, with separate status, credit, bank, branding, invoice-default and pricing permissions. A company membership without a business permission grants only the company chooser. Customer bank and internal notes are redacted when the corresponding read permission is missing. UI menus and sensitive controls reflect the loaded permission snapshot. Server-side checks remain authoritative if a view is stale. Role changes and company access changes take effect on the next protected call. User changes and resets advance the existing account version; stale or locked identities are rejected.

## Audit and workflow

User changes, status/lock changes, resets, required changes, role changes, role grants and removals, permission grants and removals, and company grants and revocations are audited in the existing AuditEvents table. Password change and login events use the existing installation audit flow. The audit UI filters by date, event, actor, target user and outcome. Ordinary audit readers see company-scoped events only; installation-wide security events are visible to Super Admin. Neither audit records nor screens expose hashes, recovery codes, plaintext passwords or raw audit JSON.

In Users or Roles, double-click an actual data row to load fresh persisted details in View mode. Choose Edit explicitly to update a record. View mode does not show Save. State-changing saves display a confirmation. Editors guard unsaved changes and disable duplicate submissions. Grids are paged and use the shared 300 ms debounce after at least three trimmed characters; clearing text restores the default results while status and company filters continue to apply.

The machine-wide mutex, installation and path verification, existing session gate, operation admission and draining, and Logout and Exit remain in use. No database engine, password library, or other package was added.

## Migration and verification scope

Migration `20261119000600_SecurityAdministration` adds ordinary-user metadata and security flags, role metadata and a unique case-insensitive code, and a new UserCompanyAccess table with restrictive foreign keys. It attaches protection triggers without deleting or rebuilding existing tables. The only existing-row backfill sets newly added role codes using their stable IDs. The previous schema migrations, existing credentials, installation marker, and database upgrade allowlist are unchanged by this stage.

Automatic tests and any functional security acceptance tests are deferred. The build compiles the existing test project as part of the solution without running it. The local database remains plaintext and is restricted to synthetic data. Migration execution and end-to-end sign-in, permission, and edit workflows require an authorized development verification after the upgrade.

## File inventory

Paths are relative to Billing-System.

Created

- `docs/stage4e-user-management.md`
- `src/GTS.Billing.Application/PermissionCatalog.cs`
- `src/GTS.Billing.Application/SecurityAdministration.cs`
- `src/GTS.Billing.Domain/UserCompanyAccess.cs`
- `src/GTS.Billing.Infrastructure/Migrations/20261119000600_SecurityAdministration.Designer.cs`
- `src/GTS.Billing.Infrastructure/Migrations/20261119000600_SecurityAdministration.cs`
- `src/GTS.Billing.Infrastructure/Migrations/SecurityAdministration.sql`
- `src/GTS.Billing.Infrastructure/ScopedAccess.cs`
- `src/GTS.Billing.Infrastructure/SecurityAdministrationStore.cs`
- `src/GTS.Billing.Infrastructure/SecuritySchema.cs`
- `src/GTS.Billing.Presentation.Wpf/SecurityAdministrationView.xaml`
- `src/GTS.Billing.Presentation.Wpf/SecurityAdministrationView.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/SecurityAdministrationViewModel.cs`

Modified

- `src/GTS.Billing.Application/Authentication.cs`
- `src/GTS.Billing.Application/Companies.cs`
- `src/GTS.Billing.Application/CompanyPresentation.cs`
- `src/GTS.Billing.Application/Customers.cs`
- `src/GTS.Billing.Application/ProductCatalog.cs`
- `src/GTS.Billing.Domain/Entities.cs`
- `src/GTS.Billing.Infrastructure/BillingDbContext.cs`
- `src/GTS.Billing.Infrastructure/CompanyPresentationStore.cs`
- `src/GTS.Billing.Infrastructure/CompanyStore.cs`
- `src/GTS.Billing.Infrastructure/CustomerStore.cs`
- `src/GTS.Billing.Infrastructure/Migrations/BillingDbContextModelSnapshot.cs`
- `src/GTS.Billing.Infrastructure/ProductCatalogStore.cs`
- `src/GTS.Billing.Presentation.Wpf/App.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/AuthenticationView.xaml`
- `src/GTS.Billing.Presentation.Wpf/AuthenticationView.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/AuthenticationViewModel.cs`
- `src/GTS.Billing.Presentation.Wpf/CatalogView.xaml`
- `src/GTS.Billing.Presentation.Wpf/CatalogViewModel.cs`
- `src/GTS.Billing.Presentation.Wpf/CompanyPresentationView.xaml`
- `src/GTS.Billing.Presentation.Wpf/CompanyPresentationViewModel.cs`
- `src/GTS.Billing.Presentation.Wpf/CompanyViewModel.cs`
- `src/GTS.Billing.Presentation.Wpf/CustomerEditorModels.cs`
- `src/GTS.Billing.Presentation.Wpf/CustomerView.xaml`
- `src/GTS.Billing.Presentation.Wpf/CustomerViewModel.cs`
- `src/GTS.Billing.Presentation.Wpf/MainWindow.xaml`
- `src/GTS.Billing.Presentation.Wpf/MainWindow.xaml.cs`
- `src/GTS.Billing.Presentation.Wpf/Navigation.cs`

An ignored EF design project (MigrationDesign.csproj and Program.cs) and model-source outputs were created under `.local/stage4e-design` and `.local/stage4e-migration-source`. It generated EF metadata and schema text without opening a database. Debug and Release outputs are under `.local/stage4e-artifacts`.

## Build and launch evidence

Run from Billing-System with process-local ArtifactsPath set to .local/stage4e-artifacts, cached dependencies and NuGet auditing disabled for the offline restore invocation.

| Command | Final result |
|---|---|
| dotnet restore GTS.Billing.slnx | Passed |
| dotnet build GTS.Billing.slnx -c Debug | Passed · 0 warnings · 0 errors |
| dotnet build GTS.Billing.slnx -c Release | Passed · 0 warnings · 0 errors |

~~~powershell
$env:ArtifactsPath = Join-Path $PWD '.local/stage4e-artifacts'
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
~~~

The Debug process started on the interactive desktop. The last visible screen was **GTS Billing — startup stopped**. It reported:

> This database needs a reviewed update for this version of GTS Billing. Use the confirmed backup-and-update workflow; do not delete the database. No replacement database was created.

The dialog was dismissed through the UI. The process exited normally for a rejected startup with code 1. Sign-in and User Management were not reached. The next step is to approve the Stage 4E migration for the synthetic development database, then use the existing verified backup-and-update flow.

No automated tests were added, changed, deleted, or executed. The installed database and installation marker were not updated. AI-Harness, Windows provisioning, and Git history were untouched.
