namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionExpiredEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext,
    ILogger<SubscriptionExpiredEventHandler> logger)
    : INotificationHandler<SubscriptionExpiredEvent>
{
    public async Task Handle(SubscriptionExpiredEvent notification, CancellationToken cancellationToken)
    {
        var sub = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => new { x.PlanName, x.GracePeriodEndsAt })
            .FirstOrDefaultAsync(cancellationToken);

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new SubscriptionExpiredModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = sub?.PlanName ?? "Plan",
            GracePeriodEndsAt = sub?.GracePeriodEndsAt?.ToString("yyyy-MM-dd") ?? string.Empty,
            RenewalUrl = $"{frontendBaseUrl}/subscription/renew"
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionExpired, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);

        logger.LogInformation(
            "Subscription expired email sent for {SubscriptionId} subscriber {SubscriberId}",
            notification.SubscriptionId,
            notification.SubscriberId);
    }
}
