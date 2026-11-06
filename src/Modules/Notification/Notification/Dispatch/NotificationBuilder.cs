using Notification.Contracts.InApp;
using Notification.Contracts.Push;

namespace Notification.Dispatch;

/// <summary>
/// <see cref="INotificationBuilder"/> returned by <see cref="INotificationSender"/>.
/// Chain <c>.WithEmail()</c>, <c>.WithWhatsApp()</c>, <c>.WithInApp()</c> and/or <c>.WithPush()</c>, then call <c>.SendAsync()</c>.
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
    private InAppContent? _inApp;
    private PushContent? _push;
    private bool? _pushFromInAppSensitive;
    private string? _category;

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

    public INotificationBuilder WithInApp(InAppContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Title);
        ArgumentNullException.ThrowIfNull(content.Body);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Category);
        if (content.Category.Length > InAppContent.CategoryMaxLength)
            throw new ArgumentException($"Category must not exceed {InAppContent.CategoryMaxLength} characters.", nameof(content));
        if (content.Data is { Count: > InAppContent.MaxDataEntries })
            throw new ArgumentException($"Data must not exceed {InAppContent.MaxDataEntries} entries.", nameof(content));
        _inApp = content;
        return this;
    }

    public INotificationBuilder WithPush(PushContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Title);
        ArgumentNullException.ThrowIfNull(content.Body);
        if (content.Data is { Count: > PushContent.MaxDataEntries })
            throw new ArgumentException($"Data must not exceed {PushContent.MaxDataEntries} entries.", nameof(content));
        _push = content;
        _pushFromInAppSensitive = null;
        return this;
    }

    public INotificationBuilder WithPush(bool sensitive = false)
    {
        // Resolved at SendAsync so WithInApp may come before or after this call.
        _push = null;
        _pushFromInAppSensitive = sensitive;
        return this;
    }

    public INotificationBuilder InCategory(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (category.Length > InAppContent.CategoryMaxLength)
            throw new ArgumentException($"Category must not exceed {InAppContent.CategoryMaxLength} characters.", nameof(category));
        _category = category;
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

        var push = ResolvePush();
        var message = new NotificationMessage
        {
            UserId = userId,
            Culture = _culture,
            EmailOverride = _emailOverride,
            PhoneOverride = _phoneOverride,
            Email = _email,
            WhatsApp = _whatsApp,
            InApp = _inApp,
            Push = push,
            Category = _category ?? _inApp?.Category ?? push?.Data?.GetValueOrDefault("category")
        };
        return await _enqueuer.EnqueueAsync(message, forcedChannel, ct);
    }

    private PushContent? ResolvePush()
    {
        if (_push is not null)
            return _push;
        if (_pushFromInAppSensitive is not { } sensitive)
            return null;

        var inApp = _inApp ?? throw new InvalidOperationException(
            "WithPush() without content reuses the in-app payload: call WithInApp(...) as well, or pass a PushContent.");

        var data = new Dictionary<string, string>(inApp.Data ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["category"] = inApp.Category
        };
        if (!string.IsNullOrWhiteSpace(inApp.ActionUrl))
            data["actionUrl"] = inApp.ActionUrl;

        return new PushContent(inApp.Title, inApp.Body, data, sensitive);
    }
}
