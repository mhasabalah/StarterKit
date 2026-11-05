using Notification.Contracts.Inbox;
using Notification.Contracts.Realtime;

namespace Notification.Tests.Fakes;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>In-memory inbox with the SAME uniqueness rule as the table (user_id, correlation_id).</summary>
internal sealed class FakeInboxRepository : IInboxNotificationRepository
{
    public List<InboxNotification> Rows { get; } = [];

    public Exception? FailOnInsert { get; set; }

    public Task<bool> TryAddAsync(InboxNotification entry, CancellationToken ct = default)
    {
        if (FailOnInsert is not null)
            throw FailOnInsert;
        if (Rows.Any(r => r.UserId == entry.UserId && r.CorrelationId == entry.CorrelationId))
            return Task.FromResult(false);
        Rows.Add(entry);
        return Task.FromResult(true);
    }

    public Task<InboxNotification?> GetByCorrelationAsync(Guid userId, Guid correlationId, CancellationToken ct = default) =>
        Task.FromResult(Rows.SingleOrDefault(r => r.UserId == userId && r.CorrelationId == correlationId));

    public Task<InboxNotification?> GetForUserAsync(Guid id, Guid userId, Guid? tenantId, CancellationToken ct = default) =>
        Task.FromResult(Rows.AsQueryable().VisibleTo(userId, tenantId).SingleOrDefault(r => r.Id == id));

    public Task<int> MarkAllReadAsync(Guid userId, Guid? tenantId, DateTime upTo, DateTime readAt, CancellationToken ct = default)
    {
        var rows = Rows.AsQueryable().VisibleTo(userId, tenantId).Where(r => r.ReadAt == null && r.CreatedAt <= upTo).ToList();
        rows.ForEach(r => r.ReadAt = readAt);
        return Task.FromResult(rows.Count);
    }

    public Task<int> CountUnreadAsync(Guid userId, Guid? tenantId, CancellationToken ct = default) =>
        Task.FromResult(Rows.AsQueryable().VisibleTo(userId, tenantId).Count(r => r.ReadAt == null));

    public void Remove(InboxNotification entry) => Rows.Remove(entry);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> PurgeExpiredAsync(DateTime now, int batchSize, CancellationToken ct = default) =>
        Task.FromResult(Rows.RemoveAll(r => r.ExpiresAt < now));
}

internal sealed class FakePushDeviceRepository : IPushDeviceRepository
{
    public List<PushDevice> Rows { get; } = [];

    public PushDevice Add(Guid userId, Guid? sessionId = null, PushPlatform platform = PushPlatform.Android)
    {
        var device = new PushDevice
        {
            Id = Guid.NewGuid(), UserId = userId, Token = "tok-" + Guid.NewGuid().ToString("N"), Platform = platform,
            SessionId = sessionId, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
        };
        Rows.Add(device);
        return device;
    }

    public Task<PushDevice?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        Task.FromResult(Rows.SingleOrDefault(r => r.Token == token));

    public Task<PushDevice?> GetForUserAsync(Guid id, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Rows.SingleOrDefault(r => r.Id == id && r.UserId == userId));

    public Task<List<PushDevice>> ListForUserAsync(Guid userId, IReadOnlyCollection<Guid>? ids = null, CancellationToken ct = default) =>
        Task.FromResult(Rows.Where(r => r.UserId == userId && (ids == null || ids.Contains(r.Id)))
            .OrderByDescending(r => r.LastSeenAt).ToList());

    void IPushDeviceRepository.Add(PushDevice device) => Rows.Add(device);

    public void Remove(PushDevice device) => Rows.Remove(device);

    public Task<int> DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult(Rows.RemoveAll(r => ids.Contains(r.Id)));

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> PurgeStaleAsync(DateTime threshold, int batchSize, CancellationToken ct = default) =>
        Task.FromResult(Rows.RemoveAll(r => r.LastSeenAt < threshold));
}

/// <summary>Records every real-time event; optionally throws like a dead Redis backplane would.</summary>
internal sealed class RecordingRealtimeNotifier : IRealtimeNotifier
{
    public List<(Guid User, InboxNotificationDto Dto)> Received { get; } = [];
    public List<(Guid User, Guid? Tenant, int Count)> Counts { get; } = [];
    public List<(Guid User, NotificationsReadEvent Read)> Reads { get; } = [];
    public List<(Guid User, Guid Id)> Deleted { get; } = [];

    public Task NotificationReceivedAsync(Guid userId, InboxNotificationDto notification, CancellationToken ct = default)
    {
        Received.Add((userId, notification));
        return Task.CompletedTask;
    }

    public Task UnreadCountChangedAsync(Guid userId, Guid? tenantId, int count, CancellationToken ct = default)
    {
        Counts.Add((userId, tenantId, count));
        return Task.CompletedTask;
    }

    public Task NotificationsReadAsync(Guid userId, NotificationsReadEvent read, CancellationToken ct = default)
    {
        Reads.Add((userId, read));
        return Task.CompletedTask;
    }

    public Task NotificationDeletedAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        Deleted.Add((userId, notificationId));
        return Task.CompletedTask;
    }
}

/// <summary>A push provider whose per-device outcome the test decides.</summary>
internal sealed class FakePushProvider : IPushProvider
{
    public List<(PushMessage Message, IReadOnlyList<PushTarget> Targets)> Calls { get; } = [];

    public Func<PushTarget, PushSendOutcome> Outcome { get; set; } = _ => PushSendOutcome.Delivered;

    public string Name => "Fake";

    public Task<IReadOnlyList<PushSendResult>> SendAsync(PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct)
    {
        Calls.Add((message, targets));
        IReadOnlyList<PushSendResult> results = targets
            .Select(t => new PushSendResult(t.DeviceId, Outcome(t), Outcome(t) == PushSendOutcome.Delivered ? null : "fake error"))
            .ToList();
        return Task.FromResult(results);
    }
}
