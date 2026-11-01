namespace Shared.Notification.Services;

public sealed class NotificationSender(INotificationJobEnqueuer enqueuer) : INotificationSender
{
    public NotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En)
        => new(userId, culture, enqueuer);

    public NotificationBuilder ToEmail(
        string email,
        NotificationCulture culture = NotificationCulture.En)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new NotificationBuilder(
            userId: Guid.Empty,
            culture: culture,
            enqueuer: enqueuer,
            emailOverride: email);
    }
}
