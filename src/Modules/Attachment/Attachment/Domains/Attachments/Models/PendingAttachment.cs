using Shared.Core.MultiTenancy;
using Attachment.Domains.Attachments.Events;

namespace Attachment.Domains.Attachments.Models;

/// <summary>
/// Represents a temporary attachment uploaded before entity creation.
/// Used for session-based anonymous uploads that will later be associated with an entity.
/// </summary>
public class PendingAttachment : Aggregate<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    // Session Information
    public Guid SessionId { get; private set; }

    // File Information
    public string FileName { get; private set; } = default!;
    public string OriginalFileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long FileSize { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the uploaded bytes; carried to the attachment on association.</summary>
    public string? ContentHash { get; private set; }

    // Storage Paths (temporary location)
    public string StoragePath { get; private set; } = default!;
    public string FileUrl { get; private set; } = default!;
    public string? ThumbnailPath { get; private set; }
    public string? ThumbnailUrl { get; private set; }

    // Classification
    public AttachmentType Type { get; private set; }

    /// <summary>
    /// Category is derived data - automatically calculated from Type.
    /// For pending attachments (before entity association), defaults based on intended use.
    /// Recalculated when associated with entity.
    /// </summary>
    public AttachmentCategory Category { get; private set; }

    // Display Order
    public int DisplayOrder { get; private set; }

    // Primary Image Flag
    public bool IsPrimary { get; private set; }

    // Image Metadata
    public int? Width { get; private set; }
    public int? Height { get; private set; }

    // Lifecycle Management
    public DateTime ExpiresAt { get; private set; }
    public bool IsAssociated { get; private set; }
    public DateTime? AssociatedAt { get; private set; }

    private PendingAttachment() { }

    public static PendingAttachment Create(
        Guid id,
        Guid sessionId,
        string fileName,
        string originalFileName,
        string contentType,
        long fileSize,
        string storagePath,
        string fileUrl,
        AttachmentType type,
        AttachmentCategory intendedCategory,
        int displayOrder,
        bool isPrimary,
        int expirationHours,
        string? contentHash = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentException.ThrowIfNullOrEmpty(originalFileName);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        ArgumentException.ThrowIfNullOrEmpty(storagePath);
        ArgumentException.ThrowIfNullOrEmpty(fileUrl);

        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("SessionId cannot be empty.", nameof(sessionId));
        }

        if (fileSize <= 0)
        {
            throw new ArgumentException("File size must be greater than zero.", nameof(fileSize));
        }

        if (displayOrder < 0)
        {
            throw new ArgumentException("Display order cannot be negative.", nameof(displayOrder));
        }

        var pendingAttachment = new PendingAttachment
        {
            Id = id,
            SessionId = sessionId,
            FileName = fileName,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            FileSize = fileSize,
            ContentHash = contentHash,
            StoragePath = storagePath,
            FileUrl = fileUrl,
            Type = type,
            // For pending attachments, use intended category (will be recalculated on association)
            // This allows UI to specify ProductImage, ProductDocument, etc. before entity exists
            Category = intendedCategory,
            DisplayOrder = displayOrder,
            IsPrimary = isPrimary,
            ExpiresAt = DateTime.UtcNow.AddHours(expirationHours),
            IsAssociated = false
        };

        pendingAttachment.AddDomainEvent(new PendingAttachmentUploadedEvent(pendingAttachment));

        return pendingAttachment;
    }

    public void SetThumbnail(string thumbnailPath, string thumbnailUrl, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(thumbnailPath);
        ArgumentException.ThrowIfNullOrEmpty(thumbnailUrl);

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Width and height must be positive.");
        }

        ThumbnailPath = thumbnailPath;
        ThumbnailUrl = thumbnailUrl;
        Width = width;
        Height = height;

        AddDomainEvent(new PendingAttachmentThumbnailGeneratedEvent(Id, SessionId, thumbnailUrl));
    }

    public void MarkAsAssociated()
    {
        if (IsAssociated)
        {
            throw new ConflictException("Pending attachment is already associated.");
        }

        if (IsExpired())
        {
            throw new BadRequestException("Cannot associate an expired pending attachment.");
        }

        IsAssociated = true;
        AssociatedAt = DateTime.UtcNow;

        AddDomainEvent(new PendingAttachmentAssociatedEvent(Id, SessionId));
    }

    public void MarkAsDeleted()
    {
        if (IsAssociated)
        {
            throw new ConflictException("Cannot delete an associated pending attachment.");
        }

        IsDeleted = true;

        AddDomainEvent(
            new PendingAttachmentDeletedEvent(Id, SessionId, StoragePath, ThumbnailPath));
    }

    public void UpdateDisplayOrder(int newDisplayOrder)
    {
        if (newDisplayOrder < 0)
        {
            throw new ArgumentException("Display order cannot be negative.",
                nameof(newDisplayOrder));
        }

        DisplayOrder = newDisplayOrder;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow > ExpiresAt;
    }

    public bool BelongsToSession(Guid sessionId)
    {
        return SessionId == sessionId;
    }
}
