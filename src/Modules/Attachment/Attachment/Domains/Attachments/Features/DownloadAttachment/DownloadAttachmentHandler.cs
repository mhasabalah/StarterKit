using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Attachment.Domains.Attachments.Features.DownloadAttachment;

/// <summary>Internal query behind <c>GET /attachments/{id}/download</c>.</summary>
public record DownloadAttachmentQuery(Guid Id, bool Thumbnail = false) : IQuery<DownloadAttachmentResult>;

/// <summary>Either a short-lived <see cref="RedirectUrl"/> (S3/Azure) or a <see cref="Content"/> stream (Local).</summary>
public sealed record DownloadAttachmentResult(
    string? RedirectUrl,
    Stream? Content,
    string FileName,
    string ContentType,
    bool Inline,
    bool IsPublic);

/// <summary>
/// Access: public file, admin, uploader, or the entity policy's CanRead. Anonymous callers get 401 for
/// private files, authenticated callers without access get 403. Only Ready attachments are found; the row is
/// loaded once (untracked) and the access check reuses it.
/// </summary>
public class DownloadAttachmentHandler(
    IAttachmentRepository repository,
    IAttachmentAccessService accessService,
    AttachmentReadUrlFactory readUrls,
    IStorageService storage,
    ILogger<DownloadAttachmentHandler> logger)
    : IQueryHandler<DownloadAttachmentQuery, DownloadAttachmentResult>
{
    public async Task<DownloadAttachmentResult> Handle(DownloadAttachmentQuery query, CancellationToken cancellationToken)
    {
        var attachment = await repository.GetReadOnlyByIdAsync(query.Id, cancellationToken)
                         ?? throw new AttachmentNotFoundException(query.Id);

        if (!await accessService.CanReadAsync(attachment, cancellationToken))
        {
            logger.LogWarning("User {UserId} denied download of attachment {AttachmentId}",
                accessService.Current.UserId, query.Id);

            if (!accessService.Current.IsAuthenticated)
                throw new UnauthorizedException("Authentication is required to download this file.");

            throw new ForbiddenException("You do not have permission to download this file.");
        }

        var target = readUrls.Describe(attachment, query.Thumbnail);

        if (storage.ServesThroughApplication)
        {
            // The provider stream itself (a FileStream for Local): the endpoint streams it, nothing is buffered.
            var stream = await storage.OpenReadAsync(target.StoragePath, cancellationToken);
            return new DownloadAttachmentResult(null, stream, target.FileName, target.ContentType, target.Inline, attachment.IsPublic);
        }

        var url = await readUrls.CreateAsync(target, null, cancellationToken);
        return new DownloadAttachmentResult(url.Url, null, target.FileName, target.ContentType, target.Inline, attachment.IsPublic);
    }
}

/// <summary>Builds signed read URLs (and their response headers) for stored attachments.</summary>
public sealed class AttachmentReadUrlFactory(IStorageService storage, IOptions<AttachmentOptions> options)
{
    public sealed record ReadTarget(string StoragePath, string FileName, string ContentType, bool Inline);

    public ReadTarget Describe(AttachmentFile attachment, bool thumbnail)
    {
        if (thumbnail)
        {
            if (string.IsNullOrEmpty(attachment.ThumbnailPath))
                throw new NotFoundException("This attachment has no thumbnail.");

            return new ReadTarget(attachment.ThumbnailPath,
                $"thumb_{Path.GetFileNameWithoutExtension(attachment.OriginalFileName)}.jpg", "image/jpeg", true);
        }

        var inline = FileResponseHeaders.IsInline(attachment.ContentType, options.Value.Download.InlineContentTypes);
        return new ReadTarget(attachment.StoragePath, attachment.OriginalFileName, attachment.ContentType, inline);
    }

    public async Task<(string Url, DateTime ExpiresAt)> CreateAsync(
        ReadTarget target, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        var lifetime = ttl ?? TimeSpan.FromMinutes(options.Value.Download.ReadUrlTtlMinutes);
        var url = await storage.GetReadUrlAsync(
            target.StoragePath, lifetime, new ReadUrlOptions(target.FileName, target.ContentType, target.Inline),
            cancellationToken);
        return (url, DateTime.UtcNow.Add(lifetime));
    }
}
