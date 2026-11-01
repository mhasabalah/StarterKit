namespace Attachment.Contracts.Attachments.Dtos;

public record AttachmentUploadResultDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = default!;
    public string FileUrl { get; init; } = default!;
    public string? ThumbnailUrl { get; init; }
    public long FileSize { get; init; }
    public string ContentType { get; init; } = default!;
    public AttachmentType Type { get; init; }
    public AttachmentCategory Category { get; init; }
}

public record AttachmentMetadataDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = default!;
    public string OriginalFileName { get; init; } = default!;
    public string ContentType { get; init; } = default!;
    public long FileSize { get; init; }
    public AttachmentType Type { get; init; }
    public AttachmentCategory Category { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool IsPublic { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record ThumbnailDto
{
    public Guid AttachmentId { get; init; }
    public string ThumbnailUrl { get; init; } = default!;
    public int Width { get; init; }
    public int Height { get; init; }
}

public record AttachmentListDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = default!;
    public string FileUrl { get; init; } = default!;
    public string? ThumbnailUrl { get; init; }
    public AttachmentType Type { get; init; }
    public AttachmentCategory Category { get; init; }
    public long FileSize { get; init; }
    public DateTime CreatedAt { get; init; }
}
