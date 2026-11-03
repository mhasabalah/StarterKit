namespace Notification.Contracts;

/// <summary>
/// Entry point for sending a notification (email and/or WhatsApp) to a person. Delivery is asynchronous (Hangfire):
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
    /// Enqueues the notification. <paramref name="forcedChannel"/> bypasses user channel preferences (use it for
    /// security mail that preferences must never suppress). Returns the correlation id of the delivery log.
    /// </summary>
    Task<Guid> SendAsync(NotificationChannel? forcedChannel = null, CancellationToken ct = default);
}
