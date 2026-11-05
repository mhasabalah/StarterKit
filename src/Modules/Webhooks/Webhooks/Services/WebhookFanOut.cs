using Shared.Contracts.Messaging;
using Webhooks.Data.Configurations;
using Webhooks.Domains.Deliveries.Models;

namespace Webhooks.Services;

public interface IWebhookFanOut
{
    /// <summary>
    /// One pending delivery per enabled endpoint of the CURRENT tenant that subscribes to <paramref name="eventType"/>.
    /// Idempotent per (endpoint, event): the bus is at-least-once, a repeated event creates nothing.
    /// </summary>
    /// <returns>Ids of the deliveries created now (the caller enqueues their jobs after commit).</returns>
    Task<IReadOnlyList<Guid>> CreateDeliveriesAsync(IIntegrationEvent integrationEvent, string eventType, string payload,
        CancellationToken cancellationToken);
}

public sealed class WebhookFanOut(WebhooksDbContext dbContext, TimeProvider timeProvider, ILogger<WebhookFanOut> logger)
    : IWebhookFanOut
{
    public async Task<IReadOnlyList<Guid>> CreateDeliveriesAsync(IIntegrationEvent integrationEvent, string eventType,
        string payload, CancellationToken cancellationToken)
    {
        var endpoints = await dbContext.Endpoints.AsNoTracking()
            .Where(e => e.Enabled && (e.EventTypes.Contains(eventType) || e.EventTypes.Contains("*")))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        if (endpoints.Count == 0)
            return [];

        var existing = await dbContext.Deliveries.AsNoTracking()
            .Where(d => d.EventId == integrationEvent.EventId && d.RedeliveryOfId == null && endpoints.Contains(d.EndpointId))
            .Select(d => d.EndpointId)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = endpoints.Except(existing)
            .Select(endpointId => WebhookDelivery.Create(endpointId, integrationEvent.EventId, eventType, payload, now))
            .ToList();
        if (created.Count == 0)
            return [];

        dbContext.Deliveries.AddRange(created);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(WebhookDeliveryConfiguration.EventIndexName))
        {
            // A concurrent consumer instance fanned the same event out first: nothing left to do here.
            logger.LogInformation("Webhook fan-out for event {EventId} was already done concurrently", integrationEvent.EventId);
            return [];
        }

        return created.Select(d => d.Id).ToList();
    }
}
