# Stage 4I Financial Year and Document Series

## Financial Year Master

Select an active company, then open **Master → Financial Year Master**. Add the year code, name and inclusive calendar boundaries. The editor suggests April through March for a new record and permits different dates. Overlaps and duplicate codes within a company are rejected. A year starts open. Explicitly set an open year as current. The application clears the previous selection in the same transaction and reports missing setup when there is no current year. Switching companies clears the previous company context.

Use **Close year** to close a financial year administratively. The workflow confirms the action, records the actor and UTC timestamp, and clears the current flag if needed. It does not perform an accounting close. Reopen requires its own permission and a reason. Neither action resets sequence state. Date changes are blocked once the year is closed or referenced by a series, counter or ledger entry. View mode includes associated series and audit timestamps.

## Document Series Master

Select **Master → Document Series Master**. A series belongs to the selected company, one year, one GST registration and one document type. Multiple named series can coexist within that scope. Company, year, GST registration, document type and series code are fixed after creation. Setting a default replaces the previous default atomically. A default must be active, use an active registration and belong to an open year. Deactivation clears the default flag. Financial year closure preserves series configuration and blocks new counter advances and number ledger inserts. Examples of user-defined business labels include Footwear Sales, Wholesale and Sports Equipment. No series, formats or financial years are seeded.

The document-type catalog uses stable internal codes and retains the existing `TaxInvoice` identity. Types are available for configuration of future workflows. A type entry does not enable any transaction workflow. A series requires an active Company Master GST registration when it is created or activated.

The number is the expanded prefix, optional join separator, padded sequence, then the optional separator and expanded suffix. Templates support the literal tokens `{Company}`, `{Year}`, and `{Series}`; the sequential number is inserted between the two template parts. Literal output is restricted to ASCII letters, digits, slash and hyphen, following the existing ledger's 16-character limit. Padding is 1 through 9. A sequence cannot exceed nine digits or the space remaining in the rendered format. Potential structural collisions are rejected across document types within the same GST registration and year. The check is conservative and may reject overlapping shapes even if their configured starting sequences differ. A peer legacy format that cannot be interpreted safely is reported as a conflict for new configuration in that namespace.

**Preview number** validates the format and shows the stored next value or the configured starting value. It creates no counter, reservation, ledger entry or invoice. The editor never provides a reset action. Starting values and formatting lock once a counter or ledger entry exists.

Both grids use the shared three-character search, default 300 ms debounce, status filters, stale-result protection and read-only double-click View. Editors warn about unsaved changes and disable duplicate submissions. Stage 4F settings supply titles and display captions. Security policy, document identities and validation limits are defined in code.

## Future number finalization

The finalization request carries document ID/date, document type, year, GST registration and optional series. No implementation is registered at this stage because there is no durable invoice finalizer. Future code must independently verify authenticated company scope, document eligibility, year/date, GST registration, active/default series and the rendered number. It must write the counter, permanent ledger and durable document association in one SQLite transaction. Resolve a missing default explicitly. A retry must reconcile the existing durable association by document identity before attempting another allocation. It must never rewind a counter or recycle a cancelled number.

Existing sequence and rendered-number uniqueness protections remain. An added case-insensitive rendered-number index uses company, GST registration and financial year. Existing ledger and snapshot immutability triggers remain. Counter triggers prevent deletion, backward movement and advances for closed or inactive series. Ledger insertion validates the year and series association. Closing a year is administrative. This implementation makes no promise of statutory or gapless numbering.

## Migration and preservation

Migration `20261119001000_FinancialYearNumbering` replaces the April-only `CK_FY_Boundaries` check with an ordered date check and replaces the old one-series-per-registration/type index with company/year/registration/type/code uniqueness. It adds partial unique indexes for a current open year and an active default series. Overlap, identity and counter/ledger triggers enforce the new rules in SQLite. Only the two configuration tables are rebuilt. The migration also ensures the financial-year and document-series permission catalog rows exist.

| Legacy data | Treatment |
| --- | --- |
| Financial years | Copy original IDs, company, start/end, state and version. Derive the code and name from the dates. No current year or historical audit actor is invented. |
| Series | Copy original IDs, company, GST registration, type, prefix, starting number, padding and version. A single evidenced year is required across the existing counters and ledger. Stable legacy codes are derived from IDs. No default is assigned. |
| Counters | Leave table and rows untouched. Retain IDs, year, series and next values. |
| Ledger | Leave rows and existing permanent-history triggers untouched. Preserve all existing statuses and numbers. No reservation subsystem is introduced. |
| Invoice snapshots and assets | Leave records, references and original trigger definitions untouched. |
| Audit | Leave historical rows untouched. Audit future configuration actions with actor, company, time, target and reopening reason when applicable. |

