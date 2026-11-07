using System.Globalization;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionUpgradedEventHandler(
    INotificationSender notificationSender,
    ITenantScopeFactory tenants,
    ISubscriberInfoProvider subscriberInfoProvider,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<SubscriptionUpgradedEvent>
{
    public async Task Handle(SubscriptionUpgradedEvent notification, CancellationToken cancellationToken)
    {
        var planInfo = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => new { x.PlanName, x.Currency })
            .FirstOrDefaultAsync(cancellationToken);

        var oldPlan = await dbContext.Plans
            .AsNoTracking()
            .Where(x => x.Id == notification.OldPlanId)
            .Select(x => x.Name.En)
            .FirstOrDefaultAsync(cancellationToken) ?? "Previous Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new SubscriptionUpgradedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            OldPlanName = oldPlan,
            NewPlanName = planInfo?.PlanName ?? "New Plan",
            ProratedAmount = CurrencyRules.Format(notification.ProratedAmount, planInfo?.Currency),
            Currency = planInfo?.Currency ?? "USD",
            IsDeferred = notification.IsDeferred ? "Yes — payment deferred" : "No — payment required now",
            PortalUrl = $"{frontendBaseUrl}/subscription"
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionUpgraded, model.ToDictionary())
            .WithBillingInApp("upgraded", NotificationSeverity.Success, BillingInApp.SubscriptionUrl, data: BillingInApp.SubscriptionData(notification.SubscriptionId, notification.SubscriberId, tenants), args: [model.OldPlanName, model.NewPlanName])
            .SendForSubscriberAsync(tenants, notification.SubscriberId, NotificationChannel.Email, cancellationToken);
    }
}
