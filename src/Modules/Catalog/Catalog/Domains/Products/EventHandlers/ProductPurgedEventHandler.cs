using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Catalog.Domains.Products.Events;

namespace Catalog.Domains.Products.EventHandlers;

/// <summary>
/// Cleans up every attachment linked to a product once the product is HARD-deleted (purged).
/// Deliberately not on the soft delete: a restored product must still have its images.
/// Failures are logged, never rethrown — the product is already gone.
/// </summary>
public class ProductPurgedEventHandler(ISender sender, ILogger<ProductPurgedEventHandler> logger)
    : INotificationHandler<ProductPurgedEvent>
{
    public async Task Handle(ProductPurgedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.Send(
                new DeleteAttachmentsByEntityCommand(EntityType.Product, notification.ProductId), cancellationToken);

            logger.LogInformation("Deleted {Count} attachment(s) of purged product {ProductId}",
                result.DeletedCount, notification.ProductId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to delete attachments of purged product {ProductId}; files may be orphaned",
                notification.ProductId);
        }
    }
}
