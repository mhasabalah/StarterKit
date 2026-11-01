using Attachment.Contracts.Attachments.Queries;
using Attachment.Data.Repository.Attachments;

namespace Attachment.Domains.Attachments.Features.GetPendingAttachmentById;

public class GetPendingAttachmentByIdHandler(
    IPendingAttachmentRepository repository,
    ILogger<GetPendingAttachmentByIdHandler> logger)
    : IQueryHandler<GetPendingAttachmentByIdQuery, PendingAttachmentDto?>
{
    public async Task<PendingAttachmentDto?> Handle(
        GetPendingAttachmentByIdQuery query,
        CancellationToken cancellationToken)
    {
        var pendingAttachment = await repository.GetByIdAsync(query.Id, cancellationToken);

        if (pendingAttachment == null)
        {
            logger.LogWarning("Pending attachment not found: {Id}", query.Id);
            return null;
        }

        return new PendingAttachmentDto(
            pendingAttachment.Id,
            pendingAttachment.SessionId,
            pendingAttachment.FileName,
            pendingAttachment.OriginalFileName,
            pendingAttachment.ContentType,
            pendingAttachment.FileSize,
            pendingAttachment.FileUrl,
            pendingAttachment.ThumbnailUrl,
            pendingAttachment.Type,
            pendingAttachment.Category,
            pendingAttachment.DisplayOrder,
            pendingAttachment.IsPrimary,
            pendingAttachment.Width,
            pendingAttachment.Height,
            pendingAttachment.ExpiresAt,
            pendingAttachment.CreatedAt,
            pendingAttachment.IsExpired());
    }
}
