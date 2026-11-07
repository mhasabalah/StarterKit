using Catalog.Contracts.Products.IntegrationEvents;
using Catalog.Contracts.Products.Queries;
using Catalog.Resources;
using MassTransit;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Shared.Core.Localization;

namespace Catalog.Domains.Products.Consumers;

/// <summary>
/// SAMPLE consumer. Shows the receiving side of an integration event: registered automatically because
/// <c>CatalogRoot.AssemblyName</c> is passed to <c>AddMassTransitWithAssemblies(...)</c> in Program.cs.
/// It tells the product's owner that the product is live (in-app inbox + real time). Consumers re-read the current
/// state: if the product is no longer published (unpublished or deleted since) there is nothing to announce. Delivery
/// is at-least-once, so the notification is pinned to the event id: a redelivery maps to the same correlation id and the
/// inbox (unique per user and correlation id) keeps a single entry.
/// </summary>
public class ProductPublishedConsumer(ISender sender, INotificationSender notifications, ILogger<ProductPublishedConsumer> logger)
    : IConsumer<ProductPublishedIntegrationEvent>
{
    public const string Category = "catalog.product-published";

    public static InAppContent Content(ProductPublishedIntegrationEvent message) =>
        new(
            Title: BilingualResource.Get(CatalogResource.ResourceManager, "InApp.catalog.product-published.Title"),
            Body: BilingualResource.Get(CatalogResource.ResourceManager, "InApp.catalog.product-published.Body", message.Sku),
            Category: Category,
            ActionUrl: $"/catalog/products/{message.ProductId}",
            Severity: NotificationSeverity.Success,
            Data: new Dictionary<string, string> { ["productId"] = message.ProductId.ToString() });

    public async Task Consume(ConsumeContext<ProductPublishedIntegrationEvent> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "Product {ProductId} (SKU {Sku}) by {OwnerId} was published at {PublishedAt}",
            message.ProductId, message.Sku, message.OwnerId, message.PublishedAt);

        try
        {
            // Anonymous read = published products only; anything else is "not found".
            await sender.Send(new GetProductByIdQuery(message.ProductId, IncrementView: false), context.CancellationToken);
        }
        catch (ProductNotFoundException)
        {
            logger.LogInformation("Product {ProductId} is no longer published; owner not notified", message.ProductId);
            return;
        }

        await notifications.For(message.OwnerId)
            .WithInApp(Content(message))
            .WithCorrelationId(context.MessageId ?? message.EventId)
            .SendAsync(ct: context.CancellationToken);
    }
}
