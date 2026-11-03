using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

/// <summary>
/// Ready attachments of an entity, newest first, one page at a time (filtered and paged in SQL).
/// </summary>
/// <param name="PageNumber">1-based page; null = 1.</param>
/// <param name="PageSize">
/// Page size, 1..<c>Paging:MaxPageSize</c>. Null = <c>Paging:MaxPageSize</c> (default 100), so callers that do not
/// page get the same list as before for any normal entity, but never an unbounded one.
/// </param>
public record GetAttachmentsByEntityQuery(
    EntityType EntityType,
    Guid EntityId,
    int? PageNumber = null,
    int? PageSize = null)
    : IQuery<GetAttachmentsByEntityResult>;

/// <param name="Attachments">The requested page.</param>
public record GetAttachmentsByEntityResult(List<AttachmentDto> Attachments)
{
    /// <summary>Attachments the caller can see in total (all pages).</summary>
    public int TotalCount { get; init; }

    /// <summary>The page returned (after clamping).</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>The page size used (after clamping).</summary>
    public int PageSize { get; init; }
}
