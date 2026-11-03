using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

/// <summary>
/// Deletes all attachments linked to a specific entity (e.g., all images of a purged product).
/// The attachments disappear from every read immediately (soft delete, saved by this command, including uploads
/// still in progress for the entity); their files and rows are removed by a background job, retried until it
/// succeeds. <see cref="DeleteAttachmentsByEntityResult.DeletedCount"/> is the number of attachments marked.
/// Trusted callers only: the owning module decides when its entity is gone.
/// </summary>
public record DeleteAttachmentsByEntityCommand(
    EntityType EntityType,
    Guid EntityId)
    : ICommand<DeleteAttachmentsByEntityResult>;

public record DeleteAttachmentsByEntityResult(int DeletedCount);
