using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

// Get Pending Attachments by Session (the session must be owned by the caller)
public record GetPendingAttachmentsBySessionQuery(Guid SessionId)
    : IQuery<GetPendingAttachmentsBySessionResult>;

public record GetPendingAttachmentsBySessionResult(
    List<PendingAttachmentDto> Attachments,
    int TotalCount,
    int RemainingSlots);

// Get Pending Attachment by ID
public record GetPendingAttachmentByIdQuery(Guid Id)
    : IQuery<PendingAttachmentDto?>;

// DTOs. FileUrl/ThumbnailUrl are short-lived signed read URLs generated per request (preview only).
public record PendingAttachmentDto(
    Guid Id,
    Guid SessionId,
    string FileName,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    string FileUrl,
    string? ThumbnailUrl,
    AttachmentType Type,
    AttachmentCategory Category,
    int DisplayOrder,
    bool IsPrimary,
    int? Width,
    int? Height,
    DateTime ExpiresAt,
    DateTime? CreatedAt,
    bool IsExpired);
