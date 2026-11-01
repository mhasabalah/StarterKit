using Attachment.Domains.Attachments.Models;

namespace Attachment.Data.Repository.Attachments;

public class PendingAttachmentRepository : IPendingAttachmentRepository
{
    private readonly AttachmentDbContext _context;

    public PendingAttachmentRepository(AttachmentDbContext context)
    {
        _context = context;
    }

    public async Task<PendingAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.PendingAttachments
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<List<PendingAttachment>> GetBySessionIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PendingAttachments
            .Where(p => p.SessionId == sessionId && !p.IsDeleted && !p.IsAssociated)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountBySessionIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PendingAttachments
            .CountAsync(p => p.SessionId == sessionId && !p.IsDeleted && !p.IsAssociated, cancellationToken);
    }

    public async Task<List<PendingAttachment>> GetExpiredUnassociatedAsync(
        DateTime cutoffDate,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _context.PendingAttachments
            .Where(p => p.ExpiresAt < cutoffDate && !p.IsAssociated && !p.IsDeleted)
            .OrderBy(p => p.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<(int totalCount, DateTime? oldestUploadDate)> GetSessionMetadataAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var attachments = await _context.PendingAttachments
            .Where(p => p.SessionId == sessionId && !p.IsDeleted && !p.IsAssociated)
            .Select(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return (attachments.Count, attachments.Any() ? attachments.Min() : null);
    }

    public async Task AddAsync(PendingAttachment pendingAttachment, CancellationToken cancellationToken = default)
    {
        await _context.PendingAttachments.AddAsync(pendingAttachment, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public void Remove(PendingAttachment pendingAttachment)
    {
        _context.PendingAttachments.Remove(pendingAttachment);
    }

    public void RemoveRange(IEnumerable<PendingAttachment> pendingAttachments)
    {
        _context.PendingAttachments.RemoveRange(pendingAttachments);
    }
}
