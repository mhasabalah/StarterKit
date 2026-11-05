using System.Globalization;
namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionDowngradeScheduledEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<SubscriptionDowngradeScheduledEvent>
{
    public async Task Handle(SubscriptionDowngradeScheduledEvent notification, CancellationToken cancellationToken)
    {
        var planInfo = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Current Plan";

        var newPlanName = await dbContext.Plans
            .AsNoTracking()
            .Where(x => x.Id == notification.NewPlanId)
            .Select(x => x.Name.En)
            .FirstOrDefaultAsync(cancellationToken) ?? "New Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new SubscriptionDowngradeScheduledModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            CurrentPlanName = planInfo,
            NewPlanName = newPlanName,
            EffectiveAt = notification.EffectiveAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PortalUrl = $"{frontendBaseUrl}/subscription"
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionDowngradeScheduled, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
