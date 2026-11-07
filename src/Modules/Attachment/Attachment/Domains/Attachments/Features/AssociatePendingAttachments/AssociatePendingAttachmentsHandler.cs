using Attachment.Contracts.Attachments.Commands;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.Security;
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

/// <summary>
/// Moves the files of an owned session to the entity folder and creates the attachment rows.
/// Consistency: if the save fails, every moved file is moved back to its pending path (the pending rows
/// are untouched), so the session can be associated again.
/// Race with the thumbnail job: it may save a pending row (thumbnail path + version) between our read and our save.
/// That conflict is not the user's doing, so the rows are re-read once and, when only a thumbnail appeared, that
/// thumbnail is moved and attached too and the save is repeated. Any other change (deleted, already associated)
/// still fails and moves the files back.
/// </summary>
public class AssociatePendingAttachmentsHandler(
    IPendingAttachmentRepository pendingRepository,
    IAttachmentRepository attachmentRepository,
    IStorageService storageService,
    IPendingSessionService sessionService,
    IAttachmentAccessService accessService,
    IAttachmentUrls urls,
    ILogger<AssociatePendingAttachmentsHandler> logger)
    : ICommandHandler<AssociatePendingAttachmentsCommand, AssociatePendingAttachmentsResult>
{
    private sealed record Move(PendingAttachment Pending, StorageResult File, string? ThumbPath)
    {
        /// <summary>A thumbnail the thumbnail job committed after our first read, moved during the retry.</summary>
        public string? LateThumbPath { get; set; }

        public AttachmentFile? Attachment { get; set; }
    }

    private const int MaxSaveAttempts = 2;

    public async Task<AssociatePendingAttachmentsResult> Handle(
        AssociatePendingAttachmentsCommand command,
        CancellationToken cancellationToken)
    {
        if (!sessionService.IsOwnedBy(command.SessionId, command.UserId))
            throw new PendingSessionAccessDeniedException();

        if (command.EnforceEntityAccessPolicy)
            await accessService.EnsureCanAttachToEntityAsync(command.EntityType, command.EntityId, cancellationToken);

        var pending = (await pendingRepository.GetBySessionIdAsync(command.SessionId, cancellationToken))
            .Where(p => !p.IsExpired())
            .ToList();

        if (pending.Count == 0)
        {
            logger.LogWarning("No live pending attachments for session {SessionId}", command.SessionId);
            return new AssociatePendingAttachmentsResult(0, [], []);
        }

        // Storage I/O in parallel (independent files); DB work sequential (DbContext is not thread-safe).
        var moves = await Task.WhenAll(pending.Select(p => TryMoveAsync(p, command, cancellationToken)));
        var moved = moves.Where(m => m is not null).Select(m => m!).ToList();

        var associatedIds = new List<Guid>();
        var metadata = new List<AssociatedAttachmentMetadata>();

        try
        {
            foreach (var m in moved)
            {
                var attachment = AttachmentFile.Create(
                    m.Pending.Id,
                    m.Pending.FileName,
                    m.Pending.OriginalFileName,
                    m.Pending.ContentType,
                    m.File.FileSize,
                    m.File.StoragePath,
                    urls.File(m.Pending.Id),
                    m.Pending.Type,
                    command.UserId,
                    command.EntityType,
                    command.EntityId,
                    description: null,
                    isPublic: command.IsPublic,
                    contentHash: m.Pending.ContentHash);

                if (m.ThumbPath is not null && m.Pending is { Width: { } w, Height: { } h })
                    attachment.SetThumbnail(m.ThumbPath, urls.Thumbnail(attachment.Id), w, h);

                await attachmentRepository.AddAsync(attachment, cancellationToken);
                m.Pending.MarkAsAssociated();
                m.Attachment = attachment;

                associatedIds.Add(attachment.Id);
                metadata.Add(new AssociatedAttachmentMetadata(
                    attachment.Id, urls.File(attachment.Id), m.Pending.DisplayOrder, m.Pending.IsPrimary));
            }

            // Single SaveChanges: both repositories share the same AttachmentDbContext.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await attachmentRepository.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (Exception ex) when (attempt < MaxSaveAttempts && ex.IsConcurrencyConflict())
                {
                    logger.LogInformation(
                        "Pending attachments of session {SessionId} changed during associate (thumbnail); retrying",
                        command.SessionId);
                    if (!await ReconcileAfterConflictAsync(moved, command, cancellationToken))
                        throw;
                }
            }
        }
        catch
        {
            await MoveBackAsync(moved);
            throw;
        }

        logger.LogInformation(
            "Associated {Count} pending attachments of session {SessionId} with {EntityType} {EntityId}",
            associatedIds.Count, command.SessionId, command.EntityType, command.EntityId);

        return new AssociatePendingAttachmentsResult(associatedIds.Count, associatedIds, metadata);
    }

    private async Task<Move?> TryMoveAsync(
        PendingAttachment pending, AssociatePendingAttachmentsCommand command, CancellationToken ct)
    {
        try
        {
            var extension = Path.GetExtension(pending.StoragePath);
            var file = await storageService.MoveAsync(
                pending.StoragePath,
                StoragePaths.ForEntity(pending.TenantId, command.EntityType, command.EntityId, pending.Id, extension),
                ct);

            string? thumbPath = null;
            if (!string.IsNullOrEmpty(pending.ThumbnailPath))
            {
                try
                {
                    thumbPath = (await storageService.MoveAsync(
                        pending.ThumbnailPath,
                        StoragePaths.ForEntityThumbnail(pending.TenantId, command.EntityType, command.EntityId, pending.Id),
                        ct)).StoragePath;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Thumbnail of pending {PendingId} could not be moved", pending.Id);
                }
            }

            return new Move(pending, file, thumbPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to move pending attachment {PendingId} of session {SessionId}",
                pending.Id, command.SessionId);
            return null;
        }
    }

    /// <summary>
    /// Re-reads every moved pending row after a concurrency conflict. Returns false (give up, the caller rethrows) when
    /// a row is gone, deleted or associated by someone else; otherwise moves any thumbnail that appeared meanwhile,
    /// attaches it, and re-marks the rows associated (the reload discarded that change).
    /// </summary>
    private async Task<bool> ReconcileAfterConflictAsync(
        List<Move> moved, AssociatePendingAttachmentsCommand command, CancellationToken ct)
    {
        foreach (var m in moved)
        {
            if (!await pendingRepository.ReloadAsync(m.Pending, ct) || m.Pending.IsDeleted || m.Pending.IsAssociated)
                return false;

            if (m.ThumbPath is null && m.LateThumbPath is null && !string.IsNullOrEmpty(m.Pending.ThumbnailPath)
                && m.Pending is { Width: { } w, Height: { } h } && m.Attachment is { } attachment)
            {
                try
                {
                    m.LateThumbPath = (await storageService.MoveAsync(
                        m.Pending.ThumbnailPath,
                        StoragePaths.ForEntityThumbnail(m.Pending.TenantId, command.EntityType, command.EntityId, m.Pending.Id),
                        ct)).StoragePath;
                    attachment.SetThumbnail(m.LateThumbPath, urls.Thumbnail(attachment.Id), w, h);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Same rule as the first pass: a thumbnail that cannot be moved never fails the associate.
                    logger.LogWarning(ex, "Late thumbnail of pending {PendingId} could not be moved", m.Pending.Id);
                }
            }

            m.Pending.MarkAsAssociated();
        }

        return true;
    }

    private async Task MoveBackAsync(IEnumerable<Move> moves)
    {
        foreach (var m in moves)
        {
            await TryMoveBackAsync(m.File.StoragePath, m.Pending.StoragePath);
            if (m.ThumbPath is not null && m.Pending.ThumbnailPath is not null)
                await TryMoveBackAsync(m.ThumbPath, m.Pending.ThumbnailPath);
            if (m.LateThumbPath is not null && m.Pending.ThumbnailPath is not null)
                await TryMoveBackAsync(m.LateThumbPath, m.Pending.ThumbnailPath);
        }
    }

    private async Task TryMoveBackAsync(string from, string to)
    {
        try
        {
            await storageService.MoveAsync(from, to, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Associate failed and {From} could not be moved back to {To}", from, to);
        }
    }
}
