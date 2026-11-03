using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

/// <summary>
/// Returns a fresh, short-lived URL that lets a browser read the file of an attachment
/// (S3 presigned GET, Azure SAS, or a signed application route for Local storage).
/// <para>
/// <b>Trusted callers only.</b> This query does NOT check who the current user is: the calling module must
/// have authorized the request first (for example "the payment belongs to this subscriber, or the caller is
/// an admin"). HTTP clients use <c>GET /attachments/{id}/download</c>, which performs the access check.
/// </para>
/// </summary>
/// <param name="AttachmentId">The attachment to read.</param>
/// <param name="Thumbnail">Read the thumbnail instead of the original file.</param>
/// <param name="Ttl">URL lifetime; defaults to <c>Attachment:Download:ReadUrlTtlMinutes</c> (5 minutes).</param>
public record GetAttachmentReadUrlQuery(Guid AttachmentId, bool Thumbnail = false, TimeSpan? Ttl = null)
    : IQuery<GetAttachmentReadUrlResult>;

public record GetAttachmentReadUrlResult(
    string Url,
    DateTime ExpiresAt,
    string FileName,
    string ContentType);
