using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class RefundRejectedEventHandler(
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    INotificationSender notificationSender,
    ITenantScopeFactory tenants,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<RefundRejectedEvent>
{
    public async Task Handle(RefundRejectedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.RefundRequests
            .AsNoTracking()
            .Where(x => x.Id == notification.RefundRequestId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new RefundRejectedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            Reason = notification.Reason,
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.RefundRejected, model.ToDictionary())
            .WithBillingInApp("refund-rejected", NotificationSeverity.Warning, BillingInApp.SubscriptionUrl, args: [planName])
            .SendForSubscriberAsync(tenants, notification.SubscriberId, NotificationChannel.Email, cancellationToken);
    }
}
