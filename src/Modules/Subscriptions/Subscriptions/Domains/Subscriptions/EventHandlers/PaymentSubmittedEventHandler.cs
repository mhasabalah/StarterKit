namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class PaymentSubmittedEventHandler(
    ISubscriberInfoProvider subscriberInfoProvider,
    INotificationSender notificationSender,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext,
    ILogger<PaymentSubmittedEventHandler> logger)
    : INotificationHandler<PaymentSubmittedEvent>
{
    public async Task Handle(PaymentSubmittedEvent notification, CancellationToken cancellationToken)
    {
        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var adminEmail = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogWarning(
                "Admin email not configured — skipping payment proof notification for {SubscriptionId}",
                notification.SubscriptionId);
            return;
        }

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new AdminPaymentProofReceivedModel
        {
            SubscriberName = subscriber?.DisplayName ?? "Unknown subscriber",
            ApplicantEmail = subscriber?.Email ?? string.Empty,
            PlanName = planName,
            ProofUploadedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC"),
            AdminReviewUrl = $"{frontendBaseUrl}/admin/subscriptions/{notification.SubscriptionId}"
        };

        await notificationSender
            .ToEmail(adminEmail)
            .WithEmail(EmailTemplate.AdminPaymentProofReceived, model.ToDictionary())
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);

        logger.LogInformation(
            "Admin notified of payment proof for {SubscriptionId} by subscriber {SubscriberId}",
            notification.SubscriptionId,
            notification.SubscriberId);
    }
}
