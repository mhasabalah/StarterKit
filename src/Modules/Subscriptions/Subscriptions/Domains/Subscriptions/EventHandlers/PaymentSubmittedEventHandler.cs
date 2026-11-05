using System.Globalization;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.EventHandlers;

/// <summary>Tells the billing team (see <see cref="IAdminNotificationRecipients"/>) that a proof awaits review.</summary>
public class PaymentSubmittedEventHandler(
    ISubscriberInfoProvider subscriberInfoProvider,
    INotificationSender notificationSender,
    IAdminNotificationRecipients recipients,
    IConfiguration configuration,
    SubscriptionsDbContext dbContext,
    ILogger<PaymentSubmittedEventHandler> logger)
    : INotificationHandler<PaymentSubmittedEvent>
{
    public async Task Handle(PaymentSubmittedEvent notification, CancellationToken cancellationToken)
    {
        var adminEmails = await recipients.GetEmailsAsync(cancellationToken);
        if (adminEmails.Count == 0)
        {
            logger.LogWarning(
                "No admin notification recipients configured (Subscriptions:AdminNotifications) - skipping payment proof notification for {SubscriptionId}",
                notification.SubscriptionId);
            return;
        }

        var planName = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == notification.SubscriptionId)
            .Select(x => x.PlanName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var subscriber = await subscriberInfoProvider.GetAsync(notification.SubscriberId, cancellationToken);

        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new AdminPaymentProofReceivedModel
        {
            SubscriberName = subscriber?.DisplayName ?? "Unknown subscriber",
            ApplicantEmail = subscriber?.Email ?? string.Empty,
            PlanName = planName,
            ProofUploadedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC", CultureInfo.InvariantCulture),
            AdminReviewUrl = $"{frontendBaseUrl}/admin/subscriptions/{notification.SubscriptionId}"
        };

        foreach (var email in adminEmails)
        {
            await notificationSender
                .ToEmail(email)
                .WithEmail(EmailTemplate.AdminPaymentProofReceived, model.ToDictionary())
                .SendAsync(forcedChannel: NotificationChannel.Email, ct: cancellationToken);
        }

        logger.LogInformation(
            "{Count} admin(s) notified of payment proof for {SubscriptionId} by subscriber {SubscriberId}",
            adminEmails.Count,
            notification.SubscriptionId,
            notification.SubscriberId);
    }
}
