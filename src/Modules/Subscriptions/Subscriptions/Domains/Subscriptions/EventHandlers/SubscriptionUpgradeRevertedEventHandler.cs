namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionUpgradeRevertedEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<SubscriptionUpgradeRevertedEvent>
{
    public async Task Handle(SubscriptionUpgradeRevertedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new SubscriptionUpgradeRevertedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            Reason = "Upgrade SLA expired without proof of payment submission.",
            PortalUrl = $"{frontendBaseUrl}/subscription"
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionUpgradeReverted, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
