using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

public record UploadMultipleAttachmentsCommand(
    List<AttachmentUploadItem> Files,
    AttachmentCategory Category,
    Guid UploadedBy,
    EntityType? EntityType = null,
    Guid? EntityId = null,
    bool IsPublic = false,
    bool GenerateThumbnails = true)
    : ICommand<UploadMultipleAttachmentsResult>;

public record UploadMultipleAttachmentsResult(List<AttachmentUploadResultDto> Attachments);

public record AttachmentUploadItem(
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileStream,
    AttachmentType Type,
    string? Description = null);

public record LinkAttachmentToEntityCommand(
    Guid AttachmentId,
    EntityType EntityType,
    Guid EntityId)
    : ICommand<LinkAttachmentToEntityResult>;

public record LinkAttachmentToEntityResult(bool Success);

public record MakeAttachmentPublicCommand(
    Guid AttachmentId)
    : ICommand<MakeAttachmentPublicResult>;

public record MakeAttachmentPublicResult(bool Success);

public record MakeAttachmentPrivateCommand(
    Guid AttachmentId)
    : ICommand<MakeAttachmentPrivateResult>;

public record MakeAttachmentPrivateResult(bool Success);

public record GenerateThumbnailCommand(
    Guid AttachmentId)
    : ICommand<GenerateThumbnailResult>;

public record GenerateThumbnailResult(ThumbnailDto Thumbnail);

public record UpdateAttachmentDescriptionCommand(
    Guid AttachmentId,
    string? Description)
    : ICommand<UpdateAttachmentDescriptionResult>;

public record UpdateAttachmentDescriptionResult(bool Success);

/// <summary>
/// Deletes all attachment files linked to a specific entity (e.g., all images of a deleted product).
/// The handler removes both the physical files from storage and the database records.
/// </summary>
public record DeleteAttachmentsByEntityCommand(
    EntityType EntityType,
    Guid EntityId)
    : ICommand<DeleteAttachmentsByEntityResult>;

public record DeleteAttachmentsByEntityResult(int DeletedCount);
