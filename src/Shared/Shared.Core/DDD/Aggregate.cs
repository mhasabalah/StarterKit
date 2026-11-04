using Shared.Contracts.Messaging;

namespace Shared.Core.DDD;

public class Aggregate<TId> : Entity<TId>, IAggregate<TId>
{
    private readonly List<IDomainEvent> _domainEvents = new();
    private readonly List<IIntegrationEvent> _integrationEvents = new();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public IReadOnlyList<IIntegrationEvent> IntegrationEvents => _integrationEvents.AsReadOnly();

    public IDomainEvent[] ClearDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }

    public IIntegrationEvent[] ClearIntegrationEvents()
    {
        var events = _integrationEvents.ToArray();
        _integrationEvents.Clear();
        return events;
    }

    /// <summary>In-process event, handled by mediator handlers (any module) after the unit of work commits.</summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Cross-module fact, delivered asynchronously and at least once: written to this context's transactional outbox
    /// by <c>OutboxInterceptor</c> in the SAME SaveChanges as the aggregate (so it exists if and only if the change was
    /// committed), then published to the message bus. The context must map an outbox
    /// (<c>builder.AddOutbox()</c> / <c>builder.AddTenantOutbox()</c>) - saving throws otherwise.
    /// </summary>
    protected void AddIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _integrationEvents.Add(integrationEvent);
    }
}
