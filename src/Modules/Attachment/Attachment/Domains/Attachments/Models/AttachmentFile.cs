using Attachment.Domains.Attachments.Events;

namespace Attachment.Domains.Attachments.Models;

public class AttachmentFile : Aggregate<Guid>
{
    // File Information
    public string FileName { get; private set; } = default!;
    public string OriginalFileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long FileSize { get; private set; }

    // Storage Paths
    public string StoragePath { get; private set; } = default!;
    public string FileUrl { get; private set; } = default!;
    public string? ThumbnailPath { get; private set; }
    public string? ThumbnailUrl { get; private set; }

    // Classification
    public AttachmentType Type { get; private set; }

    /// <summary>
    /// Category is derived data - automatically calculated from EntityType + Type.
    /// Should never be set manually. Always use AttachmentCategoryHelper.DetermineCategory().
    /// </summary>
    public AttachmentCategory Category { get; private set; }

    // Entity Linking
    public EntityType? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }

    // Ownership & Access
    public Guid UploadedBy { get; private set; }
    public bool IsPublic { get; private set; }

    // Image Metadata
    public int? Width { get; private set; }
    public int? Height { get; private set; }

    // Additional Info
    public string? Description { get; private set; }

    public static AttachmentFile Create(
        Guid id,
        string fileName,
        string originalFileName,
        string contentType,
        long fileSize,
        string storagePath,
        string fileUrl,
        AttachmentType type,
        Guid uploadedBy,
        EntityType? entityType = null,
        Guid? entityId = null,
        string? description = null,
        bool isPublic = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentException.ThrowIfNullOrEmpty(originalFileName);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        ArgumentException.ThrowIfNullOrEmpty(storagePath);
        ArgumentException.ThrowIfNullOrEmpty(fileUrl);

        if (fileSize <= 0)
        {
            throw new ArgumentException("File size must be greater than zero.", nameof(fileSize));
        }

        var attachment = new AttachmentFile
        {
            Id = id,
            FileName = fileName,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            FileSize = fileSize,
            StoragePath = storagePath,
            FileUrl = fileUrl,
            Type = type,
            Category = AttachmentCategoryHelper.DetermineCategory(entityType, type), // ✅ Always calculated
            UploadedBy = uploadedBy,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            IsPublic = isPublic
        };

        attachment.AddDomainEvent(new AttachmentUploadedEvent(attachment));

        return attachment;
    }

    public void LinkToEntity(EntityType entityType, Guid entityId)
    {
        EntityType = entityType;
        EntityId = entityId;

        // Recalculate category when entity type changes
        Category = AttachmentCategoryHelper.DetermineCategory(entityType, Type);

        AddDomainEvent(new AttachmentLinkedToEntityEvent(Id, entityType, entityId));
    }

    public void SetThumbnail(string thumbnailPath, string thumbnailUrl, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(thumbnailPath);
        ArgumentException.ThrowIfNullOrEmpty(thumbnailUrl);

        ThumbnailPath = thumbnailPath;
        ThumbnailUrl = thumbnailUrl;
        Width = width;
        Height = height;

        AddDomainEvent(new AttachmentThumbnailGeneratedEvent(Id, thumbnailUrl));
    }

    public void UpdateDescription(string? description)
    {
        Description = description;

        AddDomainEvent(new AttachmentUpdatedEvent(this));
    }

    public void MakePublic()
    {
        if (!IsPublic)
        {
            IsPublic = true;

            AddDomainEvent(new AttachmentMadePublicEvent(Id));
        }
    }

    public void MakePrivate()
    {
        if (IsPublic)
        {
            IsPublic = false;

            AddDomainEvent(new AttachmentMadePrivateEvent(Id));
        }
    }

    public void MarkAsDeleted()
    {
        IsDeleted = true;

        AddDomainEvent(new AttachmentDeletedEvent(Id, StoragePath, ThumbnailPath));
    }

    public bool CanBeAccessedBy(Guid userId, bool isAdmin = false)
    {
        // Admins can access anything
        if (isAdmin)
        {
            return true;
        }

        // Public attachments can be accessed by anyone
        if (IsPublic && !IsDeleted)
        {
            return true;
        }

        // Owner can always access their attachments
        if (UploadedBy == userId)
        {
            return true;
        }

        return false;
    }
}
