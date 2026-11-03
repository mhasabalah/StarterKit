using Attachment.Infrastructure.Storage;
using Shared.Core.MultiTenancy;

namespace Attachment.Domains.Attachments.EventHandlers;

/// <summary>
/// Tenant offboarding: deletes every blob under the tenant's storage root (<c>t/{tenantId}/</c>). Rows are purged
/// centrally by the Tenancy module. Idempotent (re-runs find nothing left). The Default tenant is never purged.
/// </summary>
public sealed class TenantDataPurgeRequestedEventHandler(
    IStorageService storage,
    ILogger<TenantDataPurgeRequestedEventHandler> logger) : INotificationHandler<TenantDataPurgeRequestedEvent>
{
    private const int BatchSize = 500;

    public async Task Handle(TenantDataPurgeRequestedEvent notification, CancellationToken cancellationToken)
    {
        if (notification.TenantId == TenantDefaults.DefaultTenantId) return;

        if (storage is not IStorageListing listing)
        {
            logger.LogWarning("Storage provider cannot list objects; blobs of tenant {TenantId} must be removed manually",
                notification.TenantId);
            return;
        }

        var root = StoragePaths.TenantRoot(notification.TenantId);
        var batch = new List<string>(BatchSize);
        var deleted = 0;

        await foreach (var item in listing.ListAsync(root, cancellationToken))
        {
            batch.Add(item.StoragePath);
            if (batch.Count < BatchSize) continue;

            deleted += await DeleteAsync(batch, cancellationToken);
            batch.Clear();
        }

        if (batch.Count > 0) deleted += await DeleteAsync(batch, cancellationToken);

        logger.LogWarning("Tenant purge: {Count} blob(s) of tenant {TenantId} deleted", deleted, notification.TenantId);
    }

    private async Task<int> DeleteAsync(List<string> paths, CancellationToken cancellationToken)
    {
        var result = await storage.DeleteBatchAsync(paths, cancellationToken);
        if (result.Failed.Count > 0)
            throw new InvalidOperationException(
                $"{result.Failed.Count} blob(s) could not be deleted; the purge is retried on the next run.");
        return paths.Count;
    }
}
