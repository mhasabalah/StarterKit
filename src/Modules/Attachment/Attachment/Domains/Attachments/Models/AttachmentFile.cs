using Shared.Core.MultiTenancy;
using Attachment.Contracts.Attachments.IntegrationEvents;
using Attachment.Domains.Attachments.Events;

namespace Attachment.Domains.Attachments.Models;

/// <summary>
/// A stored file. Lifecycle (see <see cref="AttachmentStatus"/>):
/// <list type="number">
/// <item><see cref="StartUpload"/> inserts the row as <c>Uploading</c> BEFORE the blob is written, so a crash
/// between the two always leaves a row the sweeper can find (never an untracked blob).</item>
/// <item><see cref="MarkReady"/> records the real size and SHA-256 once the blob is stored and raises
/// <see cref="AttachmentUploadedEvent"/>. <see cref="Create"/> does both steps for blobs that already exist
/// (pending-upload association).</item>
/// <item><see cref="RequestDeletion"/> hides the row (<c>IsDeleted</c> + <c>PendingDeletion</c>) in the business
/// transaction; a background job deletes the blobs and then the row (<see cref="RecordDeletionFailure"/> schedules
/// retries).</item>
/// </list>
/// </summary>
public class AttachmentFile : Aggregate<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Length of a lowercase hex SHA-256.</summary>
    public const int ContentHashLength = 64;

    // File Information
    public string FileName { get; private set; } = default!;
    public string OriginalFileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long FileSize { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the stored bytes (set when the row becomes Ready).</summary>
    public string? ContentHash { get; private set; }

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

    // Lifecycle
    public AttachmentStatus Status { get; private set; }

    /// <summary>When the business deleted the attachment (the row is hidden since then).</summary>
    public DateTime? DeletionRequestedAt { get; private set; }

    /// <summary>Failed blob-deletion attempts so far.</summary>
    public int DeletionAttempts { get; private set; }

    /// <summary>Earliest time the sweeper retries the blob deletion.</summary>
    public DateTime? NextDeletionAttemptAt { get; private set; }

    public bool IsReady => Status == AttachmentStatus.Ready && !IsDeleted;

    /// <summary>
    /// Phase 1 of an upload: the row exists (status <c>Uploading</c>, invisible to reads) before any byte is
    /// written to <paramref name="storagePath"/>. No event is raised until <see cref="MarkReady"/>.
    /// </summary>
    public static AttachmentFile StartUpload(
        Guid id,
        string fileName,
        string originalFileName,
        string contentType,
        long expectedFileSize,
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
        EnsurePositiveSize(expectedFileSize);

        return new AttachmentFile
        {
            Id = id,
            FileName = fileName,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            FileSize = expectedFileSize,
            StoragePath = storagePath,
            FileUrl = fileUrl,
            Type = type,
            Category = AttachmentCategoryHelper.DetermineCategory(entityType, type), // ✅ Always calculated
            UploadedBy = uploadedBy,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            IsPublic = isPublic,
            Status = AttachmentStatus.Uploading
        };
    }

    /// <summary>
    /// A row for a blob that is ALREADY stored (e.g. a pending upload moved to its entity folder): created
    /// directly as <c>Ready</c> and raises <see cref="AttachmentUploadedEvent"/>.
    /// </summary>
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
        bool isPublic = false,
        string? contentHash = null)
    {
        var attachment = StartUpload(id, fileName, originalFileName, contentType, fileSize, storagePath, fileUrl,
            type, uploadedBy, entityType, entityId, description, isPublic);

        attachment.MarkReady(fileSize, contentHash, width: null, height: null);
        return attachment;
    }

    /// <summary>
    /// Phase 2 of an upload: the blob is stored. Records the real size, content hash and image dimensions and
    /// makes the row visible. Only valid from <c>Uploading</c>: a row the sweeper or a delete already claimed
    /// must not come back to life.
    /// </summary>
    public void MarkReady(long fileSize, string? contentHash, int? width, int? height)
    {
        if (Status != AttachmentStatus.Uploading || IsDeleted)
            throw new ConflictException($"Attachment {Id} is {Status} and cannot become Ready.");

        EnsurePositiveSize(fileSize);

        if (contentHash is not null &&
            (contentHash.Length != ContentHashLength || !contentHash.All(char.IsAsciiHexDigitLower)))
            throw new ArgumentException("Content hash must be a lowercase hex SHA-256.", nameof(contentHash));

        FileSize = fileSize;
        ContentHash = contentHash;
        Width = width ?? Width;
        Height = height ?? Height;
        Status = AttachmentStatus.Ready;

        AddDomainEvent(new AttachmentUploadedEvent(this));

        // Cross-module: the user's avatar changed (Identity mirrors it). Outbox, same transaction as Ready.
        if (Category == AttachmentCategory.UserAvatar && EntityType == Contracts.Attachments.Dtos.EntityType.User && EntityId is { } userId)
        {
            AddIntegrationEvent(new UserAvatarChangedIntegrationEvent { UserId = userId, AttachmentId = Id });
        }
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

    /// <summary>
    /// Business delete: the row is hidden at once (<c>IsDeleted</c>, <c>PendingDeletion</c>) and saved with the
    /// caller's change; the blobs are deleted later by <c>DeleteAttachmentFilesJob</c> or the sweeper. Valid from
    /// <c>Uploading</c> too: a racing upload then fails its <see cref="MarkReady"/> save on the version check.
    /// Idempotent.
    /// </summary>
    /// <returns>False when the deletion had already been requested.</returns>
    public bool RequestDeletion(DateTime utcNow)
    {
        if (Status == AttachmentStatus.PendingDeletion)
            return false;

        Status = AttachmentStatus.PendingDeletion;
        IsDeleted = true;
        DeletionRequestedAt = utcNow;
        NextDeletionAttemptAt = utcNow;

        AddDomainEvent(new AttachmentDeletedEvent(Id, StoragePath, ThumbnailPath));
        return true;
    }

    /// <summary>A blob deletion failed: count it and schedule the next attempt.</summary>
    public void RecordDeletionFailure(DateTime utcNow, TimeSpan retryAfter)
    {
        if (Status != AttachmentStatus.PendingDeletion)
            throw new InvalidOperationException($"Attachment {Id} is not pending deletion.");

        DeletionAttempts++;
        NextDeletionAttemptAt = utcNow.Add(retryAfter);
    }

    /// <summary>Storage paths owned by this row: the file and, when present, its thumbnail.</summary>
    public IReadOnlyList<string> OwnedStoragePaths() =>
        string.IsNullOrEmpty(ThumbnailPath) ? [StoragePath] : [StoragePath, ThumbnailPath];

    private static void EnsurePositiveSize(long fileSize)
    {
        if (fileSize <= 0)
        {
            throw new ArgumentException("File size must be greater than zero.", nameof(fileSize));
        }
    }

    // Access decisions live in IAttachmentAccessService (public / admin / uploader / entity policy).
}
