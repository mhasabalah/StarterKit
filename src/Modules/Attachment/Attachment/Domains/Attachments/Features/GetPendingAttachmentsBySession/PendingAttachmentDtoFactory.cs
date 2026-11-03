using Attachment.Contracts.Attachments.Queries;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Attachment.Domains.Attachments.Features.GetPendingAttachmentsBySession;

/// <summary>Builds pending DTOs with fresh short-lived signed preview URLs.</summary>
public sealed class PendingAttachmentDtoFactory(IStorageService storage, IOptions<AttachmentOptions> options)
{
    public async Task<PendingAttachmentDto> CreateAsync(PendingAttachment p, CancellationToken cancellationToken)
    {
        var download = options.Value.Download;
        var ttl = TimeSpan.FromMinutes(download.ReadUrlTtlMinutes);
        var inline = FileResponseHeaders.IsInline(p.ContentType, download.InlineContentTypes);

        var fileUrl = await storage.GetReadUrlAsync(
            p.StoragePath, ttl, new ReadUrlOptions(p.OriginalFileName, p.ContentType, inline), cancellationToken);

        var thumbnailUrl = string.IsNullOrEmpty(p.ThumbnailPath)
            ? null
            : await storage.GetReadUrlAsync(
                p.ThumbnailPath, ttl, new ReadUrlOptions($"thumb_{p.OriginalFileName}.jpg", "image/jpeg", true),
                cancellationToken);

        return new PendingAttachmentDto(
            p.Id,
            p.SessionId,
            p.FileName,
            p.OriginalFileName,
            p.ContentType,
            p.FileSize,
            fileUrl,
            thumbnailUrl,
            p.Type,
            p.Category,
            p.DisplayOrder,
            p.IsPrimary,
            p.Width,
            p.Height,
            p.ExpiresAt,
            p.CreatedAt,
            p.IsExpired());
    }
}
