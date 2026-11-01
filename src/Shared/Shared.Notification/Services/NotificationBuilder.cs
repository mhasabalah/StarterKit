using Shared.Email.Contracts;

namespace Shared.Notification.Services;

/// <summary>
/// Fluent builder returned by <see cref="INotificationSender.For"/>.
/// Chain <c>.WithEmail()</c> and/or <c>.WithWhatsApp()</c>, then call <c>.SendAsync()</c>.
/// </summary>
public sealed class NotificationBuilder
{
    private readonly Guid _userId;
    private readonly NotificationCulture _culture;
    private readonly INotificationJobEnqueuer _enqueuer;
    private readonly string? _emailOverride;
    private readonly string? _phoneOverride;
    private EmailPayload? _email;
    private WhatsAppPayload? _whatsApp;

    internal NotificationBuilder(
        Guid userId,
        NotificationCulture culture,
        INotificationJobEnqueuer enqueuer,
        string? emailOverride = null,
        string? phoneOverride = null)
    {
        _userId = userId;
        _culture = culture;
        _enqueuer = enqueuer;
        _emailOverride = emailOverride;
        _phoneOverride = phoneOverride;
    }

    /// <summary>Attach an email payload. <paramref name="model"/> defaults to an empty dictionary.</summary>
    public NotificationBuilder WithEmail(
        EmailTemplate template,
        Dictionary<string, object>? model = null,
        EmailAttachment[]? attachments = null)
    {
        _email = new EmailPayload(template, model ?? [], attachments);
        return this;
    }

    /// <summary>Attach a WhatsApp payload. <paramref name="model"/> is optional for templates with no body parameters.</summary>
    public NotificationBuilder WithWhatsApp(
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
    public Task<Guid> SendAsync(
        NotificationChannel? forcedChannel = null,
        CancellationToken ct = default)
    {
        var message = new NotificationMessage
        {
            UserId = _userId,
            Culture = _culture,
            EmailOverride = _emailOverride,
            PhoneOverride = _phoneOverride,
            Email = _email,
            WhatsApp = _whatsApp
        };
        return _enqueuer.EnqueueAsync(message, forcedChannel, ct);
    }
}
