namespace Shared.Notification.Persistence;

public sealed class NotificationLogRepository(NotificationDbContext db)
{
    public async Task InsertAsync(NotificationLog log, CancellationToken ct = default)
    {
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

    public async Task<int> PurgeOlderThanAsync(DateTime threshold, CancellationToken ct = default)
        => await db.NotificationLogs
            .Where(x => x.CreatedAt < threshold)
            .ExecuteDeleteAsync(ct);
}
