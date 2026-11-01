namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionPastDueEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext,
    ILogger<SubscriptionPastDueEventHandler> logger)
    : INotificationHandler<SubscriptionPastDueEvent>
{
    public async Task Handle(SubscriptionPastDueEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new SubscriptionPastDueModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            GraceDeadline = notification.GracePeriodEndsAt.ToString("yyyy-MM-dd"),
            RenewalUrl = $"{frontendBaseUrl}/subscription/renew",
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionPastDue, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);

        logger.LogInformation(
            "Subscription past due email sent for {SubscriptionId} subscriber {SubscriberId} grace {GracePeriodEndsAt}",
            notification.SubscriptionId,
            notification.SubscriberId,
            notification.GracePeriodEndsAt);
    }
}
