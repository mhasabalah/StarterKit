using System.Globalization;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionDowngradeAppliedEventHandler(
    INotificationSender notificationSender,
    ITenantScopeFactory tenants,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<SubscriptionDowngradeAppliedEvent>
{
    public async Task Handle(SubscriptionDowngradeAppliedEvent notification, CancellationToken cancellationToken)
    {
        var subscriptionInfo = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => new { x.PlanName, x.RenewalDate })
            .FirstOrDefaultAsync(cancellationToken);

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new SubscriptionDowngradeAppliedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            NewPlanName = subscriptionInfo?.PlanName ?? "New Plan",
            NewRenewalDate = subscriptionInfo?.RenewalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            PortalUrl = $"{frontendBaseUrl}/subscription"
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionDowngradeApplied, model.ToDictionary())
            .WithBillingInApp("downgrade-applied", NotificationSeverity.Info, BillingInApp.SubscriptionUrl, data: BillingInApp.SubscriptionData(notification.SubscriptionId, notification.SubscriberId, tenants), args: [model.NewPlanName, model.NewRenewalDate])
            .SendForSubscriberAsync(tenants, notification.SubscriberId, NotificationChannel.Email, cancellationToken);
    }
}
