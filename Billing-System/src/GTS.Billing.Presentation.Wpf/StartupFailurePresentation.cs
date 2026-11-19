using System.IO;
using System.Text.Json;
using GTS.Billing.Infrastructure;
using Microsoft.Data.Sqlite;

namespace GTS.Billing.Presentation.Wpf;

public sealed record StartupFailureNotice(string Code,string Message);
public static class StartupFailurePresentation
{
    public static StartupFailureNotice Describe(Exception error,string stage)
    {
        const string safe=" No replacement database was created.";
        if(error is DatabaseStartupException database)
            return database.Problem switch
            {
                DatabaseStartupProblem.UpgradeRequired=>new("startup-schema-upgrade","This database needs a reviewed update for this version of GTS Billing. Use the confirmed backup-and-update workflow; do not delete the database."+safe),
                DatabaseStartupProblem.ApplicationTooOld=>new("startup-version-newer","This copy of GTS Billing is older than the database. Start the latest application build. Do not replace or downgrade the database."+safe),
                DatabaseStartupProblem.MissingDatabase=>new("startup-database-missing","The configured database file is missing. Restore or locate the expected installation; first-time initialization will not run automatically."+safe),
                DatabaseStartupProblem.MaintenanceInProgress=>new("startup-marker","A previous database update did not finish verification. The installation remains in maintenance mode. Keep the safety backup and obtain reviewed recovery; do not delete or reset its marker."+safe),
                DatabaseStartupProblem.InvalidMarker=>new("startup-marker","The database installation marker is missing, invalid or not ready. Check the expected installation; do not recreate the database."+safe),
                DatabaseStartupProblem.IntegrityFailure=>new("startup-integrity","Database integrity or foreign-key verification failed. Keep the database and backups for recovery."+safe),
                DatabaseStartupProblem.IdentityMismatch=>new("startup-database-identity","The database does not match its installation identity or development mode. Restore the matching database and marker."+safe),
                _=>new("startup-history","The database migration history is not recognized by this build. No update or downgrade was attempted."+safe)
            };
        if(error is UnauthorizedAccessException or System.Security.SecurityException)
            return new("startup-access-denied","Windows denied access during "+stage+". Check the authorized account and installation permissions."+safe);
        if(error is JsonException || stage=="configuration")
            return new("startup-configuration","The application configuration could not be read or is invalid. Check the deployed appsettings.json and configured development path."+safe);
        if(error is IOException or SqliteException)
            return new("startup-storage","The configured database or storage could not be accessed during "+stage+". Check its availability and any active maintenance operation."+safe);
        return new("startup-failed","GTS Billing could not complete "+stage+". No application session was opened."+safe);
    }
}

