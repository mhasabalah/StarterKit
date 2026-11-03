using Shared.Core.Data;

namespace Shared.Core.Behaviors;

/// <summary>
/// Publishes the domain events collected by <c>DispatchDomainEventsInterceptor</c> after the command handler - and
/// every nested command it invokes via <c>ISender.Send</c> - has fully completed. Publishing itself is done by
/// <see cref="IDomainEventDispatcher"/> (the same implementation background jobs use via <see cref="IUnitOfWorkRunner"/>).
///
/// Why not dispatch inside the EF interceptor:
///   The interceptor fires once per SaveChanges. A handler that saves three times would make subscribers see
///   partially-committed state twice. This behavior publishes once per ROOT command, after every save in the call
///   tree has committed.
///
/// Nesting - the depth counter:
///   A nested ISender.Send also passes through this behavior. Only the outermost command (depth == 1) dispatches;
///   inner commands only increment/decrement the counter.
///
/// Failure:
///   If the root command throws, every event collected in its unit of work is discarded, so nothing leaks into the
///   next command that runs in the same DI scope. Events of failed saves and rolled-back transactions never reach the
///   collector's "ready" list in the first place.
/// </summary>
public class DispatchDomainEventsBehavior<TRequest, TResponse>(
    IDomainEventCollector collector,
    IDomainEventDispatcher dispatcher)
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

            // Only the outermost command dispatches - nested commands skip.
            if (depth == 1)
            {
                await dispatcher.DispatchPendingAsync(cancellationToken);
            }

            return response;
        }
        catch
        {
            if (depth == 1)
            {
                collector.Clear();
            }

            throw;
        }
        finally
        {
            collector.DecrementDepth();
        }
    }
}
