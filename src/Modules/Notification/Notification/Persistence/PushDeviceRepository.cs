namespace Notification.Persistence;

public interface IPushDeviceRepository
{
    Task<PushDevice?> GetByTokenAsync(string token, CancellationToken ct = default);

    Task<PushDevice?> GetForUserAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>The user's devices (tracked), most recently seen first; optionally only <paramref name="ids"/>.</summary>
    Task<List<PushDevice>> ListForUserAsync(Guid userId, IReadOnlyCollection<Guid>? ids = null, CancellationToken ct = default);

    void Add(PushDevice device);

    void Remove(PushDevice device);

    Task<int> DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Deletes devices not seen since <paramref name="threshold"/>, in batches; returns the total.</summary>
    Task<int> PurgeStaleAsync(DateTime threshold, int batchSize, CancellationToken ct = default);
}

public sealed class PushDeviceRepository(NotificationDbContext db) : IPushDeviceRepository
{
    public Task<PushDevice?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        db.PushDevices.FirstOrDefaultAsync(x => x.Token == token, ct);

    public Task<PushDevice?> GetForUserAsync(Guid id, Guid userId, CancellationToken ct = default) =>
        db.PushDevices.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    public Task<List<PushDevice>> ListForUserAsync(Guid userId, IReadOnlyCollection<Guid>? ids = null, CancellationToken ct = default)
    {
        var query = db.PushDevices.Where(x => x.UserId == userId);
        if (ids is not null)
            query = query.Where(x => ids.Contains(x.Id));
        return query.OrderByDescending(x => x.LastSeenAt).ThenBy(x => x.Id).ToListAsync(ct);
    }

    public void Add(PushDevice device) => db.PushDevices.Add(device);

    public void Remove(PushDevice device) => db.PushDevices.Remove(device);

    public async Task<int> DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        ids.Count == 0 ? 0 : await db.PushDevices.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<int> PurgeStaleAsync(DateTime threshold, int batchSize, CancellationToken ct = default)
    {
        batchSize = Math.Max(1, batchSize);
        var total = 0;
        while (true)
        {
            var ids = await db.PushDevices.AsNoTracking()
                .Where(x => x.LastSeenAt < threshold)
                .OrderBy(x => x.LastSeenAt)
                .Select(x => x.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (ids.Count == 0)
                return total;

            total += await db.PushDevices.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);

            if (ids.Count < batchSize)
                return total;
        }
    }
}
