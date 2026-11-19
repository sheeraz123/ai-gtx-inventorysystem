# Stage 4F settings and configuration

## Architecture and editing

The application defines typed keys, allowed values, validation and built-in defaults in `SettingsRegistry.cs`. `SettingsLabels.cs` lists the editable titles, menu captions, dashboard text, field labels, grid headings and invoice captions. The settings service resolves built-in, application, current company and eligible personal values in that order. SQLite stores only overrides. Query and update operations use the existing authenticated session, current-company authorization and application lifetime gate.

Open **Application Settings** from the shell. Select a section and available scope, edit values, then use **Save** or **Cancel**. **Restore Defaults** removes the relevant overrides so inherited values resolve again. The editor requests confirmation for resets and warns about unsaved changes. Labels can be searched by typing at least three characters. Display settings refresh the open shell after saving. A right to change one company setting does not imply a right to manage bank, branding, or security records.

| Area | Behavior |
| --- | --- |
| General and personal | Application title, subtitle, menu and dashboard labels, theme, density, font size and regional formatting. English is the available UI language. |
| Masters and grids | Configurable column headings and layout, visible-page sorting, page size and search delay. Search still requires three effective characters. Populated-row double-click retains read-only View mode. |
| Sales and product | Default payment terms, credit and PO preferences for authorized customer edits, plus product defaults, search fields and configurable catalog field visibility. Stored invoice and stock workflow preferences await their corresponding operational modules. |
| Branding and payments | Uses Company Master logos, signature, terms, GST defaults and bank records. Settings control their presentation. The existing default bank and GST selection supply invoice snapshot details. |
| Invoice presentation | Captures display options alongside the resolved seller, branding and bank data in an immutable application contract. Required fields remain visible. The preview summarizes the captured choices. |
| Printing | A4 options and an installed-printer check are available. The stage does not send a print job, render a production invoice, or enforce stock rules. |
| System and security | Operational information remains read-only. Credentials, database paths, installation identity and security controls have no editable setting keys. |

## Authorization, audit and SQLite

Application-wide edits require Super Admin. Company settings are filtered to the authorized current company and gated by the relevant permission. Users may edit their own eligible preferences. Company Master remains authoritative for bank, GST, branding and terms records. Multi-setting saves and resets are transactional and check the versions read by the editor. The existing security audit captures the actor, setting, scope and action. Free-text values are omitted from audit details. Safe enumerated and numeric changes can be recorded.

`20261119000700_ApplicationSettings` adds the override table with scope ownership checks, restrictive foreign keys and filtered unique indexes for application, company and user values. Earlier migrations are preserved. The new migration was reviewed as source and was not applied or added to the automatic upgrade list. Existing databases retain their data and require the confirmed backup-and-update workflow before this build can open them.

## Verification

Commands were run from Billing-System using the existing local package cache and isolated build artifacts.

```powershell
dotnet restore GTS.Billing.slnx
dotnet build GTS.Billing.slnx -c Debug
dotnet build GTS.Billing.slnx -c Release
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
```

Restore succeeded. Debug and Release each finished with zero warnings and zero errors. The WPF process opened a startup-stopped dialog on the Windows desktop and exited with code 1 after the dialog was acknowledged. The dialog reported that the database needs a reviewed update and that no replacement database was created. The shell and Settings screen were therefore unreachable. Visual behavior, persisted settings, printer checks and migration execution remain unverified. No automated tests were created, changed, removed or run. AI-Harness, real business data, Windows provisioning and Git history were left untouched.

## File inventory

Paths below are relative to Billing-System.

| Project or folder | Created |
| --- | --- |
| docs | `stage4f-settings-configuration.md` |
| src/GTS.Billing.Application | `ApplicationSettings.cs`, `SettingsRegistry.cs`, `SettingsLabels.cs` |
| src/GTS.Billing.Domain | `SettingOverride.cs` |
| src/GTS.Billing.Infrastructure | `SettingsStore.cs`, `SettingsSchema.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `20261119000700_ApplicationSettings.cs`, `20261119000700_ApplicationSettings.Designer.cs` |
| src/GTS.Billing.Printing | `InvoicePrintPreferences.cs` |
| src/GTS.Billing.Presentation.Wpf | `SettingsDisplay.cs`, `SettingsView.xaml`, `SettingsView.xaml.cs`, `SettingsViewModel.cs` |

| Project or folder | Modified |
| --- | --- |
| src/GTS.Billing.Application | `Companies.cs`, `CompanyPresentation.cs`, `Customers.cs`, `PermissionCatalog.cs`, `ProductCatalog.cs`, `SecurityAdministration.cs` |
| src/GTS.Billing.Infrastructure | `BillingDbContext.cs`, `CompanyPresentationStore.cs`, `CompanyStore.cs`, `CustomerStore.cs`, `ProductCatalogStore.cs`, `SecurityAdministrationStore.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `BillingDbContextModelSnapshot.cs` |
| src/GTS.Billing.Presentation.Wpf | `App.xaml`, `App.xaml.cs`, `CatalogView.xaml`, `CatalogViewModel.cs`, `CompanyPresentationView.xaml`, `CompanyView.xaml`, `CompanyViewModel.cs`, `CustomerEditorModels.cs`, `CustomerView.xaml`, `CustomerViewModel.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MasterGridInteraction.cs`, `Navigation.cs`, `SecurityAdministrationView.xaml`, `SecurityAdministrationViewModel.cs` |
