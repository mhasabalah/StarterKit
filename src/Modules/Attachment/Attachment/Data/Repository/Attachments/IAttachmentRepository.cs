namespace Attachment.Data.Repository.Attachments;

public interface IAttachmentRepository
{
    // Get operations
    Task<AttachmentFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    Task<List<AttachmentFile>> GetByEntityAsync(
        EntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);
    
    Task<List<AttachmentFile>> GetByEntityAndTypeAsync(
        EntityType entityType,
        Guid entityId,
        AttachmentType type,
        CancellationToken cancellationToken = default);

    Task<(List<AttachmentFile> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId,
        AttachmentCategory? category = null,
        AttachmentType? type = null,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    Task<(List<AttachmentFile> Items, int TotalCount)> GetByTypeAsync(
        AttachmentType type,
        EntityType? entityType = null,
        Guid? entityId = null,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    Task<(List<AttachmentFile> Items, int TotalCount)> GetByCategoryAsync(
        AttachmentCategory category,
        bool publicOnly = false,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    // Command operations
    Task AddAsync(AttachmentFile attachment, CancellationToken cancellationToken = default);
    void Update(AttachmentFile attachment);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
