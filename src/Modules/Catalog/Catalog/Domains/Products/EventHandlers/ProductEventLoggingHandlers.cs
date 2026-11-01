using Catalog.Domains.Products.Events;

namespace Catalog.Domains.Products.EventHandlers;

/// <summary>
/// Sample in-process domain-event handlers. Replace the logging with real side effects
/// (cache invalidation, integration events via MassTransit, search indexing, ...).
/// </summary>
public class ProductEventLoggingHandlers(ILogger<ProductEventLoggingHandlers> logger)
    : INotificationHandler<ProductCreatedEvent>,
      INotificationHandler<ProductUpdatedEvent>,
      INotificationHandler<ProductDeletedEvent>
{
    public Task Handle(ProductCreatedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Product {ProductId} created with SKU {Sku}",
            notification.ProductId, notification.Sku);
        return Task.CompletedTask;
    }

    public Task Handle(ProductUpdatedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Product {ProductId} updated (active: {IsActive})",
            notification.ProductId, notification.IsActive);
        return Task.CompletedTask;
    }

    public Task Handle(ProductDeletedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Product {ProductId} deleted", notification.ProductId);
        return Task.CompletedTask;
    }
}
