using Attachment.Domains.Attachments.Models;

namespace Attachment.Data.Repository.Attachments;

public interface IPendingAttachmentRepository
{
    Task<PendingAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<PendingAttachment>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<int> GetCountBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<List<PendingAttachment>> GetExpiredUnassociatedAsync(
        DateTime cutoffDate,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<(int totalCount, DateTime? oldestUploadDate)> GetSessionMetadataAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task AddAsync(PendingAttachment pendingAttachment, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    void Remove(PendingAttachment pendingAttachment);

    void RemoveRange(IEnumerable<PendingAttachment> pendingAttachments);
}
