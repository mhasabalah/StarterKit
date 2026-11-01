namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class RefundApprovedEventHandler(
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    INotificationSender notificationSender,
    SubscriptionsDbContext dbContext)
    : INotificationHandler<RefundApprovedEvent>
{
    public async Task Handle(RefundApprovedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.RefundRequests
            .AsNoTracking()
            .Where(x => x.Id == notification.RefundRequestId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var instructions = await sender.Send(new GetPaymentInstructionsQuery(), cancellationToken);

        var model = new RefundApprovedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            PlanName = planName,
            Amount = notification.ApprovedAmount.ToString("F2"),
            Currency = notification.Currency,
            SupportWhatsApp = instructions?.SupportWhatsApp ?? string.Empty,
            SupportEmail = instructions?.SupportEmail ?? string.Empty
        };

        await notificationSender
            .For(notification.SubscriberId)
            .WithEmail(EmailTemplate.RefundApproved, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
