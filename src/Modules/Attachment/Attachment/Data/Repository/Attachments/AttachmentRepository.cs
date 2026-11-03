using Shared.Core.MultiTenancy.Data;
namespace Attachment.Data.Repository.Attachments;

/// <summary>
/// See <see cref="IAttachmentRepository"/>. The read filter (Ready + not deleted) is the global query filter of
/// <see cref="AttachmentDbContext"/>; only the maintenance methods call <c>IgnoreNonIsolationFilters</c>.
/// </summary>
public class AttachmentRepository(AttachmentDbContext context) : IAttachmentRepository
{
    public Task<AttachmentFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<AttachmentFile?> GetReadOnlyByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<List<AttachmentFile>> GetByEntityAsync(
        EntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default) =>
        context.Attachments
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<(List<AttachmentFile> Items, int TotalCount)> GetByEntityVisiblePageAsync(
        EntityType entityType,
        Guid entityId,
        bool includePrivate,
        Guid? uploaderId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Attachments
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId);

        if (!includePrivate)
        {
            query = uploaderId is { } uid
                ? query.Where(a => a.IsPublic || a.UploadedBy == uid)
                : query.Where(a => a.IsPublic);
        }

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return ([], 0);

        var skip = (int)Math.Min(((long)pageNumber - 1) * pageSize, int.MaxValue);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<List<AttachmentFile>> GetLiveByEntityAsync(
        EntityType entityType,
        Guid entityId,
        CancellationToken cancellationToken = default) =>
        context.Attachments
            .IgnoreNonIsolationFilters()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId && !a.IsDeleted &&
                        (a.Status == AttachmentStatus.Ready || a.Status == AttachmentStatus.Uploading))
            .ToListAsync(cancellationToken);

    public Task<AttachmentFile?> GetForMaintenanceAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Attachments.IgnoreNonIsolationFilters().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task AddAsync(AttachmentFile attachment, CancellationToken cancellationToken = default) =>
        await context.Attachments.AddAsync(attachment, cancellationToken);

    public void Delete(AttachmentFile attachment) => context.Attachments.Remove(attachment);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
