using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

/// <summary>
/// Command to upload an attachment file.
/// Category is automatically calculated from EntityType + Type - do not specify it.
/// </summary>
public record UploadAttachmentCommand(
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileStream,
    AttachmentType Type,
    EntityType? EntityType = null,
    Guid? EntityId = null,
    string? Description = null,
    bool IsPublic = false,
    bool GenerateThumbnail = true,
    Guid? UploadedBy = null)
    : ICommand<UploadAttachmentResult>;

public record UploadAttachmentResult(
    Guid Id,
    string FileName,
    string FileUrl,
    string? ThumbnailUrl,
    long FileSize,
    string ContentType);

