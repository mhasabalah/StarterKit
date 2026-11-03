using Microsoft.EntityFrameworkCore.Storage;

namespace Attachment.Data.Repository.Attachments;

public interface IPendingAttachmentRepository
{
    Task<PendingAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live (not deleted, not associated) attachments of a session, in display order.</summary>
    Task<List<PendingAttachment>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<int> GetCountBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<bool> DisplayOrderExistsAsync(Guid sessionId, int displayOrder, CancellationToken cancellationToken = default);

    /// <summary>Expired, unassociated, not deleted rows, oldest first, skipping <paramref name="excludeIds"/>.</summary>
    Task<List<PendingAttachment>> GetExpiredUnassociatedAsync(
        DateTime cutoffDate,
        int batchSize,
        IReadOnlyCollection<Guid> excludeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a transaction and takes a PostgreSQL transaction-scoped advisory lock for the session, so
    /// concurrent uploads to one session are serialized (no limit race, no unique-index 500).
    /// </summary>
    Task<IDbContextTransaction> BeginSessionLockAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task AddAsync(PendingAttachment pendingAttachment, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
