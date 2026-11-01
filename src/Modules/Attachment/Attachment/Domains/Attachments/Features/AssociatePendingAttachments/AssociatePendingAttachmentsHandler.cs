using Attachment.Contracts.Attachments.Commands;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.Storage;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.AssociatePendingAttachments;

public class AssociatePendingAttachmentsCommandValidator : AbstractValidator<AssociatePendingAttachmentsCommand>
{
    public AssociatePendingAttachmentsCommandValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage(_ => localizer["Validation.SessionId.Required"]);

        RuleFor(x => x.EntityId)
            .NotEmpty().WithMessage(_ => localizer["Validation.EntityId.Required"]);

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage(_ => localizer["Validation.UserId.Required"]);
    }
}

public class AssociatePendingAttachmentsHandler(
    IPendingAttachmentRepository pendingRepository,
    IAttachmentRepository attachmentRepository,
    IStorageService storageService,
    ILogger<AssociatePendingAttachmentsHandler> logger)
    : ICommandHandler<AssociatePendingAttachmentsCommand, AssociatePendingAttachmentsResult>
{
    public async Task<AssociatePendingAttachmentsResult> Handle(
        AssociatePendingAttachmentsCommand command,
        CancellationToken cancellationToken)
    {
        // Get all pending attachments for session
        var pendingAttachments = await pendingRepository.GetBySessionIdAsync(
            command.SessionId,
            cancellationToken);

        if (!pendingAttachments.Any())
        {
            logger.LogWarning(
                "No pending attachments found for session {SessionId}",
                command.SessionId);

            return new AssociatePendingAttachmentsResult(0, new List<Guid>(), new List<AssociatedAttachmentMetadata>());
        }

        // Filter out expired attachments
        var validAttachments = pendingAttachments.Where(p => !p.IsExpired()).ToList();

        if (!validAttachments.Any())
        {
            logger.LogWarning(
                "All pending attachments for session {SessionId} are expired",
                command.SessionId);

            return new AssociatePendingAttachmentsResult(0, new List<Guid>(), new List<AssociatedAttachmentMetadata>());
        }

        // Run all storage I/O in parallel — each file move is fully independent.
        // DB writes follow sequentially; DbContext is not thread-safe.
        var entityPrefix = command.EntityType.ToString().ToLower() + "s";

        var storageTasks = validAttachments.Select(async pending =>
        {
            try
            {
                var ext = Path.GetExtension(pending.FileName);
                var newPath = $"{entityPrefix}/{command.EntityId}/{pending.Id}{ext}";

                // Move instead of Copy — atomically removes the temp file, no storage leak.
                var moved = await storageService.MoveAsync(pending.StoragePath, newPath, cancellationToken);

                string? thumbPath = null;
                string? thumbUrl = null;

                if (!string.IsNullOrEmpty(pending.ThumbnailPath))
                {
                    var thumbExt = Path.GetExtension(pending.ThumbnailPath);
                    var newThumbPath = $"{entityPrefix}/{command.EntityId}/thumb_{pending.Id}{thumbExt}";
                    var movedThumb = await storageService.MoveAsync(pending.ThumbnailPath, newThumbPath, cancellationToken);
                    thumbPath = movedThumb.StoragePath;
                    thumbUrl = movedThumb.PublicUrl;
                }

                return (Ok: true, Pending: pending, Moved: moved, ThumbPath: thumbPath, ThumbUrl: thumbUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to move storage for pending attachment {PendingId} from session {SessionId}",
                    pending.Id, command.SessionId);

                return (Ok: false, Pending: pending, Moved: (StorageResult)null!,
                        ThumbPath: (string?)null, ThumbUrl: (string?)null);
            }
        });

        var storageResults = await Task.WhenAll(storageTasks);

        var associatedIds = new List<Guid>();
        var metadata = new List<AssociatedAttachmentMetadata>();

        foreach (var r in storageResults.Where(r => r.Ok))
        {
            var attachment = AttachmentFile.Create(
                r.Pending.Id,
                r.Pending.FileName,
                r.Pending.OriginalFileName,
                r.Pending.ContentType,
                r.Moved.FileSize,
                r.Moved.StoragePath,
                r.Moved.PublicUrl,
                r.Pending.Type,
                command.UserId,
                command.EntityType,
                command.EntityId,
                description: null,
                isPublic: command.IsPublic);

            if (r.ThumbPath != null && r.ThumbUrl != null
                && r.Pending.Width.HasValue && r.Pending.Height.HasValue)
            {
                attachment.SetThumbnail(r.ThumbPath, r.ThumbUrl,
                    r.Pending.Width.Value, r.Pending.Height.Value);
            }

            await attachmentRepository.AddAsync(attachment, cancellationToken);
            r.Pending.MarkAsAssociated();

            associatedIds.Add(attachment.Id);
            metadata.Add(new AssociatedAttachmentMetadata(
                attachment.Id, r.Moved.PublicUrl, r.Pending.DisplayOrder, r.Pending.IsPrimary));

            logger.LogInformation(
                "Associated pending attachment {PendingId} with {EntityType} {EntityId}",
                r.Pending.Id, command.EntityType, command.EntityId);
        }

        // Single SaveChanges — both repos share the same AttachmentDbContext.
        await attachmentRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Successfully associated {Count} pending attachments from session {SessionId} with {EntityType} {EntityId}",
            associatedIds.Count,
            command.SessionId,
            command.EntityType,
            command.EntityId);

        return new AssociatePendingAttachmentsResult(associatedIds.Count, associatedIds, metadata);
    }
}
