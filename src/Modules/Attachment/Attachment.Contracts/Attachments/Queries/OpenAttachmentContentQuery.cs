using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

/// <summary>
/// Opens the stored file of a Ready attachment for reading, as a stream (for module code that processes a file in
/// the background, e.g. a bulk import job). The caller must dispose <see cref="OpenAttachmentContentResult.Content"/>.
/// <para>
/// <b>Trusted callers only.</b> Like <see cref="GetAttachmentReadUrlQuery"/>, this query does NOT check who the
/// current user is: the calling module must have authorized the access first.
/// </para>
/// </summary>
public record OpenAttachmentContentQuery(Guid AttachmentId) : IQuery<OpenAttachmentContentResult>;

/// <param name="Content">The provider stream (not buffered). Not necessarily seekable.</param>
public sealed record OpenAttachmentContentResult(
    Stream Content,
    string FileName,
    string ContentType,
    long FileSize);
