using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

// Upload Pending Attachment
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

public record UploadPendingAttachmentResult(
    Guid Id,
    Guid SessionId,
    string FileName,
    string FileUrl,
    DateTime ExpiresAt,
    int DisplayOrder);

// Associate Pending Attachments
public record AssociatePendingAttachmentsCommand(
    Guid SessionId,
    EntityType EntityType,
    Guid EntityId,
    Guid UserId,
    bool IsPublic = true)
    : ICommand<AssociatePendingAttachmentsResult>;

public record AssociatePendingAttachmentsResult(
    int TotalAssociated,
    List<Guid> AttachmentIds,
    List<AssociatedAttachmentMetadata> Metadata);

public record AssociatedAttachmentMetadata(
    Guid Id,
    string FileUrl,
    int DisplayOrder,
    bool IsPrimary);

// Delete Pending Attachment
public record DeletePendingAttachmentCommand(
    Guid Id,
    Guid SessionId)
    : ICommand<bool>;

// Reorder Pending Attachments
public record ReorderPendingAttachmentsCommand(
    Guid SessionId,
    List<Guid> OrderedIds)
    : ICommand<bool>;

// Cleanup Expired Pending Attachments (Internal use by Hangfire)
public record CleanupExpiredPendingAttachmentsCommand(
    DateTime CutoffDate,
    int BatchSize = 100)
    : ICommand<CleanupExpiredPendingAttachmentsResult>;

public record CleanupExpiredPendingAttachmentsResult(
    int Cleaned,
    int FilesDeleted);
