using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

/// <summary>
/// Uploads a file into a pending (pre-entity) upload session.
/// <see cref="SessionId"/> must be a server-issued session owned by the caller
/// (<c>POST /attachments/pending/sessions</c>, or omit <c>sessionId</c> on the upload endpoint to get one).
/// </summary>
public record UploadPendingAttachmentCommand(
    Guid SessionId,
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileStream,
    AttachmentType Type,
    AttachmentCategory Category,
    int DisplayOrder = 0,
    bool IsPrimary = false)
    : ICommand<UploadPendingAttachmentResult>;

/// <param name="FileUrl">Short-lived signed read URL of the pending file (preview only; do not persist).</param>
public record UploadPendingAttachmentResult(
    Guid Id,
    Guid SessionId,
    string FileName,
    string FileUrl,
    DateTime ExpiresAt,
    int DisplayOrder);

/// <summary>
/// Moves the pending files of an upload session to permanent storage and links them to an entity.
/// <para>
/// The session must be owned by the caller: the server-issued session id is bound either to
/// <see cref="UserId"/> or to the anonymous uploader fingerprint of the current request. A session id that
/// was not issued by the server, or was issued to someone else, is rejected (403).
/// </para>
/// <para>
/// When <see cref="EnforceEntityAccessPolicy"/> is <c>true</c> (set by the public HTTP endpoint) the entity
/// type's <c>IAttachmentEntityAccessPolicy.CanAttachAsync</c> must allow the caller (deny by default).
/// Module code that just created/authorized the entity leaves it <c>false</c>.
/// </para>
/// </summary>
public record AssociatePendingAttachmentsCommand(
    Guid SessionId,
    EntityType EntityType,
    Guid EntityId,
    Guid UserId,
    bool IsPublic = true)
    : ICommand<AssociatePendingAttachmentsResult>
{
    /// <summary>Set by the Attachment HTTP endpoint. See the type summary.</summary>
    public bool EnforceEntityAccessPolicy { get; init; }
}

public record AssociatePendingAttachmentsResult(
    int TotalAssociated,
    List<Guid> AttachmentIds,
    List<AssociatedAttachmentMetadata> Metadata);

/// <param name="FileUrl">Stable API URL (<c>/api/v1/attachments/{id}/download</c>).</param>
public record AssociatedAttachmentMetadata(
    Guid Id,
    string FileUrl,
    int DisplayOrder,
    bool IsPrimary);

/// <summary>Deletes a pending attachment. The session must be owned by the caller.</summary>
public record DeletePendingAttachmentCommand(
    Guid Id,
    Guid SessionId)
    : ICommand<bool>;

/// <summary>Reorders the pending attachments of a session. The session must be owned by the caller.</summary>
public record ReorderPendingAttachmentsCommand(
    Guid SessionId,
    List<Guid> OrderedIds)
    : ICommand<bool>;

/// <summary>
/// Cleanup of expired pending attachments (internal, run by Hangfire). Processes batches of
/// <see cref="BatchSize"/> until no expired rows are left; rows whose files could not be deleted are kept
/// for the next run.
/// </summary>
public record CleanupExpiredPendingAttachmentsCommand(
    DateTime CutoffDate,
    int BatchSize = 100)
    : ICommand<CleanupExpiredPendingAttachmentsResult>;

public record CleanupExpiredPendingAttachmentsResult(
    int Cleaned,
    int FilesDeleted);
