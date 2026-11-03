using MassTransit;
using Shared.Contracts.Messaging;

namespace Attachment.Contracts.Attachments.IntegrationEvents;

/// <summary>
/// Integration event: a user's avatar upload became Ready (it replaces any previous avatar). Raised by the
/// AttachmentFile aggregate and written to the Attachment outbox in the same transaction.
/// <para>
/// A NOTIFICATION, not the state: consumers re-read the current avatar with <c>GetUserAvatarQuery</c> instead of
/// trusting <see cref="AttachmentId"/>, so out-of-order or duplicate deliveries still converge on the newest avatar.
/// </para>
/// </summary>
[MessageUrn("attachment.user-avatar-changed.v1")]
public sealed class UserAvatarChangedIntegrationEvent : IntegrationEvent
{
    public Guid UserId { get; init; }

    /// <summary>The avatar attachment that became Ready (informational).</summary>
    public Guid AttachmentId { get; init; }
}
