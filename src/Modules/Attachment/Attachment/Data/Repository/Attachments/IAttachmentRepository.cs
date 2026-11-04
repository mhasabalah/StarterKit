namespace Attachment.Data.Repository.Attachments;

/// <summary>
/// Attachment persistence. Unless a method says otherwise it only sees <b>Ready</b>, not-deleted rows (global
/// query filter); <c>Uploading</c> and <c>PendingDeletion</c> rows are reachable only through the explicit
/// maintenance methods.
/// </summary>
public interface IAttachmentRepository
{
    /// <summary>Tracked Ready attachment (for commands that change it), or null.</summary>
    Task<AttachmentFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Untracked Ready attachment (read paths: metadata, download, read URL), or null.</summary>
    Task<AttachmentFile?> GetReadOnlyByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tracked Ready attachments of an entity, newest first.</summary>
    Task<List<AttachmentFile>> GetByEntityAsync(
        EntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the Ready attachments of an entity, filtered in SQL: all of them when
    /// <paramref name="includePrivate"/>, otherwise only public ones plus the ones uploaded by
    /// <paramref name="uploaderId"/>. Newest first (ties broken by id). Untracked.
    /// </summary>
    Task<(List<AttachmentFile> Items, int TotalCount)> GetByEntityVisiblePageAsync(
        EntityType entityType,
        Guid entityId,
        bool includePrivate,
        Guid? uploaderId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maintenance: tracked rows of an entity that are still alive (Ready or Uploading), ignoring the read filter.
    /// Used by delete-by-entity so an upload racing the delete is claimed too.
    /// </summary>
    Task<List<AttachmentFile>> GetLiveByEntityAsync(
        EntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Maintenance: tracked row in ANY status (ignores the read filter), or null.</summary>
    Task<AttachmentFile?> GetForMaintenanceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Account erasure: tracked PERSONAL files (<c>EntityType.User</c> with the user as entity, i.e. the avatar) in any
    /// status except already pending deletion. Tenant-owned files the user merely uploaded are NOT included.
    /// </summary>
    Task<List<AttachmentFile>> GetUploadedByForErasureAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(AttachmentFile attachment, CancellationToken cancellationToken = default);

    /// <summary>Maintenance: hard-deletes the row (its blobs must already be gone).</summary>
    void Delete(AttachmentFile attachment);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
