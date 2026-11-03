using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

/// <summary>
/// Stable, access-checked API URLs handed out in DTOs instead of stored (and possibly expiring or public)
/// provider URLs: <c>{PublicBaseUrl}/api/v1/attachments/{id}/download[?thumbnail=true]</c>.
/// </summary>
public interface IAttachmentUrls
{
    string File(Guid attachmentId);

    string Thumbnail(Guid attachmentId);

    string? ThumbnailOrNull(AttachmentFile attachment);

    AttachmentDto ToDto(AttachmentFile attachment);
}

public sealed class AttachmentUrls(IOptions<AttachmentOptions> options) : IAttachmentUrls
{
    public const string DownloadRouteTemplate = "/attachments/{id:guid}/download";

    private string BaseUrl => options.Value.Download.PublicBaseUrl?.TrimEnd('/') ?? string.Empty;

    public string File(Guid attachmentId) => $"{BaseUrl}/api/v1/attachments/{attachmentId}/download";

    public string Thumbnail(Guid attachmentId) => $"{File(attachmentId)}?thumbnail=true";

    public string? ThumbnailOrNull(AttachmentFile attachment) =>
        string.IsNullOrEmpty(attachment.ThumbnailPath) ? null : Thumbnail(attachment.Id);

    public AttachmentDto ToDto(AttachmentFile a) => new()
    {
        Id = a.Id,
        FileName = a.FileName,
        OriginalFileName = a.OriginalFileName,
        ContentType = a.ContentType,
        FileSize = a.FileSize,
        FileUrl = File(a.Id),
        ThumbnailUrl = ThumbnailOrNull(a),
        Type = a.Type,
        Category = a.Category,
        EntityType = a.EntityType,
        EntityId = a.EntityId,
        UploadedBy = a.UploadedBy,
        IsPublic = a.IsPublic,
        Width = a.Width,
        Height = a.Height,
        Description = a.Description,
        CreatedAt = a.CreatedAt ?? default,
        UpdatedAt = a.LastModified,
        Version = a.Version,
        ContentHash = a.ContentHash
    };
}
