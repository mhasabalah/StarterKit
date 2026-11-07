using MassTransit;
using Shared.Contracts.Messaging;

namespace UserDirectory.Contracts.Users.IntegrationEvents;

/// <summary>
/// Integration event: sign-in sessions of a user were ended (sign-out of a device, "sign out everywhere", lock, delete,
/// password/role change, refresh-token reuse). Long-lived connections (the notification hub) listen to it and close the
/// sockets of the revoked sessions at once, instead of waiting for the access token to expire.
/// <para>
/// Carries ids only. <see cref="AllSessions"/> = every session of <see cref="UserId"/> (then
/// <see cref="SessionIds"/> is null); otherwise <see cref="SessionIds"/> lists the ended sessions (the <c>sid</c>
/// claim = refresh-token family). Consumers are idempotent: closing a socket twice is a no-op. Not a public webhook
/// event.
/// </para>
/// </summary>
[MessageUrn("userdirectory.user-sessions-revoked.v1")]
public sealed class UserSessionsRevokedIntegrationEvent : IntegrationEvent
{
    public Guid UserId { get; init; }

    public IReadOnlyList<Guid>? SessionIds { get; init; }

    public bool AllSessions { get; init; }
}
