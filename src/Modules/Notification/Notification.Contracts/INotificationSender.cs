using Notification.Contracts.InApp;
using Notification.Contracts.Push;

namespace Notification.Contracts;

/// <summary>
/// Entry point for sending a notification (email, WhatsApp, in-app inbox, push) to a person. Delivery is asynchronous (Hangfire):
/// channel order, user preferences, fallback, retries, idempotency and the delivery log are handled by the
/// Notification module. This is the ONLY way modules send email - there is no direct "send an email" API.
/// </summary>
public interface INotificationSender
{
    /// <summary>
    /// Notify a user (contact data resolved through the user directory at delivery time). <paramref name="userId"/>
    /// may also be a non-user recipient id that an <see cref="INotificationRecipientResolver"/> maps to a user.
    /// </summary>
    INotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En);

    /// <summary>
    /// Notify an explicit email address (invitations, password reset to the CONFIRMED address, admin digests).
    /// <paramref name="userId"/> (optional) only links the delivery log to a user; it never changes the recipient.
    /// </summary>
    INotificationBuilder ToEmail(string email, NotificationCulture culture = NotificationCulture.En, Guid? userId = null);
}

/// <summary>Fluent notification: attach one payload per channel, then <see cref="SendAsync"/>.</summary>
public interface INotificationBuilder
{
    /// <summary>Attach an email payload. <paramref name="model"/> defaults to an empty dictionary.</summary>
    INotificationBuilder WithEmail(
        EmailTemplate template,
        Dictionary<string, object>? model = null,
        EmailAttachment[]? attachments = null);

    /// <summary>Attach a WhatsApp payload. <paramref name="model"/> is optional for templates without body parameters.</summary>
    INotificationBuilder WithWhatsApp(WhatsAppTemplate template, Dictionary<string, object>? model = null);

    /// <summary>
    /// Attach an in-app payload: stored in the user's inbox and pushed in real time (SignalR) to their open sessions.
    /// ADDITIVE - delivered in addition to the email/WhatsApp fallback chain, not instead of it. Needs a user
    /// recipient (<see cref="INotificationSender.For"/> or <c>ToEmail(..., userId)</c>).
    /// </summary>
    INotificationBuilder WithInApp(InAppContent content);

    /// <summary>
    /// Attach a push payload, sent through FCM to every active device of the user (unless they turned push off).
    /// ADDITIVE like <see cref="WithInApp"/>.
    /// </summary>
    INotificationBuilder WithPush(PushContent content);

    /// <summary>Push the in-app title and body (call after <see cref="WithInApp"/>); the in-app data is reused.</summary>
    INotificationBuilder WithPush(bool sensitive = false);

    /// <summary>
    /// Enqueues the notification. <paramref name="forcedChannel"/> bypasses user channel preferences (use it for
    /// security mail that preferences must never suppress); it applies to the email/WhatsApp chain only - in-app and
    /// push payloads are always delivered when attached. Returns the correlation id of the delivery log.
    /// </summary>
    Task<Guid> SendAsync(NotificationChannel? forcedChannel = null, CancellationToken ct = default);
}
