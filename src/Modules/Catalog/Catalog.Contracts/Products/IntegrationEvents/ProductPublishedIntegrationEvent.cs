using MassTransit;
using Shared.Contracts.Messaging;

namespace Catalog.Contracts.Products.IntegrationEvents;

/// <summary>
/// Integration event: a product went live. Other modules (search indexing, notifications, analytics, ...) consume it
/// with an <c>IConsumer&lt;ProductPublishedIntegrationEvent&gt;</c>. Raised by the Product aggregate and written to the
/// Catalog outbox in the same transaction as the status change, then delivered at least once.
/// Carries ids and a minimal projection only, never the aggregate. The urn is the stable wire name - never change it.
/// </summary>
[MessageUrn("catalog.product-published.v1")]
[PublicWebhookEvent(Description = "A product went live in the catalog.")]
public sealed class ProductPublishedIntegrationEvent : IntegrationEvent
{
    public Guid ProductId { get; init; }
    public Guid OwnerId { get; init; }
    public string Sku { get; init; } = default!;
    public DateTime PublishedAt { get; init; }
}