Read-only inspection of the configured synthetic database found zero years, series, counters, ledger entries and snapshots. The migration also checks every legacy series at run time and aborts before changing tables if no year or more than one year is evidenced. Existing overlaps, case-insensitive ledger conflicts or invalid foreign-key relationships also stop the migration. No mapping guesses, renumbering or cleanup are performed.

### Controlled SQLite rebuild

The migration is outside the reviewed startup allowlist. A future approved run must first use the installed application's mutex, verify the synthetic database and its history, create and verify a safety backup, and put the installation into maintenance state. The source includes a dedicated route for this **one pending migration only**. On the existing non-pooled maintenance connection, the route temporarily turns connection-local foreign-key enforcement off before EF begins its transaction. Current application runtime connections continue to enforce foreign keys.

The migration refuses to begin its DDL when the dedicated connection precondition is missing. It copies the two parent tables, verifies every legacy column and row count, swaps the tables, recreates constraints and verifies foreign keys and database integrity before EF commits. The same EF transaction includes the migration history insert. Any SQL guard or DDL failure before that commit rolls back the rebuild. A finally block restores and verifies foreign-key enforcement on the maintenance connection. The surrounding workflow then compares prior row fingerprints against the backup and verifies final installation state before returning Ready. The only allowed existing-table additions are the reviewed permission catalog entries. If a failure occurs after commit and before final verification, the installation remains in maintenance. Recovery requires review against the verified backup. Down migration is unsupported. The usual first-time initializer does not enable this guarded rebuild. A reviewed fresh-install path will be needed before using this schema for a new installation. [SQLite table-change procedure](https://sqlite.org/lang_altertable.html)

## Authorization and file inventory

Service boundaries validate live session, active selected company and current RBAC. Financial-year rights include View, Create, Edit, SetCurrent, Close and a dedicated Reopen right. Series rights include View, Create, Edit, SetDefault and StatusChange. Existing installation identity, path validation, machine-wide ownership and shutdown coordination remain in use.

Paths are relative to Billing-System.

| Folder | Created |
| --- | --- |
| src/GTS.Billing.Application | `DocumentNumbering.cs` |
| src/GTS.Billing.Infrastructure | `NumberingSchema.cs`, `NumberingStore.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `20261119001000_FinancialYearNumbering.cs`, `20261119001000_FinancialYearNumbering.Designer.cs` |
| src/GTS.Billing.Presentation.Wpf | `NumberingView.xaml`, `NumberingView.xaml.cs`, `NumberingViewModel.cs` |
| docs | `stage4i-financial-year-document-series.md` |

| Folder | Modified |
| --- | --- |
| src/GTS.Billing.Domain | `Entities.cs` |
| src/GTS.Billing.Application | `PermissionCatalog.cs`, `SettingsLabels.cs` |
| src/GTS.Billing.Infrastructure | `BillingDbContext.cs`, `DevelopmentDatabaseMaintenance.cs` |
| src/GTS.Billing.Infrastructure/Migrations | `BillingDbContextModelSnapshot.cs` |
| src/GTS.Billing.Presentation.Wpf | `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Navigation.cs` |

The read-only inspection utility and EF metadata generation ran from ignored local directories. No previous migration was changed and no installed database was migrated.

## Verification

From Billing-System, using existing packages and isolated build artifacts:

```powershell
dotnet restore GTS.Billing.slnx
dotnet build GTS.Billing.slnx -c Debug
dotnet build GTS.Billing.slnx -c Release
dotnet run --no-build --configuration Debug --project "src\GTS.Billing.Presentation.Wpf\GTS.Billing.Presentation.Wpf.csproj"
```

Restore succeeded. Debug and Release each passed with zero warnings and zero errors. The Debug WPF process displayed **GTS Billing — startup stopped** on the Windows desktop and exited with code 1 after the dialog was dismissed. The dialog reported that the database needs a reviewed update and that no replacement database was created. The signed-in menus were verified in source. Their live behavior and the migration transaction remain unverified. Executing the backup-and-update workflow for the installed synthetic database requires separate approval.

No automated tests were created, changed, deleted or run. AI-Harness, installed databases, Windows provisioning and Git history were untouched.
