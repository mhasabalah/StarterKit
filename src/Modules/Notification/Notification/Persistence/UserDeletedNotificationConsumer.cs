using MassTransit;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Notification.Persistence;

/// <summary>
/// Account erasure: deletes the user's delivery log (it stores the rendered message - names, addresses, links) and
/// their channel preferences. Idempotent: a re-delivery deletes zero rows. Bulk deletes, so no aggregate events.
/// </summary>
public sealed class UserDeletedNotificationConsumer(NotificationDbContext db, ILogger<UserDeletedNotificationConsumer> logger)
    : IConsumer<UserDeletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<UserDeletedIntegrationEvent> context)
    {
        var userId = context.Message.UserId;
        var ct = context.CancellationToken;

        var logs = await db.NotificationLogs.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        var preferences = await db.UserNotificationPreferences.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        var inbox = await db.InboxNotifications.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        var devices = await db.PushDevices.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Erased notification data of deleted user {UserId}: {Logs} log row(s), {Preferences} preference row(s), " +
            "{Inbox} inbox entr(ies), {Devices} push device(s)",
            userId, logs, preferences, inbox, devices);
    }
}
