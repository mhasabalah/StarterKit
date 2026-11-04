namespace Shared.Core.Data;

/// <summary>
/// THE way background code (Hangfire jobs, hosted services, orchestrators) does database work.
/// <para>
/// Each <see cref="RunAsync"/> call is one unit of work: a fresh DI scope (own DbContexts, change tracker and
/// <see cref="IDomainEventCollector"/>), the work, then - only if the work succeeded - every domain event of the
/// committed saves is published via <see cref="IDomainEventDispatcher"/>. If the work throws, its events are
/// discarded and the exception propagates.
/// </para>
/// <para>
/// Use one call per item in batch jobs, so one failure (e.g. a concurrency conflict) never poisons the change tracker
/// or the events of the next item. Work that must publish committed events even if a LATER step fails can call
/// <see cref="IDomainEventDispatcher.DispatchPendingAsync"/> (resolved from the scope's provider) as a checkpoint.
/// </para>
/// <example><code>
/// await runner.RunAsync(async (services, ct) =>
/// {
///     var repository = services.GetRequiredService&lt;ISubscriptionRepository&gt;();
///     var subscription = await repository.GetByIdAsync(id, ct);
///     subscription!.Expire(now);
///     await repository.SaveChangesAsync(ct);
/// }, cancellationToken);   // SubscriptionExpiredEvent handlers run here, after the commit
/// </code></example>
/// mediator commands sent inside the work (<c>ISender.Send</c>) do not dispatch on their own: the runner is the root
/// unit of work and dispatches everything once at the end.
/// </summary>
public interface IUnitOfWorkRunner
{
    Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default);

    Task<TResult> RunAsync<TResult>(
        Func<IServiceProvider, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default);
}

public sealed class UnitOfWorkRunner(IServiceScopeFactory scopeFactory) : IUnitOfWorkRunner
{
    public Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return RunAsync<bool>(async (services, ct) =>
        {
            await work(services, ct);
            return true;
        }, cancellationToken);
    }

    public async Task<TResult> RunAsync<TResult>(
        Func<IServiceProvider, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var collector = services.GetRequiredService<IDomainEventCollector>();

        TResult result;
        collector.IncrementDepth();
        try
        {
            result = await work(services, cancellationToken);
        }
        catch
        {
            collector.Clear();
            throw;
        }
        finally
        {
            collector.DecrementDepth();
        }

        await services.GetRequiredService<IDomainEventDispatcher>().DispatchPendingAsync(cancellationToken);
        return result;
    }
}
