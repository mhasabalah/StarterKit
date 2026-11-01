namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class RefundRequestedEventHandler(
    ISubscriberInfoProvider subscriberInfoProvider,
    INotificationSender notificationSender,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext,
    ILogger<RefundRequestedEventHandler> logger)
    : INotificationHandler<RefundRequestedEvent>
{
    public async Task Handle(RefundRequestedEvent notification, CancellationToken cancellationToken)
    {
        var refund = await dbContext.RefundRequests
            .AsNoTracking()
            .Where(x => x.Id == notification.RefundRequestId)
            .Select(x => new { x.PlanName, x.RefundType })
            .FirstOrDefaultAsync(cancellationToken);

        var adminEmail = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogWarning("Admin email not configured — skipping refund notification for {RefundRequestId}",
                notification.RefundRequestId);
            return;
        }

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var model = new RefundRequestedModel
        {
            SubscriberName = subscriber?.DisplayName ?? "Unknown subscriber",
            PlanName = refund?.PlanName ?? "Plan",
            Amount = notification.RequestedAmount.ToString("F2"),
            Currency = notification.Currency,
            RefundType = refund?.RefundType.ToString() ?? string.Empty
        };

        await notificationSender
            .ToEmail(adminEmail)
            .WithEmail(EmailTemplate.RefundRequested, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
    }
}
