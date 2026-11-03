namespace Notification.Persistence;

public interface INotificationLogRepository
{
    Task InsertAsync(NotificationLog log, CancellationToken ct = default);

    Task UpdateStatusAsync(
        Guid id,
        NotificationDeliveryStatus status,
        string? error,
        DateTime? sentAt,
        string? hangfireJobId,
        CancellationToken ct = default);

    Task<NotificationLog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>All attempt rows of one dispatch (one correlation id), oldest first, without message_json.</summary>
    Task<IReadOnlyList<NotificationLog>> GetByCorrelationAsync(Guid correlationId, CancellationToken ct = default);

    Task<int> PurgeOlderThanAsync(DateTime threshold, int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Notifications (correlation ids) with a failed attempt at or after <paramref name="since"/> and no delivered
    /// attempt in that window - what an operator has to look at.
    /// </summary>
    Task<int> CountUndeliveredSinceAsync(DateTime since, CancellationToken ct = default);
}

/// <param name="db">The notification log context (host database).</param>
/// <param name="tenantContext">Ambient tenant (restored in jobs by the tenant job filter): stamps <see cref="NotificationLog.TenantId"/>.</param>
public sealed class NotificationLogRepository(NotificationDbContext db, ITenantContext? tenantContext = null)
    : INotificationLogRepository
{
    public async Task InsertAsync(NotificationLog log, CancellationToken ct = default)
    {
        log.TenantId ??= tenantContext is { MultiTenancyEnabled: true, IsHostScope: false } ? tenantContext.TenantId : null;
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(
        Guid id,
        NotificationDeliveryStatus status,
        string? error,
        DateTime? sentAt,
        string? hangfireJobId,
        CancellationToken ct = default)
    {
        var query = db.NotificationLogs.Where(x => x.Id == id);

        if (hangfireJobId is not null)
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.SentAt, sentAt)
                .SetProperty(x => x.HangfireJobId, hangfireJobId), ct);
        }
        else
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.SentAt, sentAt), ct);
        }
    }

    public async Task<NotificationLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.NotificationLogs.FindAsync([id], ct);

    public async Task<IReadOnlyList<NotificationLog>> GetByCorrelationAsync(Guid correlationId, CancellationToken ct = default)
        => await db.NotificationLogs
            .AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new NotificationLog
            {
                Id = x.Id,
                CorrelationId = x.CorrelationId,
                UserId = x.UserId,
                TenantId = x.TenantId,
                Channel = x.Channel,
                TemplateName = x.TemplateName,
                Status = x.Status,
                Error = x.Error,
                HangfireJobId = x.HangfireJobId,
                Culture = x.Culture,
                CreatedAt = x.CreatedAt,
                SentAt = x.SentAt
            })
            .ToListAsync(ct);

    public async Task<int> CountUndeliveredSinceAsync(DateTime since, CancellationToken ct = default)
        => await db.NotificationLogs
            .AsNoTracking()
            .Where(x => x.CreatedAt >= since)
            .GroupBy(x => x.CorrelationId)
            .Where(g => g.Count(x => x.Status == NotificationDeliveryStatus.Failed) > 0
                        && g.Count(x => x.Status == NotificationDeliveryStatus.Sent) == 0)
            .CountAsync(ct);

    /// <summary>Deletes rows older than <paramref name="threshold"/> in batches (no long table locks).</summary>
    public async Task<int> PurgeOlderThanAsync(DateTime threshold, int batchSize, CancellationToken ct = default)
    {
        batchSize = Math.Max(1, batchSize);
        var total = 0;
        while (true)
        {
            var ids = await db.NotificationLogs
                .AsNoTracking()
                .Where(x => x.CreatedAt < threshold)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (ids.Count == 0)
                return total;

            total += await db.NotificationLogs.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);

            if (ids.Count < batchSize)
                return total;
        }
    }
}
