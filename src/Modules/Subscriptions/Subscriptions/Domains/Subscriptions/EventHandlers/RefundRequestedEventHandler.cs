using System.Globalization;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.EventHandlers;

/// <summary>Tells the billing team (see <see cref="IAdminNotificationRecipients"/>) that a refund was requested.</summary>
public class RefundRequestedEventHandler(
    ISubscriberInfoProvider subscriberInfoProvider,
    INotificationSender notificationSender,
    IAdminNotificationRecipients recipients,
    SubscriptionsDbContext dbContext,
    ILogger<RefundRequestedEventHandler> logger)
    : INotificationHandler<RefundRequestedEvent>
{
    public async Task Handle(RefundRequestedEvent notification, CancellationToken cancellationToken)
    {
        var adminEmails = await recipients.GetEmailsAsync(cancellationToken);
        if (adminEmails.Count == 0)
        {
            logger.LogWarning(
                "No admin notification recipients configured (Subscriptions:AdminNotifications) - skipping refund notification for {RefundRequestId}",
                notification.RefundRequestId);
            return;
        }

        var refund = await dbContext.RefundRequests
            .AsNoTracking()
            .Where(x => x.Id == notification.RefundRequestId)
            .Select(x => new { x.PlanName, x.RefundType })
            .FirstOrDefaultAsync(cancellationToken);

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var model = new RefundRequestedModel
        {
            SubscriberName = subscriber?.DisplayName ?? "Unknown subscriber",
            PlanName = refund?.PlanName ?? "Plan",
            Amount = CurrencyRules.Format(notification.RequestedAmount, notification.Currency),
            Currency = notification.Currency,
            RefundType = refund?.RefundType.ToString() ?? string.Empty
        };

        foreach (var email in adminEmails)
        {
            await notificationSender
                .ToEmail(email)
                .WithEmail(EmailTemplate.RefundRequested, model.ToDictionary())
                .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
        }
    }
}
