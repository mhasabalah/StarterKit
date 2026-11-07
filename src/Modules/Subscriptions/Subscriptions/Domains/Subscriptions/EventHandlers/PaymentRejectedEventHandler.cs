using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class PaymentRejectedEventHandler(
    INotificationSender notificationSender,
    ITenantScopeFactory tenants,
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<PaymentRejectedEvent>
{
    public async Task Handle(PaymentRejectedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new PaymentRejectedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            RejectionReason = notification.Reason ?? string.Empty,
            ResubmitUrl = $"{frontendBaseUrl}/subscription/renew",
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.PaymentRejected, model.ToDictionary())
            .WithBillingInApp("payment-rejected", NotificationSeverity.Critical, BillingInApp.RenewUrl, push: true, sensitivePush: true, data: BillingInApp.SubscriptionData(notification.SubscriptionId, notification.SubscriberId, tenants), args: [planName])
            .SendForSubscriberAsync(tenants, notification.SubscriberId, NotificationChannel.Email, cancellationToken);
    }
}
