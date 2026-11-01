using Shared.Core.Data;

namespace Shared.Core.Behaviors;

/// <summary>
/// Dispatches domain events collected by DispatchDomainEventsInterceptor
/// after the command handler — and every nested command it invokes via
/// ISender.Send — has fully completed.
///
/// Why this beats dispatching inside the EF interceptor:
///
///   Interceptor (SavedChanges) fires once per DbContext.SaveChanges call.
///   If a handler calls SaveChanges three times, the interceptor fires three
///   times and subscribers see partially-committed state on the first two.
///
///   This behavior fires exactly once per root command, after all saves across
///   all DbContexts in the entire call tree are committed.  Subscribers always
///   see the final, consistent state.
///
/// Nesting — how the depth counter works:
///
///   When a handler calls ISender.Send(innerCommand), that inner command also
///   passes through this behavior.  The depth counter ensures only the
///   outermost command (depth == 1) dispatches the collected events.  Inner
///   commands increment/decrement the counter but do not publish.  All events
///   from the whole tree are batched and published once when the root exits.
/// </summary>
public class DispatchDomainEventsBehavior<TRequest, TResponse>(
    IDomainEventCollector collector,
    IPublisher publisher)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var depth = collector.IncrementDepth();
        try
        {
            var response = await next();

            // Only the outermost command dispatches — nested commands skip.
            if (depth == 1)
            {
                var events = collector.DequeueAll();
                foreach (var domainEvent in events)
                    await publisher.Publish(domainEvent, cancellationToken);
            }

            return response;
        }
        finally
        {
            collector.DecrementDepth();
        }
    }
}
