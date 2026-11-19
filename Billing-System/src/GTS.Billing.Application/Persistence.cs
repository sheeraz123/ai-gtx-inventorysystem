using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDataTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
public interface IDataTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
// No CRUD use cases are activated. This boundary avoids exposing provider-specific SQL to UI.
public interface ICompanyReader
{
    Task<Company?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}
public interface IDevelopmentDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task VerifyExistingAsync(CancellationToken cancellationToken = default);
}
public interface IDatabaseLocation
{
    string GetAbsolutePath();
}
