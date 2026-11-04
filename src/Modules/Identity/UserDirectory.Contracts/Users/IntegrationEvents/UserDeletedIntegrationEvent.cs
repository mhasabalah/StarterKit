using MassTransit;
using Shared.Contracts.Messaging;

namespace UserDirectory.Contracts.Users.IntegrationEvents;

/// <summary>
/// Integration event: a user erased their own account (GDPR erasure / store account deletion). The user row is kept
/// (anonymized) for referential integrity; everything ELSE that is personal to the user must go. Every module that
/// holds personal data about <see cref="UserId"/> reacts: Attachment deletes the user's files, Tenancy removes the
/// user's memberships, Notification deletes the delivery log.
/// <para>
/// Carries the id only - no PII by design. Consumers are idempotent (at-least-once delivery): a second delivery
/// finds nothing left to do.
/// </para>
/// </summary>
[MessageUrn("userdirectory.user-deleted.v1")]
public sealed class UserDeletedIntegrationEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
}
