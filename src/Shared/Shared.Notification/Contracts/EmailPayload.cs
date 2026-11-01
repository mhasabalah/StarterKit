using Shared.Email.Contracts;

namespace Shared.Notification.Contracts;

/// <summary>
/// Typed email payload for a notification message.
/// Carries the <see cref="EmailTemplate"/> enum value (compile-time safety)
/// and the model dictionary used to render the Liquid template.
/// </summary>
public sealed record EmailPayload(
    EmailTemplate Template,
    Dictionary<string, object> Model,
    EmailAttachment[]? Attachments = null
);
