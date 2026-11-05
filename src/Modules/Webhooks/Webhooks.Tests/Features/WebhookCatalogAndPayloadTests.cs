using System.Text.Json;
using Catalog.Contracts.Products.IntegrationEvents;
using Shared.Contracts.Messaging;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Webhooks.Options;
using Webhooks.Services;

namespace Webhooks.Tests.Features;

public class WebhookCatalogAndPayloadTests
{
    [Fact]
    public void Every_PublicWebhookEvent_has_a_forwarding_consumer()
    {
        // Mark an event [PublicWebhookEvent] and forget its consumer, and customers could subscribe to an event that never arrives.
        WebhookEventCatalog.MarkedWithoutConsumer().Should().BeEmpty();
    }

    [Fact]
    public void The_catalog_lists_exactly_the_marked_events_by_their_wire_name()
    {
        var catalog = new WebhookEventCatalog();

        catalog.Events.Select(e => e.Type).Should().Equal(
            "catalog.product-published.v1",
            "subscriptions.subscription-activated.v1",
            "subscriptions.subscription-expired.v1");
        catalog.Events.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Description));
    }

    [Fact]
    public void Only_marked_events_are_public()
    {
        var marked = typeof(ProductPublishedIntegrationEvent).Assembly.GetTypes()
            .Concat(typeof(SubscriptionActivatedIntegrationEvent).Assembly.GetTypes())
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttributes(typeof(PublicWebhookEventAttribute), false).Length > 0)
            .Select(t => t.Name);

        marked.Should().BeEquivalentTo("ProductPublishedIntegrationEvent", "SubscriptionActivatedIntegrationEvent",
            "SubscriptionExpiredIntegrationEvent");
    }

    [Theory]
    [InlineData("catalog.product-published.v1", true)]
    [InlineData("*", true)]
    [InlineData("identity.user-deleted.v1", false)]
    [InlineData("nonsense", false)]
    public void IsKnown_AcceptsCatalogTypesAndTheWildcardOnly(string type, bool expected) =>
        new WebhookEventCatalog().IsKnown(type).Should().Be(expected);

    [Fact]
    public void The_payload_envelope_carries_id_type_timestamp_tenant_and_the_event_data()
    {
        var evt = new ProductPublishedIntegrationEvent
        {
            ProductId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Sku = "SKU-1",
            PublishedAt = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc)
        };
        var tenantId = Guid.NewGuid();

        var json = WebhookPayloadFactory.Create("catalog.product-published.v1", evt, tenantId);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("id").GetString().Should().Be($"evt_{evt.EventId:N}");
        root.GetProperty("type").GetString().Should().Be("catalog.product-published.v1");
        root.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        root.GetProperty("timestamp").GetDateTime().Should().Be(evt.OccurredOn);
        var data = root.GetProperty("data");
        data.GetProperty("sku").GetString().Should().Be("SKU-1");
        data.GetProperty("productId").GetGuid().Should().Be(evt.ProductId);
        data.TryGetProperty("eventId", out _).Should().BeFalse("transport metadata is not part of the public data");
        data.TryGetProperty("occurredOn", out _).Should().BeFalse();
    }

    [Fact]
    public void A_ping_payload_has_the_ping_type()
    {
        var json = WebhookPayloadFactory.CreatePing(Guid.NewGuid(), TestSupport.Now, Guid.NewGuid());

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("webhook.ping");
    }

    [Theory]
    [InlineData(2, 10)]
    [InlineData(3, 60)]
    [InlineData(4, 360)]
    [InlineData(5, 2160)]
    [InlineData(6, 12960)]
    [InlineData(7, 21600)]   // capped at 6 hours
    [InlineData(20, 21600)]
    public void Retry_delays_grow_exponentially_and_are_capped(int nextAttempt, int expectedSeconds) =>
        new WebhooksOptions().DelayBeforeAttempt(nextAttempt).TotalSeconds.Should().Be(expectedSeconds);
}
