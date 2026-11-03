using Catalog.Domains.Products.Events;

namespace Catalog.Domains.Products.EventHandlers;

/// <summary>
/// Sample in-process domain-event handlers (audit-style logging). Real side effects live in their own
/// handlers (see ProductPurgedEventHandler). Several handlers per event is fine.
/// </summary>
public class ProductEventLoggingHandlers(ILogger<ProductEventLoggingHandlers> logger)
    : INotificationHandler<ProductCreatedEvent>,
      INotificationHandler<ProductUpdatedEvent>,
      INotificationHandler<ProductDeletedEvent>,
      INotificationHandler<ProductRestoredEvent>,
      INotificationHandler<ProductSubmittedForReviewEvent>,
      INotificationHandler<ProductApprovedEvent>,
      INotificationHandler<ProductRejectedEvent>,
      INotificationHandler<ProductChangesRequestedEvent>,
      INotificationHandler<ProductArchivedEvent>,
      INotificationHandler<ProductReturnedToDraftEvent>
{
    public Task Handle(ProductCreatedEvent e, CancellationToken ct) =>
        Log("Product {ProductId} created by {OwnerId} with SKU {Sku}", e.ProductId, e.OwnerId, e.Sku);

    public Task Handle(ProductUpdatedEvent e, CancellationToken ct) => Log("Product {ProductId} updated", e.ProductId);

    public Task Handle(ProductDeletedEvent e, CancellationToken ct) =>
        Log("Product {ProductId} soft-deleted", e.ProductId);

    public Task Handle(ProductRestoredEvent e, CancellationToken ct) => Log("Product {ProductId} restored", e.ProductId);

    public Task Handle(ProductSubmittedForReviewEvent e, CancellationToken ct) =>
        Log("Product {ProductId} submitted for review by {OwnerId}", e.ProductId, e.OwnerId);

    public Task Handle(ProductApprovedEvent e, CancellationToken ct) =>
        Log("Product {ProductId} approved by {ReviewerId}", e.ProductId, e.ReviewerId);

    public Task Handle(ProductRejectedEvent e, CancellationToken ct) =>
        Log("Product {ProductId} rejected by {ReviewerId}", e.ProductId, e.ReviewerId);

    public Task Handle(ProductChangesRequestedEvent e, CancellationToken ct) =>
        Log("Changes requested on product {ProductId} by {ReviewerId}", e.ProductId, e.ReviewerId);

    public Task Handle(ProductArchivedEvent e, CancellationToken ct) => Log("Product {ProductId} archived", e.ProductId);

    public Task Handle(ProductReturnedToDraftEvent e, CancellationToken ct) =>
        Log("Product {ProductId} returned to draft", e.ProductId);

    private Task Log(string message, params object[] args)
    {
#pragma warning disable CA2254 // templates are compile-time constants at every call site
        logger.LogInformation(message, args);
#pragma warning restore CA2254
        return Task.CompletedTask;
    }
}
