using Catalog.Contracts.Products.IntegrationEvents;
using MassTransit;

namespace Catalog.Domains.Products.Consumers;

/// <summary>
/// SAMPLE consumer. Shows the receiving side of an integration event: registered automatically because
/// <c>CatalogRoot.AssemblyName</c> is passed to <c>AddMassTransitWithAssemblies(...)</c> in Program.cs.
/// In a real system it lives in the module that reacts (search indexing, notifications, ...). No existing
/// module publishes a bus event Catalog could react to (Identity raises in-process mediator events only),
/// so the sample consumes Catalog's own event and just logs it.
/// </summary>
public class ProductPublishedConsumer(ILogger<ProductPublishedConsumer> logger)
    : IConsumer<ProductPublishedIntegrationEvent>
{
    public Task Consume(ConsumeContext<ProductPublishedIntegrationEvent> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "[sample consumer] Product {ProductId} (SKU {Sku}) by {OwnerId} was published at {PublishedAt}",
            message.ProductId, message.Sku, message.OwnerId, message.PublishedAt);
        return Task.CompletedTask;
    }
}
