using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionRejectedEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<SubscriptionRejectedEvent>
{
    public async Task Handle(SubscriptionRejectedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new SubscriptionRejectedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            RejectionReason = notification.Reason ?? string.Empty,
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionRejected, model.ToDictionary())
            .WithBillingInApp("subscription-rejected", NotificationSeverity.Critical, BillingInApp.SubscriptionUrl, push: true, data: BillingInApp.SubscriptionData(notification.SubscriptionId), args: [planName])
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
