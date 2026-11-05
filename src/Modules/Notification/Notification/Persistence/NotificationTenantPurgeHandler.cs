namespace Notification.Persistence;

/// <summary>Tenant offboarding: erases the tenant's notification log (personal data).</summary>
public sealed class NotificationTenantPurgeHandler(NotificationDbContext db, ILogger<NotificationTenantPurgeHandler> logger)
    : INotificationHandler<TenantDataPurgeRequestedEvent>
{
    public async Task Handle(TenantDataPurgeRequestedEvent notification, CancellationToken cancellationToken)
    {
        var deleted = await db.NotificationLogs.Where(x => x.TenantId == notification.TenantId)
            .ExecuteDeleteAsync(cancellationToken);
        var inbox = await db.InboxNotifications.Where(x => x.TenantId == notification.TenantId)
            .ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Purged {Count} notification log row(s) and {Inbox} inbox entr(ies) of tenant {TenantId}",
            deleted, inbox, notification.TenantId);
    }
}
