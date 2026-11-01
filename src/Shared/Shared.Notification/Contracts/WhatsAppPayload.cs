namespace Shared.Notification.Contracts;

/// <summary>
/// Typed WhatsApp payload for a notification message.
/// Carries the <see cref="WhatsAppTemplate"/> enum value (compile-time safety)
/// and an optional model dictionary for body parameter substitution.
/// </summary>
public sealed record WhatsAppPayload(
    WhatsAppTemplate Template,
    Dictionary<string, object>? Model = null
);
