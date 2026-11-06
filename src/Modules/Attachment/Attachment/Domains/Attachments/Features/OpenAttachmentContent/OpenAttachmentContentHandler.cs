using Attachment.Contracts.Attachments.Queries;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Storage;

namespace Attachment.Domains.Attachments.Features.OpenAttachmentContent;

/// <summary>
/// Trusted (module-to-module) query: opens the stored file of a Ready attachment, no caller check. See
/// <see cref="OpenAttachmentContentQuery"/>.
/// </summary>
public class OpenAttachmentContentHandler(IAttachmentRepository repository, IStorageService storage)
    : IQueryHandler<OpenAttachmentContentQuery, OpenAttachmentContentResult>
{
    public async Task<OpenAttachmentContentResult> Handle(OpenAttachmentContentQuery query, CancellationToken cancellationToken)
    {
        var attachment = await repository.GetReadOnlyByIdAsync(query.AttachmentId, cancellationToken)
                         ?? throw new AttachmentNotFoundException(query.AttachmentId);

        var stream = await storage.OpenReadAsync(attachment.StoragePath, cancellationToken);
        return new OpenAttachmentContentResult(stream, attachment.OriginalFileName, attachment.ContentType,
            attachment.FileSize);
    }
}
