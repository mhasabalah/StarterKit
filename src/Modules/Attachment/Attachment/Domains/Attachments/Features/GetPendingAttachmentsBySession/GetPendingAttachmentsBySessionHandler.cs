using Attachment.Contracts.Attachments.Queries;
using Attachment.Data.Repository.Attachments;
using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Attachment.Domains.Attachments.Features.GetPendingAttachmentsBySession;

public class GetPendingAttachmentsBySessionHandler(
    IPendingAttachmentRepository repository,
    IOptions<AttachmentOptions> options,
    ILogger<GetPendingAttachmentsBySessionHandler> logger)
    : IQueryHandler<GetPendingAttachmentsBySessionQuery, GetPendingAttachmentsBySessionResult>
{
    private readonly AttachmentOptions _options = options.Value;

    public async Task<GetPendingAttachmentsBySessionResult> Handle(
        GetPendingAttachmentsBySessionQuery query,
        CancellationToken cancellationToken)
    {
        var pendingAttachments = await repository.GetBySessionIdAsync(
            query.SessionId,
            cancellationToken);

        var dtos = pendingAttachments
            .Select(p => new PendingAttachmentDto(
                p.Id,
                p.SessionId,
                p.FileName,
                p.OriginalFileName,
                p.ContentType,
                p.FileSize,
                p.FileUrl,
                p.ThumbnailUrl,
                p.Type,
                p.Category,
                p.DisplayOrder,
                p.IsPrimary,
                p.Width,
                p.Height,
                p.ExpiresAt,
                p.CreatedAt,
                p.IsExpired()))
            .ToList();

        var totalCount = dtos.Count;
        var remainingSlots = Math.Max(0, _options.Pending.MaxUploadsPerSession - totalCount);

        logger.LogInformation(
            "Retrieved {Count} pending attachments for session {SessionId}, {Remaining} slots remaining",
            totalCount,
            query.SessionId,
            remainingSlots);

        return new GetPendingAttachmentsBySessionResult(dtos, totalCount, remainingSlots);
    }
}
