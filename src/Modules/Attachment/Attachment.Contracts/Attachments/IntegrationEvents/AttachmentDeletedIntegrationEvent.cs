using MassTransit;
using Shared.Contracts.Messaging;

namespace Attachment.Contracts.Attachments.IntegrationEvents;

/// <summary>
/// Integration event: an attachment was deleted (by a user, an entity deletion, account erasure or the reconciliation
/// sweeper). Raised by the AttachmentFile aggregate when the deletion is requested and written to the Attachment outbox
/// in the same transaction. Modules that keep something derived from the file react, e.g. Agentic removes the
/// knowledge document indexed from it.
/// <para>
/// Carries the id only. Consumers are idempotent (at-least-once delivery): a second delivery finds nothing left.
/// </para>
/// </summary>
[MessageUrn("attachment.attachment-deleted.v1")]
public sealed class AttachmentDeletedIntegrationEvent : IntegrationEvent
{
    public Guid AttachmentId { get; init; }
}
