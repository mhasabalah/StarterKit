namespace Shared.Core.Data.Interceptors;

/// <summary>
/// Collects domain events from the EF ChangeTracker into the scoped
/// IDomainEventCollector during SavingChanges (while entities are still
/// tracked with their events attached).
///
/// Dispatching is intentionally NOT done here.  The MediatR pipeline
/// behavior (DispatchDomainEventsBehavior) dispatches all collected events
/// after the entire command handler — and all its nested SaveChanges calls —
/// have completed.  This guarantees:
///
///   1. Every event handler sees fully committed data.
///   2. Multiple SaveChanges calls in one command batch their events and
///      publish once, not once per save.
///   3. No re-entrant SaveChanges on the same DbContext.
/// </summary>
public class DispatchDomainEventsInterceptor(IDomainEventCollector collector)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CollectAndClear(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CollectAndClear(eventData.Context);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void CollectAndClear(DbContext? context)
    {
        if (context is null) return;

        var aggregates = context.ChangeTracker
            .Entries<IAggregate>()
            .Where(a => a.Entity.DomainEvents.Any())
            .Select(a => a.Entity)
            .ToList();

        var events = aggregates
            .SelectMany(a => a.DomainEvents)
            .Cast<INotification>()
            .ToList();

        aggregates.ForEach(a => a.ClearDomainEvents());

        collector.Collect(events);
    }
}
