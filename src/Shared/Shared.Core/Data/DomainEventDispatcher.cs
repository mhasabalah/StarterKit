namespace Shared.Core.Data;

/// <summary>
/// Publishes (via the mediator <see cref="IPublisher"/>) the domain events of COMMITTED work collected in the current DI
/// scope. The single implementation used by both <c>DispatchDomainEventsBehavior</c> (HTTP / mediator commands) and
/// <see cref="IUnitOfWorkRunner"/> (Hangfire jobs, hosted services).
/// <para>
/// Call it directly only from code that runs outside the mediator in a scope you did not create (e.g. a Hangfire job
/// whose dependencies Hangfire injected): <c>await repository.SaveChangesAsync(ct); await dispatcher.DispatchPendingAsync(ct);</c>.
/// Code that creates scopes should use <see cref="IUnitOfWorkRunner"/> instead.
/// </para>
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Drains the scope's <see cref="IDomainEventCollector"/> and publishes every committed event. Loops until the
    /// collector is empty, so events raised (and committed) by event handlers are published in the same call.
    /// Events of failed saves / rolled-back transactions were already discarded and are never published.
    /// </summary>
    Task DispatchPendingAsync(CancellationToken cancellationToken = default);
}

public sealed class DomainEventDispatcher(
    IDomainEventCollector collector,
    IPublisher publisher,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    /// <summary>Guard against handlers that keep raising events that raise events (a cycle).</summary>
    public const int MaxRounds = 16;

    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        // Raise the depth while publishing: a handler that calls ISender.Send must not dispatch on its own
        // (that would re-enter this drain); its events are picked up by the next round of this loop.
        collector.IncrementDepth();
        try
        {
            for (var round = 1; ; round++)
            {
                var events = collector.DequeueAll();
                if (events.Count == 0)
                    return;

                if (round > MaxRounds)
                {
                    logger.LogError(
                        "Domain event dispatch did not settle after {Rounds} rounds; {Count} events dropped ({Types}).",
                        MaxRounds, events.Count, string.Join(", ", events.Select(e => e.GetType().Name).Distinct()));
                    throw new InvalidOperationException(
                        $"Domain event dispatch did not settle after {MaxRounds} rounds - an event handler cycle?");
                }

                foreach (var domainEvent in events)
                    await publisher.Publish(domainEvent, cancellationToken);
            }
        }
        finally
        {
            collector.DecrementDepth();
        }
    }
}
