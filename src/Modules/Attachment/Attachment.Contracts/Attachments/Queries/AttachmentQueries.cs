using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

public record GetUserAttachmentsQuery(
    Guid UserId,
    AttachmentCategory? Category = null,
    AttachmentType? Type = null,
    int PageNumber = 1,
    int PageSize = 10)
    : IQuery<GetUserAttachmentsResult>;

public record GetUserAttachmentsResult(PagedResult<AttachmentListDto> Result);

public record GetAttachmentsByTypeQuery(
    AttachmentType Type,
    EntityType? EntityType = null,
    Guid? EntityId = null,
    int PageNumber = 1,
    int PageSize = 10)
    : IQuery<GetAttachmentsByTypeResult>;

public record GetAttachmentsByTypeResult(PagedResult<AttachmentListDto> Result);

public record GetAttachmentsByCategoryQuery(
    AttachmentCategory Category,
    bool PublicOnly = false,
    int PageNumber = 1,
    int PageSize = 10)
    : IQuery<GetAttachmentsByCategoryResult>;

public record GetAttachmentsByCategoryResult(PagedResult<AttachmentListDto> Result);

public record GetAttachmentMetadataQuery(
    Guid AttachmentId)
    : IQuery<GetAttachmentMetadataResult>;

public record GetAttachmentMetadataResult(AttachmentMetadataDto Metadata);

public record DownloadAttachmentQuery(
    Guid AttachmentId)
    : IQuery<AttachmentDownloadResult>;

public record GetThumbnailQuery(
    Guid AttachmentId)
    : IQuery<ThumbnailDownloadResult>;

public record AttachmentDownloadResult(
    Stream FileStream,
    string FileName,
    string ContentType);

public record ThumbnailDownloadResult(
    Stream ThumbnailStream,
    string ContentType);

public record PagedResult<T>
{
    public List<T> Items { get; init; } = new();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
