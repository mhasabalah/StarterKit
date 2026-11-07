using Catalog.Contracts.Products.IntegrationEvents;
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
/// It tells the product's owner that the product is live (in-app inbox + real time). Idempotent enough for
/// at-least-once delivery: a redelivery at worst repeats an informational inbox entry.
/// </summary>
public class ProductPublishedConsumer(INotificationSender notifications, ILogger<ProductPublishedConsumer> logger)
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

        await notifications.For(message.OwnerId).WithInApp(Content(message))
            .SendAsync(ct: context.CancellationToken);
    }
}
