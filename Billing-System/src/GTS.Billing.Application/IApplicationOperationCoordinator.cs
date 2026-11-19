namespace GTS.Billing.Application;

// Adapter to the existing process lifetime, not a second coordinator.
public interface IApplicationOperationCoordinator
{
    Task RunAsync(Func<CancellationToken,Task> operation,CancellationToken caller=default);
}
