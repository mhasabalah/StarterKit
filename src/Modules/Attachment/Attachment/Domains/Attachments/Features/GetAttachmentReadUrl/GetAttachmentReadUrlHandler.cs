using Attachment.Contracts.Attachments.Queries;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Domains.Attachments.Features.DownloadAttachment;

namespace Attachment.Domains.Attachments.Features.GetAttachmentReadUrl;

/// <summary>
/// Trusted (module-to-module) query: a fresh signed read URL, no caller check. See
/// <see cref="GetAttachmentReadUrlQuery"/>.
/// </summary>
public class GetAttachmentReadUrlHandler(
    IAttachmentRepository repository,
    AttachmentReadUrlFactory readUrls)
    : IQueryHandler<GetAttachmentReadUrlQuery, GetAttachmentReadUrlResult>
{
    public async Task<GetAttachmentReadUrlResult> Handle(GetAttachmentReadUrlQuery query, CancellationToken cancellationToken)
    {
        var attachment = await repository.GetReadOnlyByIdAsync(query.AttachmentId, cancellationToken)
                         ?? throw new AttachmentNotFoundException(query.AttachmentId);

        var target = readUrls.Describe(attachment, query.Thumbnail);
        var (url, expiresAt) = await readUrls.CreateAsync(target, query.Ttl, cancellationToken);
        return new GetAttachmentReadUrlResult(url, expiresAt, target.FileName, target.ContentType);
    }
}
