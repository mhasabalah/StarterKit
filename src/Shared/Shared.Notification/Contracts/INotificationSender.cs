namespace Shared.Notification.Contracts;

/// <summary>
/// Entry point for the fluent notification builder.
/// Inject this instead of <see cref="INotificationJobEnqueuer"/> at call sites
/// that send user-facing notifications — it handles channel selection,
/// user preferences, and typed template resolution automatically.
/// </summary>
public interface INotificationSender
{
    NotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En);
    NotificationBuilder ToEmail(string email, NotificationCulture culture = NotificationCulture.En);
}
