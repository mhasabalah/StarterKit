namespace Catalog.Domains.Products.Events;

// Events carry ids + a minimal projection — never the entity itself — so consumers
// cannot depend on (or mutate) internal aggregate state.

public record ProductCreatedEvent(Guid ProductId, string Name, string Sku) : IDomainEvent;

public record ProductUpdatedEvent(
    Guid ProductId,
    string Name,
    string Sku,
    decimal Price,
    bool IsActive) : IDomainEvent;

public record ProductDeletedEvent(Guid ProductId, Guid? ImageAttachmentId) : IDomainEvent;
