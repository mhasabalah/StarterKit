namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionRenewedEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext,
    ILogger<SubscriptionRenewedEventHandler> logger)
    : INotificationHandler<SubscriptionRenewedEvent>
{
    public async Task Handle(SubscriptionRenewedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new SubscriptionRenewedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            NewRenewalDate = notification.NewRenewalDate.ToString("yyyy-MM-dd"),
            DashboardUrl = $"{frontendBaseUrl}/dashboard"
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionRenewed, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);

        logger.LogInformation(
            "Subscription renewed email sent for {SubscriptionId} subscriber {SubscriberId}",
            notification.SubscriptionId,
            notification.SubscriberId);
    }
}
