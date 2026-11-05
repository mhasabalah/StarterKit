using Npgsql;

namespace Notification.Persistence;

/// <summary>
/// Which inbox entries a request sees. The inbox is per USER; with multi-tenancy on, a request in tenant T sees T's
/// entries plus platform notices (no tenant). Single-tenant mode (or host scope) sees all of the user's entries.
/// </summary>
public static class InboxScope
{
    /// <summary>The tenant an inbox entry is stamped with / filtered by, from the ambient tenant.</summary>
    public static Guid? CurrentTenantId(ITenantContext? tenantContext) =>
        tenantContext is { MultiTenancyEnabled: true, IsResolved: true, IsHostScope: false } ? tenantContext.TenantId : null;

    public static IQueryable<InboxNotification> VisibleTo(this IQueryable<InboxNotification> query, Guid userId, Guid? tenantId) =>
        tenantId is { } t
            ? query.Where(x => x.UserId == userId && (x.TenantId == t || x.TenantId == null))
            : query.Where(x => x.UserId == userId);
}

public interface IInboxNotificationRepository
{
    /// <summary>
    /// Inserts <paramref name="entry"/> unless the user already has the entry of that correlation id
    /// (<c>ON CONFLICT DO NOTHING</c>). Returns false when it already existed - a retried job, not an error.
    /// </summary>
    Task<bool> TryAddAsync(InboxNotification entry, CancellationToken ct = default);

    Task<InboxNotification?> GetByCorrelationAsync(Guid userId, Guid correlationId, CancellationToken ct = default);

    /// <summary>The user's entry, tracked, within the tenant scope; null when unknown, foreign or out of scope.</summary>
    Task<InboxNotification?> GetForUserAsync(Guid id, Guid userId, Guid? tenantId, CancellationToken ct = default);

    /// <summary>Marks unread entries created at or before <paramref name="upTo"/> read (one statement); returns the count.</summary>
    Task<int> MarkAllReadAsync(Guid userId, Guid? tenantId, DateTime upTo, DateTime readAt, CancellationToken ct = default);

    Task<int> CountUnreadAsync(Guid userId, Guid? tenantId, CancellationToken ct = default);

    void Remove(InboxNotification entry);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Deletes expired entries in batches (no long locks); returns the total.</summary>
    Task<int> PurgeExpiredAsync(DateTime now, int batchSize, CancellationToken ct = default);
}

public sealed class InboxNotificationRepository(NotificationDbContext db) : IInboxNotificationRepository
{
    public async Task<bool> TryAddAsync(InboxNotification entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Raw insert: EF has no ON CONFLICT, and a unique-violation exception would poison the context for the retry.
        var data = entry.Data is null ? null : JsonSerializer.Serialize(entry.Data);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO notification.inbox_notification
                (id, user_id, tenant_id, correlation_id, category, severity, title, body, action_url, data,
                 created_at, read_at, expires_at)
            VALUES
                ({entry.Id}, {entry.UserId}, {entry.TenantId}, {entry.CorrelationId}, {entry.Category},
                 {entry.Severity.ToString()}, {entry.Title}, {entry.Body}, {entry.ActionUrl},
                 {new NpgsqlParameter { Value = (object?)data ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb }},
                 {entry.CreatedAt}, NULL, {entry.ExpiresAt})
            ON CONFLICT (user_id, correlation_id) DO NOTHING
            """, ct);
        return inserted == 1;
    }

    public Task<InboxNotification?> GetByCorrelationAsync(Guid userId, Guid correlationId, CancellationToken ct = default) =>
        db.InboxNotifications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.CorrelationId == correlationId, ct);

    public Task<InboxNotification?> GetForUserAsync(Guid id, Guid userId, Guid? tenantId, CancellationToken ct = default) =>
        db.InboxNotifications.VisibleTo(userId, tenantId).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<int> MarkAllReadAsync(Guid userId, Guid? tenantId, DateTime upTo, DateTime readAt, CancellationToken ct = default) =>
        db.InboxNotifications.VisibleTo(userId, tenantId)
            .Where(x => x.ReadAt == null && x.CreatedAt <= upTo)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, readAt), ct);

    public Task<int> CountUnreadAsync(Guid userId, Guid? tenantId, CancellationToken ct = default) =>
        db.InboxNotifications.AsNoTracking().VisibleTo(userId, tenantId).CountAsync(x => x.ReadAt == null, ct);

    public void Remove(InboxNotification entry) => db.InboxNotifications.Remove(entry);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<int> PurgeExpiredAsync(DateTime now, int batchSize, CancellationToken ct = default)
    {
        batchSize = Math.Max(1, batchSize);
        var total = 0;
        while (true)
        {
            var ids = await db.InboxNotifications.AsNoTracking()
                .Where(x => x.ExpiresAt < now)
                .OrderBy(x => x.ExpiresAt)
                .Select(x => x.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (ids.Count == 0)
                return total;

            total += await db.InboxNotifications.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);

            if (ids.Count < batchSize)
                return total;
        }
    }
}
