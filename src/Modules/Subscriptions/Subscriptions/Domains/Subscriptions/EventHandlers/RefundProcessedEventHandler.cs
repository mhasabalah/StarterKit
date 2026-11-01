namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class RefundProcessedEventHandler(
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    INotificationSender notificationSender,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<RefundProcessedEvent>
{
    public async Task Handle(RefundProcessedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.RefundRequests
            .AsNoTracking()
            .Where(x => x.Id == notification.RefundRequestId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new RefundProcessedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            Amount = notification.Amount.ToString("F2"),
            Currency = notification.Currency,
            TransactionRef = notification.ExternalTransactionRef,
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.RefundProcessed, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
