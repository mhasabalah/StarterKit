using System.Globalization;
namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionCancelledEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<SubscriptionCancelledEvent>
{
    public async Task Handle(SubscriptionCancelledEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new SubscriptionCancelledModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            CancelledDate = notification.CancelledAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        // IsScheduled=true means the period-end job applied the cancellation (email already sent at schedule time)
        if (notification.IsScheduled)
        {
            return;
        }

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionCancelled, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
