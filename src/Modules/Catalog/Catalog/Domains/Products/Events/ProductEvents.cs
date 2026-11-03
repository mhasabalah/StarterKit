namespace Catalog.Domains.Products.Events;

// Events carry ids + a minimal projection — never the entity itself — so handlers cannot depend on
// (or mutate) internal aggregate state. They are dispatched by DispatchDomainEventsBehavior after the
// command (and all its SaveChanges calls) has committed.

public record ProductCreatedEvent(Guid ProductId, Guid OwnerId, string Sku) : IDomainEvent;

public record ProductUpdatedEvent(Guid ProductId) : IDomainEvent;

/// <summary>Soft delete. Attachments are kept so the product can be restored.</summary>
public record ProductDeletedEvent(Guid ProductId) : IDomainEvent;

public record ProductRestoredEvent(Guid ProductId) : IDomainEvent;

/// <summary>Hard delete (purge job). Handlers clean up everything the product owned elsewhere.</summary>
public record ProductPurgedEvent(Guid ProductId) : IDomainEvent;

// ── Workflow: one event per transition ──────────────────────────────────────

public record ProductSubmittedForReviewEvent(Guid ProductId, Guid OwnerId) : IDomainEvent;

public record ProductApprovedEvent(Guid ProductId, Guid ReviewerId) : IDomainEvent;

public record ProductRejectedEvent(Guid ProductId, Guid ReviewerId, string Reason) : IDomainEvent;

public record ProductChangesRequestedEvent(Guid ProductId, Guid ReviewerId, string Notes) : IDomainEvent;

public record ProductPublishedEvent(Guid ProductId, Guid OwnerId, string Sku, DateTime PublishedAt) : IDomainEvent;

public record ProductArchivedEvent(Guid ProductId) : IDomainEvent;

public record ProductReturnedToDraftEvent(Guid ProductId) : IDomainEvent;
