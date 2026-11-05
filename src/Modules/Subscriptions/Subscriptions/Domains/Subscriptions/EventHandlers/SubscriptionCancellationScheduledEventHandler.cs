using System.Globalization;
namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class SubscriptionCancellationScheduledEventHandler(
    INotificationSender notificationSender,
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<SubscriptionCancellationScheduledEvent>
{
    public async Task Handle(SubscriptionCancellationScheduledEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new SubscriptionCancellationScheduledModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            CancellationDate = notification.ScheduledCancellationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.SubscriptionCancellationScheduled, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
