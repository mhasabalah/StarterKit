namespace Notification.Tests.Fakes;

internal sealed class FakeNotificationLogRepository : INotificationLogRepository
{
    public List<NotificationLog> Rows { get; } = new();

    public Task InsertAsync(NotificationLog log, CancellationToken ct = default)
    {
        Rows.Add(log);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(Guid id, NotificationDeliveryStatus status, string? error, DateTime? sentAt,
        string? hangfireJobId, CancellationToken ct = default)
    {
        var row = Rows.Single(r => r.Id == id);
        row.Status = status;
        row.Error = error;
        row.SentAt = sentAt;
        if (hangfireJobId is not null)
            row.HangfireJobId = hangfireJobId;
        return Task.CompletedTask;
    }

    public Task<NotificationLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Rows.SingleOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<NotificationLog>> GetByCorrelationAsync(Guid correlationId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NotificationLog>>(
            Rows.Where(r => r.CorrelationId == correlationId).OrderBy(r => r.CreatedAt).ToList());

    public Task<int> PurgeOlderThanAsync(DateTime threshold, int batchSize, CancellationToken ct = default)
        => Task.FromResult(Rows.RemoveAll(r => r.CreatedAt < threshold));

    public Task<int> CountUndeliveredSinceAsync(DateTime since, CancellationToken ct = default)
        => Task.FromResult(Rows.Where(r => r.CreatedAt >= since).GroupBy(r => r.CorrelationId).Count(g =>
            g.Any(r => r.Status == NotificationDeliveryStatus.Failed)
            && g.All(r => r.Status != NotificationDeliveryStatus.Sent)));
}
