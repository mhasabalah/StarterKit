namespace Notification.Dispatch;

/// <summary>
/// <see cref="INotificationBuilder"/> returned by <see cref="INotificationSender"/>.
/// Chain <c>.WithEmail()</c> and/or <c>.WithWhatsApp()</c>, then call <c>.SendAsync()</c>.
/// </summary>
internal sealed class NotificationBuilder : INotificationBuilder
{
    private readonly Guid _userId;
    private readonly NotificationCulture _culture;
    private readonly INotificationJobEnqueuer _enqueuer;
    private readonly string? _emailOverride;
    private readonly string? _phoneOverride;
    private readonly INotificationRecipientResolver? _recipients;
    private EmailPayload? _email;
    private WhatsAppPayload? _whatsApp;

    internal NotificationBuilder(
        Guid userId,
        NotificationCulture culture,
        INotificationJobEnqueuer enqueuer,
        string? emailOverride = null,
        string? phoneOverride = null,
        INotificationRecipientResolver? recipients = null)
    {
        _recipients = recipients;
        _userId = userId;
        _culture = culture;
        _enqueuer = enqueuer;
        _emailOverride = emailOverride;
        _phoneOverride = phoneOverride;
    }

    /// <summary>Attach an email payload. <paramref name="model"/> defaults to an empty dictionary.</summary>
    public INotificationBuilder WithEmail(
        EmailTemplate template,
        Dictionary<string, object>? model = null,
        EmailAttachment[]? attachments = null)
    {
        _email = new EmailPayload(template, model ?? [], attachments);
        return this;
    }

    /// <summary>Attach a WhatsApp payload. <paramref name="model"/> is optional for templates with no body parameters.</summary>
    public INotificationBuilder WithWhatsApp(
        WhatsAppTemplate template,
        Dictionary<string, object>? model = null)
    {
        _whatsApp = new WhatsAppPayload(template, model);
        return this;
    }

    /// <summary>
    /// Enqueues the notification for asynchronous dispatch.
    /// Pass <paramref name="forcedChannel"/> to bypass user channel preferences
    /// (e.g. when you know the message is email-only).
    /// Returns the correlation ID that groups all channel attempts in <c>notification_log</c>.
    /// </summary>
    public async Task<Guid> SendAsync(
        NotificationChannel? forcedChannel = null,
        CancellationToken ct = default)
    {
        var userId = _userId;
        if (_recipients is not null && userId != Guid.Empty &&
            await _recipients.ResolveUserIdAsync(userId, ct) is { } resolved)
        {
            userId = resolved;
        }

        var message = new NotificationMessage
        {
            UserId = userId,
            Culture = _culture,
            EmailOverride = _emailOverride,
            PhoneOverride = _phoneOverride,
            Email = _email,
            WhatsApp = _whatsApp
        };
        return await _enqueuer.EnqueueAsync(message, forcedChannel, ct);
    }
}
