using Notification.Contracts.InApp;
using Notification.Contracts.Inbox;
using Notification.Contracts.Preferences;

namespace Notification.Dispatch;

/// <summary>
/// Writes one inbox entry and tells the user's open sessions (SignalR).
/// <para><b>Exactly-once entry</b>: insert with <c>ON CONFLICT (user_id, correlation_id) DO NOTHING</c>, so any
/// re-run (Hangfire retry after a crash, a duplicate enqueue) finds the existing row instead of adding a second one.</para>
/// <para><b>At-least-once event</b>: the real-time event is sent before the <c>Sent</c> log row, so a crash between
/// the two re-sends the event on retry (clients de-duplicate by id). A finished delivery (log row Sent) is a no-op.</para>
/// <para>The tenant comes from the ambient scope Hangfire restores (the request's tenant at send time).</para>
/// </summary>
[Queue("notification")]
[AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 10, 60, 300, 900, 1800 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class InAppDeliveryJob(
    IInboxNotificationRepository inbox,
    INotificationLogRepository logs,
    INotificationPreferenceRepository preferences,
    IRealtimeNotifier realtime,
    IOptions<EmailOptions> emailOptions,
    IOptions<InboxOptions> inboxOptions,
    NotificationMetrics metrics,
    TimeProvider timeProvider,
    ILogger<InAppDeliveryJob> logger,
    ITenantContext? tenantContext = null)
{
    public async Task ExecuteAsync(InAppDelivery delivery, Guid correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var history = (await logs.GetByCorrelationAsync(correlationId, ct))
            .Where(h => h.Channel == NotificationChannel.InApp)
            .ToList();
        if (history.Any(h => h.Status is NotificationDeliveryStatus.Sent or NotificationDeliveryStatus.Skipped))
        {
            logger.LogInformation("In-app notification already handled - skipping re-run. CorrelationId: {CorrelationId}", correlationId);
            return;
        }

        if (delivery.UserId == Guid.Empty)
        {
            await logs.InsertAsync(Log(correlationId, delivery, NotificationDeliveryStatus.Skipped,
                "In-app notifications need a user recipient (sent with ToEmail and no userId).",
                timeProvider.GetUtcNow().UtcDateTime), ct);
            logger.LogWarning("In-app notification without a user recipient skipped. CorrelationId: {CorrelationId}", correlationId);
            return;
        }

        var preference = await preferences.GetDeliveryPreferenceAsync(delivery.UserId, ct);
        if (NotificationPolicy.IsMuted(preference, delivery.Content.Category, NotificationChannel.InApp))
        {
            await logs.InsertAsync(Log(correlationId, delivery, NotificationDeliveryStatus.Skipped,
                $"The user muted in-app notifications for the '{NotificationCategoryGroups.GroupOf(delivery.Content.Category)}' group.",
                timeProvider.GetUtcNow().UtcDateTime), ct);
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tenantId = InboxScope.CurrentTenantId(tenantContext);
        var entry = Render(delivery, correlationId, tenantId, now, emailOptions.Value, inboxOptions.Value);

        if (!await inbox.TryAddAsync(entry, ct))
        {
            // A previous run inserted it: notify with the STORED entry (same id the client may already hold).
            entry = await inbox.GetByCorrelationAsync(delivery.UserId, correlationId, ct) ?? entry;
        }
        else
        {
            metrics.InAppDelivered();
        }

        // Quiet hours: the entry is stored (it waits in the inbox, counted as unread) but nothing pops up live.
        if (NotificationPolicy.QuietHoursEnd(preference, entry.Category, now) is null)
            await realtime.NotificationReceivedAsync(delivery.UserId, ToDto(entry), ct);
        var unread = await inbox.CountUnreadAsync(delivery.UserId, entry.TenantId, ct);
        await realtime.UnreadCountChangedAsync(delivery.UserId, entry.TenantId, unread, ct);

        try
        {
            var log = Log(correlationId, delivery, NotificationDeliveryStatus.Sent, null, now);
            log.SentAt = now;
            log.TenantId = entry.TenantId;
            await logs.InsertAsync(log, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The entry exists - retrying would only repeat the event. Never fail a delivered notification.
            logger.LogError(ex, "Inbox entry {EntryId} stored but its log row could not be written. CorrelationId: {CorrelationId}",
                entry.Id, correlationId);
        }

        logger.LogInformation("In-app notification delivered. Category: {Category} UserId: {UserId} CorrelationId: {CorrelationId}",
            entry.Category, delivery.UserId, correlationId);
    }

    internal static InboxNotification Render(
        InAppDelivery delivery, Guid correlationId, Guid? tenantId, DateTime now, EmailOptions links, InboxOptions options)
    {
        var content = delivery.Content;
        return new InboxNotification
        {
            Id = Guid.CreateVersion7(),
            UserId = delivery.UserId,
            TenantId = tenantId,
            CorrelationId = correlationId,
            Category = InAppContentRenderer.Text(content.Category, InAppContent.CategoryMaxLength),
            Severity = Enum.IsDefined(content.Severity) ? content.Severity : NotificationSeverity.Info,
            Title = InAppContentRenderer.Text(content.Title.For(delivery.Culture), InAppContent.TitleMaxLength),
            Body = InAppContentRenderer.Text(content.Body.For(delivery.Culture), InAppContent.BodyMaxLength),
            ActionUrl = InAppContentRenderer.ActionUrl(content.ActionUrl, links),
            Data = InAppContentRenderer.Data(content.Data, InAppContent.MaxDataEntries),
            CreatedAt = now,
            ExpiresAt = now.AddDays(Math.Max(1, options.RetentionDays))
        };
    }

    internal static InboxNotificationDto ToDto(InboxNotification x) =>
        new(x.Id, x.CorrelationId, x.TenantId, x.Category, x.Severity, x.Title, x.Body, x.ActionUrl, x.Data, x.CreatedAt, x.ReadAt);

    private static NotificationLog Log(
        Guid correlationId, InAppDelivery delivery, NotificationDeliveryStatus status, string? error, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            UserId = delivery.UserId,
            Channel = NotificationChannel.InApp,
            TemplateName = delivery.Content.Category,
            Status = status,
            Error = error,
            Culture = delivery.Culture,
            CreatedAt = now
        };
}
