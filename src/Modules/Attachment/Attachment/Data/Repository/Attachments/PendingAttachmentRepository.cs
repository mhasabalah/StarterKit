using Microsoft.EntityFrameworkCore.Storage;

namespace Attachment.Data.Repository.Attachments;

public class PendingAttachmentRepository(AttachmentDbContext context) : IPendingAttachmentRepository
{
    public Task<PendingAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.PendingAttachments.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

    public Task<List<PendingAttachment>> GetBySessionIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        context.PendingAttachments
            .Where(p => p.SessionId == sessionId && !p.IsDeleted && !p.IsAssociated)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> GetCountBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        context.PendingAttachments
            .CountAsync(p => p.SessionId == sessionId && !p.IsDeleted && !p.IsAssociated, cancellationToken);

    public Task<bool> DisplayOrderExistsAsync(Guid sessionId, int displayOrder, CancellationToken cancellationToken = default) =>
        context.PendingAttachments
            .AnyAsync(p => p.SessionId == sessionId && p.DisplayOrder == displayOrder && !p.IsDeleted, cancellationToken);

    public Task<List<PendingAttachment>> GetExpiredUnassociatedAsync(
        DateTime cutoffDate,
        int batchSize,
        IReadOnlyCollection<Guid> excludeIds,
        CancellationToken cancellationToken = default)
    {
        var query = context.PendingAttachments
            .Where(p => p.ExpiresAt < cutoffDate && !p.IsAssociated && !p.IsDeleted);

        if (excludeIds.Count > 0)
            query = query.Where(p => !excludeIds.Contains(p.Id));

        return query.OrderBy(p => p.ExpiresAt).Take(batchSize).ToListAsync(cancellationToken);
    }

    public async Task<IDbContextTransaction> BeginSessionLockAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var key = BitConverter.ToInt64(sessionId.ToByteArray(), 0);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
        return transaction;
    }

    public async Task AddAsync(PendingAttachment pendingAttachment, CancellationToken cancellationToken = default) =>
        await context.PendingAttachments.AddAsync(pendingAttachment, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
