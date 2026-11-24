using Microsoft.Extensions.Logging;

namespace Search.Domains.Indexes.EventHandlers;

/// <summary>
/// Tenant offboarding: removes the tenant's documents from the engine. The rows of the Search context are purged
/// centrally by the Tenancy module; an external engine (Meilisearch) is not a database, so this handler is what empties
/// it. Idempotent (a re-run finds nothing). The Default tenant is never purged.
/// </summary>
public sealed class SearchTenantPurgeHandler(ISearchEngine engine, ILogger<SearchTenantPurgeHandler> logger)
    : INotificationHandler<TenantDataPurgeRequestedEvent>
{
    public async Task Handle(TenantDataPurgeRequestedEvent notification, CancellationToken cancellationToken)
    {
        if (notification.TenantId == TenantDefaults.DefaultTenantId)
            return;

        await engine.DeleteTenantAsync(notification.TenantId, cancellationToken);
        logger.LogWarning("Tenant purge: search documents of tenant {TenantId} removed ({Engine})",
            notification.TenantId, engine.Capabilities.Name);
    }
}
