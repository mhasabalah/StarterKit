namespace Notification.Dispatch;

/// <param name="enqueuer">Job enqueuer.</param>
/// <param name="recipients">Optional mapping of non-user recipients (tenant subscribers…) to users.</param>
public sealed class NotificationSender(INotificationJobEnqueuer enqueuer, INotificationRecipientResolver? recipients = null)
    : INotificationSender
{
    public INotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En)
        => new NotificationBuilder(userId, culture, enqueuer, recipients: recipients);

    public INotificationBuilder ToEmail(
        string email,
        NotificationCulture culture = NotificationCulture.En,
        Guid? userId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new NotificationBuilder(
            userId: userId ?? Guid.Empty,
            culture: culture,
            enqueuer: enqueuer,
            emailOverride: email.Trim());
    }
}
