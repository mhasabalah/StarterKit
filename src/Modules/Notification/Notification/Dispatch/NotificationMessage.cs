using Notification.Contracts.InApp;
using Notification.Contracts.Push;

namespace Notification.Dispatch;

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

    /// <summary>In-app (inbox + real-time) payload. Delivered by <c>InAppDeliveryJob</c>, not the fallback chain.</summary>
    public InAppContent? InApp { get; init; }

    /// <summary>Push payload. Delivered by <c>PushDeliveryJob</c>, not the fallback chain.</summary>
    public PushContent? Push { get; init; }

    /// <summary>True when the email/WhatsApp fallback chain has something to deliver.</summary>
    public bool HasFallbackChainPayload => Email is not null || WhatsApp is not null;

    /// <summary>
    /// Derived label for logging and persistence.
    /// Prefers the email template name; falls back to WhatsApp, the in-app category, then "push" / "unknown".
    /// </summary>
    public string TemplateName => Email?.Template.ToString()
        ?? WhatsApp?.Template.ToString()
        ?? InApp?.Category
        ?? (Push is not null ? "push" : "unknown");

    /// <summary>
    /// Returns <c>true</c> when this message carries a payload for the given channel.
    /// Used by <c>NotificationDispatchJob</c> to skip channels with no payload rather
    /// than attempting delivery and failing.
    /// </summary>
    public bool HasPayloadFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => Email is not null,
        NotificationChannel.WhatsApp => WhatsApp is not null,
        NotificationChannel.InApp => InApp is not null,
        NotificationChannel.Push => Push is not null,
        _ => false
    };
}
