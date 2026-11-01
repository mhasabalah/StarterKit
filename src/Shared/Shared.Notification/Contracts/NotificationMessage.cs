using Shared.Email.Contracts;

namespace Shared.Notification.Contracts;

/// <summary>
/// Immutable, channel-agnostic notification intent.
/// Each channel's payload is typed to its own enum — no magic strings anywhere.
/// A message may carry one or both channel payloads; the dispatch job only
/// attempts channels for which a payload is present.
/// </summary>
public sealed record NotificationMessage
{
    public Guid UserId { get; init; }
    public NotificationCulture Culture { get; init; } = NotificationCulture.En;
    public string? EmailOverride { get; init; }
    public string? PhoneOverride { get; init; }

    /// <summary>Email payload — present only when an email should be sent.</summary>
    public EmailPayload? Email { get; init; }

    /// <summary>WhatsApp payload — present only when a WhatsApp message should be sent.</summary>
    public WhatsAppPayload? WhatsApp { get; init; }

    /// <summary>
    /// Derived label for logging and persistence.
    /// Prefers the email template name; falls back to WhatsApp; then "unknown".
    /// </summary>
    public string TemplateName => Email?.Template.ToString()
        ?? WhatsApp?.Template.ToString()
        ?? "unknown";

    /// <summary>
    /// Returns <c>true</c> when this message carries a payload for the given channel.
    /// Used by <c>NotificationDispatchJob</c> to skip channels with no payload rather
    /// than attempting delivery and failing.
    /// </summary>
    public bool HasPayloadFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email    => Email is not null,
        NotificationChannel.WhatsApp => WhatsApp is not null,
        _                            => false
    };
}
