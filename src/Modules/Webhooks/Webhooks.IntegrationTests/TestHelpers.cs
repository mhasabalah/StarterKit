#if (SAMPLE)
using Catalog.Contracts.Products.IntegrationEvents;
#endif
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Contracts.Messaging;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Webhooks.Contracts.Endpoints;
using Webhooks.Domains.Deliveries.Consumers;
using Webhooks.Domains.Deliveries.Models;

namespace Webhooks.IntegrationTests;

public static class Arrange
{
#if (SAMPLE)
    public const string ProductPublished = "catalog.product-published.v1";
#endif
    public const string SubscriptionExpired = "subscriptions.subscription-expired.v1";

    /// <summary>Creates an endpoint through the real command; returns its id and the raw secret (shown once).</summary>
    public static async Task<(Guid Id, string Secret)> Endpoint(WebhooksPostgresFixture fx, TenantInfo tenant,
        string? url = null, params string[] eventTypes)
    {
        var result = await fx.SendAsync(tenant, new CreateWebhookEndpointCommand(
            url ?? $"http://127.0.0.1:1/{Guid.NewGuid():N}", "it", eventTypes.Length == 0 ? [SubscriptionExpired] : eventTypes));
        return (result.Id, result.Secret);
    }

#if (SAMPLE)
    public static ProductPublishedIntegrationEvent NewProductEvent() => new()
    {
        ProductId = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        Sku = "SKU-" + Guid.NewGuid().ToString("N")[..8],
        PublishedAt = DateTime.UtcNow
    };
#endif

    /// <summary>A public event of a module every project has: forwarded to the subscriber's tenant.</summary>
    public static SubscriptionExpiredIntegrationEvent NewSubscriptionEvent(TenantInfo tenant) =>
        new() { SubscriberId = tenant.Id, SubscriptionId = Guid.NewGuid() };

    private static ConsumeContext<T> ContextOf<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    /// <summary>Delivers a bus message the way MassTransit does: as the tenant carried in the message header.</summary>
    public static async Task Consume(WebhooksPostgresFixture fx, TenantInfo? tenant, IIntegrationEvent message)
    {
        var scope = tenant is null ? null : TenantScope.Begin(tenant);
        try
        {
            var sp = fx.Services;
            var uow = sp.GetRequiredService<IUnitOfWorkRunner>();
            var jobs = sp.GetRequiredService<Hangfire.IBackgroundJobClient>();
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var store = sp.GetRequiredService<ITenantStore>();

            switch (message)
            {
#if (SAMPLE)
                case ProductPublishedIntegrationEvent e:
                    await new ProductPublishedWebhookConsumer(uow, jobs, tenantContext, store).Consume(ContextOf(e));
                    break;
#endif
                case SubscriptionActivatedIntegrationEvent e:
                    await new SubscriptionActivatedWebhookConsumer(uow, jobs, tenantContext, store).Consume(ContextOf(e));
                    break;
                case SubscriptionExpiredIntegrationEvent e:
                    await new SubscriptionExpiredWebhookConsumer(uow, jobs, tenantContext, store).Consume(ContextOf(e));
                    break;
                default:
                    throw new NotSupportedException(message.GetType().Name);
            }
        }
        finally
        {
            scope?.Dispose();
        }
    }

    public static Task<List<WebhookDelivery>> Deliveries(WebhooksPostgresFixture fx, TenantInfo tenant, Guid? endpointId = null) =>
        fx.InTenantDbAsync(tenant, db => db.Deliveries.AsNoTracking()
            .Where(d => endpointId == null || d.EndpointId == endpointId)
            .OrderBy(d => d.CreatedAt).ToListAsync());

    public static Task<WebhookDelivery> Delivery(WebhooksPostgresFixture fx, TenantInfo tenant, Guid id) =>
        fx.InTenantDbAsync(tenant, db => db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == id));

    public static Task<Webhooks.Domains.Endpoints.Models.WebhookEndpoint> EndpointRow(WebhooksPostgresFixture fx,
        TenantInfo tenant, Guid id) =>
        fx.InTenantDbAsync(tenant, db => db.Endpoints.AsNoTracking().FirstAsync(e => e.Id == id));

    /// <summary>Makes a delivery for the endpoint by publishing a subscription event; returns the new delivery.</summary>
    public static async Task<WebhookDelivery> OneDelivery(WebhooksPostgresFixture fx, TenantInfo tenant, Guid endpointId)
    {
        await Consume(fx, tenant, NewSubscriptionEvent(tenant));
        return (await Deliveries(fx, tenant, endpointId)).Last();
    }
}
