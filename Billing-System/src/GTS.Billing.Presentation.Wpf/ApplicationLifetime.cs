namespace GTS.Billing.Presentation.Wpf;

public sealed class ApplicationLifetime : GTS.Billing.Application.IApplicationOperationCoordinator
{
    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? shutdown;
    private int active;
    private bool stopping;
    public bool IsStopping { get { lock (sync) return stopping; } }
    public bool IsWaitingForOperations { get { lock (sync) return stopping && active > 0; } }
    public bool IsStopped { get; private set; }
    public event Action? ShutdownStarted;
    public event Action? ShutdownCompleted;
    public event Action? DrainStateChanged;
    internal Func<Task>? SessionCleanup { private get; set; }
    internal Func<Task>? HostCleanup { private get; set; }

    public async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken caller = default)
    {
        lock (sync)
        {
            if (stopping) throw new OperationCanceledException("Application shutdown has started.");
            active++;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, cancellation.Token);
            await operation(linked.Token);
        }
        finally
        {
            var notify = false;
            lock (sync)
            {
                active--;
                if (stopping && active == 0)
                {
                    drained.TrySetResult();
                    notify = true;
                }
            }
            if (notify) DrainStateChanged?.Invoke();
        }
    }

    public Task ShutdownAsync()
    {
        TaskCompletionSource completion;
        lock (sync)
        {
            if (shutdown is not null) return shutdown;
            stopping = true;
            if (active == 0) drained.TrySetResult();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdown = completion.Task;
        }
        // Run outside the monitor. Completion remains cached so close/logout cannot race.
        _ = FinishAsync(completion);
        return completion.Task;
    }

    private async Task FinishAsync(TaskCompletionSource completion)
    {
        try
        {
            ShutdownStarted?.Invoke();
            DrainStateChanged?.Invoke();
            cancellation.Cancel();
            await drained.Task;
            if (SessionCleanup is not null) await SessionCleanup();
            if (HostCleanup is not null) await HostCleanup();
            IsStopped = true;
            ShutdownCompleted?.Invoke();
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            // Stay in the stopping state. The entry thread retains ownership until process termination.
            SafeDiagnostics.Record("shutdown-drain-failed", "Error");
            completion.TrySetException(error);
        }
    }
}
