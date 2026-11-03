namespace Attachment.Contracts.Attachments.Dtos;

public record AttachmentDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = default!;
    public string OriginalFileName { get; init; } = default!;
    public string ContentType { get; init; } = default!;
    public long FileSize { get; init; }
    /// <summary>Stable API URL (<c>/api/v1/attachments/{id}/download</c>); access is checked per request.</summary>
    public string FileUrl { get; init; } = default!;
    /// <summary>Stable API URL of the thumbnail (<c>.../download?thumbnail=true</c>), or null.</summary>
    public string? ThumbnailUrl { get; init; }
    public AttachmentType Type { get; init; }
    public AttachmentCategory Category { get; init; }
    public EntityType? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public Guid UploadedBy { get; init; }
    public bool IsPublic { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public long Version { get; init; }

    /// <summary>
    /// Lowercase hex SHA-256 of the stored file (null for files stored before hashes were recorded). Equal hashes
    /// mean identical content.
    /// </summary>
    public string? ContentHash { get; init; }
}
