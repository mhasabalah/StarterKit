namespace Attachment.Data.Repository.Attachments;

public class AttachmentRepository(AttachmentDbContext context) : IAttachmentRepository
{
    public async Task<AttachmentFile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await context.Attachments
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<List<AttachmentFile>> GetByEntityAsync(
        EntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        return await context.Attachments
            .Where(a => a.EntityType == entityType && a.EntityId == entityId && !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AttachmentFile>> GetByEntityAndTypeAsync(
        EntityType entityType,
        Guid entityId,
        AttachmentType type,
        CancellationToken cancellationToken = default)
    {
        return await context.Attachments
            .Where(a => a.EntityType == entityType 
                && a.EntityId == entityId 
                && a.Type == type
                && !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<AttachmentFile> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId,
        AttachmentCategory? category = null,
        AttachmentType? type = null,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = context.Attachments
            .Where(a => a.UploadedBy == userId && !a.IsDeleted);

        if (category.HasValue)
        {
            query = query.Where(a => a.Category == category.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(a => a.Type == type.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(List<AttachmentFile> Items, int TotalCount)> GetByTypeAsync(
        AttachmentType type,
        EntityType? entityType = null,
        Guid? entityId = null,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = context.Attachments
            .Where(a => a.Type == type && !a.IsDeleted);

        if (entityType.HasValue)
        {
            query = query.Where(a => a.EntityType == entityType.Value);
        }

        if (entityId.HasValue)
        {
            query = query.Where(a => a.EntityId == entityId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(List<AttachmentFile> Items, int TotalCount)> GetByCategoryAsync(
        AttachmentCategory category,
        bool publicOnly = false,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = context.Attachments
            .Where(a => a.Category == category && !a.IsDeleted);

        if (publicOnly)
        {
            query = query.Where(a => a.IsPublic);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(
        AttachmentFile attachment,
        CancellationToken cancellationToken = default)
    {
        await context.Attachments.AddAsync(attachment, cancellationToken);
    }

    public void Update(AttachmentFile attachment)
    {
        context.Attachments.Update(attachment);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var attachment = await context.Attachments
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (attachment == null)
        {
            return false;
        }

        attachment.MarkAsDeleted(); // raises AttachmentDeletedEvent — collected by interceptor before DELETE
        context.Attachments.Remove(attachment);
        return true;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await context.SaveChangesAsync(cancellationToken);
    }
}
