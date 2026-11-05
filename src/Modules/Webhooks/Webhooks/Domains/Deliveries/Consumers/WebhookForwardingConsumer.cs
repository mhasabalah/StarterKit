using Catalog.Contracts.Products.IntegrationEvents;
using Hangfire;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Messaging;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Webhooks.Domains.Deliveries.Jobs;
using Webhooks.Services;

namespace Webhooks.Domains.Deliveries.Consumers;

/// <summary>
/// Forwards one <c>[PublicWebhookEvent]</c> integration event to the tenant's webhook endpoints: creates one pending
/// delivery per subscribed endpoint (in a unit of work, idempotent per event) and, after commit, enqueues each delivery
/// job. The bus restores the tenant from the message header, so for tenant-owned events everything here already runs as the
/// publishing tenant.
/// <para>
/// Host-owned events (Subscriptions: the SUBSCRIBER is the tenant in multi-tenant mode) carry no tenant header; those
/// subclasses name the tenant through <see cref="TenantOverride"/> and this consumer enters that tenant's scope.
/// </para>
/// <para>
/// MassTransit discovers only CLOSED consumer types, hence one tiny sealed subclass per forwarded event below. Adding a
/// public event means: mark it <c>[PublicWebhookEvent]</c> and add its subclass here (a unit test fails otherwise).
/// </para>
/// </summary>
public abstract class WebhookForwardingConsumer<TEvent>(
    IUnitOfWorkRunner unitOfWork,
    IBackgroundJobClient jobs,
    ITenantContext tenantContext,
    ITenantStore tenantStore) : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    /// <summary>The tenant this event belongs to when it is NOT the ambient one (multi-tenant mode only), else <c>null</c>.</summary>
    protected virtual Guid? TenantOverride(TEvent message) => null;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var scopeTenant = tenantContext.MultiTenancyEnabled ? TenantOverride(context.Message) : null;
        if (scopeTenant is { } tenantId && tenantId != tenantContext.TenantId)
        {
            var tenant = await tenantStore.FindByIdAsync(tenantId, context.CancellationToken);
            if (tenant is null) return; // the subscriber is not a tenant

            using (TenantScope.Begin(tenant))
            {
                await ForwardAsync(context, tenant.Id);
            }

            return;
        }

        // No tenant on the message (host-level event): nothing tenant-owned to forward to.
        if (tenantContext.TenantId is not { } ambient) return;
        await ForwardAsync(context, ambient);
    }

    private async Task ForwardAsync(ConsumeContext<TEvent> context, Guid tenantId)
    {
        var eventType = WebhookEventCatalog.TypeOf(typeof(TEvent));
        var payload = WebhookPayloadFactory.Create(eventType, context.Message, tenantId);

        var deliveryIds = await unitOfWork.RunAsync(
            (services, ct) => services.GetRequiredService<IWebhookFanOut>()
                .CreateDeliveriesAsync(context.Message, eventType, payload, ct),
            context.CancellationToken);

        // After commit. If the process dies right here, the sweep job re-enqueues the (overdue) pending deliveries.
        foreach (var id in deliveryIds)
        {
            jobs.Enqueue<DeliverWebhookJob>(job => job.ExecuteAsync(id, CancellationToken.None));
        }
    }
}

public sealed class ProductPublishedWebhookConsumer(
    IUnitOfWorkRunner unitOfWork, IBackgroundJobClient jobs, ITenantContext tenantContext, ITenantStore tenantStore)
    : WebhookForwardingConsumer<ProductPublishedIntegrationEvent>(unitOfWork, jobs, tenantContext, tenantStore);

public sealed class SubscriptionActivatedWebhookConsumer(
    IUnitOfWorkRunner unitOfWork, IBackgroundJobClient jobs, ITenantContext tenantContext, ITenantStore tenantStore)
    : WebhookForwardingConsumer<SubscriptionActivatedIntegrationEvent>(unitOfWork, jobs, tenantContext, tenantStore)
{
    protected override Guid? TenantOverride(SubscriptionActivatedIntegrationEvent message) => message.SubscriberId;
}

public sealed class SubscriptionExpiredWebhookConsumer(
    IUnitOfWorkRunner unitOfWork, IBackgroundJobClient jobs, ITenantContext tenantContext, ITenantStore tenantStore)
    : WebhookForwardingConsumer<SubscriptionExpiredIntegrationEvent>(unitOfWork, jobs, tenantContext, tenantStore)
{
    protected override Guid? TenantOverride(SubscriptionExpiredIntegrationEvent message) => message.SubscriberId;
}
