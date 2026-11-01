namespace Shared.Notification.Contracts;

public interface INotificationJobEnqueuer
{
    Task<Guid> EnqueueAsync(NotificationMessage message, NotificationChannel? forcedChannel = null, CancellationToken ct = default);
    Task<string> ReEnqueueAsync(Guid notificationLogId, NotificationChannel? forcedChannel = null, CancellationToken ct = default);
}
