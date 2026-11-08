using Shared.Contracts.Messaging;

namespace Shared.Core.DDD;

public interface IAggregate<T> : IEntity<T>, IAggregate
{
}

public interface IAggregate : IEntity
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    IDomainEvent[] ClearDomainEvents();

    /// <summary>Integration events waiting to be written to the outbox on the next SaveChanges.</summary>
    IReadOnlyList<IIntegrationEvent> IntegrationEvents { get; }
    IIntegrationEvent[] ClearIntegrationEvents();

    /// <summary>
    /// True once after a behaviour method asked for a version bump, then false: read by
    /// <c>AuditableEntityInterceptor</c> on save.
    /// </summary>
    bool ConsumeVersionBumpRequest();
}
