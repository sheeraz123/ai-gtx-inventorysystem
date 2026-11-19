namespace GTS.Billing.Infrastructure;

public enum DatabaseStartupProblem
{
    MissingDatabase, InvalidMarker, MaintenanceInProgress, UpgradeRequired,
    ApplicationTooOld, UnrecognizedHistory, IntegrityFailure, IdentityMismatch
}

public sealed class DatabaseStartupException(DatabaseStartupProblem problem, IReadOnlyList<string>? pending = null)
    : InvalidOperationException("Development database startup check: " + problem)
{
    public DatabaseStartupProblem Problem { get; } = problem;
    public IReadOnlyList<string> PendingMigrations { get; } = pending ?? [];

    internal static DatabaseStartupException? CompareHistory(string[] expected, string[] applied)
    {
        if (expected.SequenceEqual(applied)) return null;
        if (applied.Length > expected.Length && applied.Take(expected.Length).SequenceEqual(expected))
            return new(DatabaseStartupProblem.ApplicationTooOld);
        if (applied.Length > 0 && applied.Length < expected.Length && expected.Take(applied.Length).SequenceEqual(applied))
            return new(DatabaseStartupProblem.UpgradeRequired, expected.Skip(applied.Length).ToArray());
        return new(DatabaseStartupProblem.UnrecognizedHistory);
    }
}
